#!/usr/bin/env node

"use strict";

const crypto = require("node:crypto");
const net = require("node:net");

const CONTROL_PORT = Number(process.env.UB_RELAY_CONTROL_PORT || 28080);
const PUBLIC_PORT_MIN = Number(process.env.UB_RELAY_PORT_MIN || 30000);
const PUBLIC_PORT_MAX = Number(process.env.UB_RELAY_PORT_MAX || 39999);
const RELAY_TOKEN = process.env.UB_RELAY_TOKEN || "";
const MAX_SESSIONS = Number(process.env.UB_RELAY_MAX_SESSIONS || 200);
const MAX_SESSIONS_PER_IP = Number(process.env.UB_RELAY_MAX_SESSIONS_PER_IP || 5);
const MAX_PLAYERS_PER_SESSION = Number(process.env.UB_RELAY_MAX_PLAYERS || 50);
const MAX_FRAME_SIZE = 1024 * 1024;

if (!RELAY_TOKEN || RELAY_TOKEN.length < 24) {
  throw new Error("UB_RELAY_TOKEN must contain at least 24 characters");
}

const sessions = new Set();
const sessionCountsByIp = new Map();

function normalizeIp(address) {
  return (address || "unknown").replace(/^::ffff:/, "");
}

function makeFrame(type, connectionId, payload = Buffer.alloc(0)) {
  const header = Buffer.allocUnsafe(9);
  header.writeUInt8(type, 0);
  header.writeUInt32BE(connectionId >>> 0, 1);
  header.writeUInt32BE(payload.length >>> 0, 5);
  return payload.length === 0 ? header : Buffer.concat([header, payload]);
}

function safeWrite(socket, data) {
  if (!socket.destroyed && socket.writable) {
    socket.write(data);
  }
}

function closeSession(session, reason) {
  if (session.closed) return;
  session.closed = true;
  sessions.delete(session);
  const current = sessionCountsByIp.get(session.ip) || 1;
  if (current <= 1) sessionCountsByIp.delete(session.ip);
  else sessionCountsByIp.set(session.ip, current - 1);

  for (const player of session.players.values()) player.destroy();
  session.players.clear();
  if (session.publicServer) session.publicServer.close();
  if (!session.control.destroyed) session.control.destroy();
  console.log(`${new Date().toISOString()} relay closed ip=${session.ip} port=${session.port || "-"} reason=${reason}`);
}

function sendClose(session, connectionId) {
  safeWrite(session.control, makeFrame(3, connectionId));
}

function attachPublicPlayer(session, player) {
  if (session.players.size >= MAX_PLAYERS_PER_SESSION) {
    player.destroy();
    return;
  }

  let connectionId = session.nextConnectionId++ >>> 0;
  if (connectionId === 0) connectionId = session.nextConnectionId++ >>> 0;
  session.players.set(connectionId, player);
  safeWrite(session.control, makeFrame(1, connectionId));

  player.on("data", (chunk) => safeWrite(session.control, makeFrame(2, connectionId, chunk)));
  player.on("close", () => {
    if (session.players.delete(connectionId)) sendClose(session, connectionId);
  });
  player.on("error", () => player.destroy());
}

function handleFrame(session, type, connectionId, payload) {
  if (type === 2) {
    const player = session.players.get(connectionId);
    if (player && !player.destroyed) player.write(payload);
    return;
  }

  if (type === 3) {
    const player = session.players.get(connectionId);
    if (player) {
      session.players.delete(connectionId);
      player.destroy();
    }
    return;
  }

  if (type === 4) {
    safeWrite(session.control, makeFrame(5, connectionId));
    return;
  }

  closeSession(session, `invalid frame type ${type}`);
}

function consumeFrames(session, chunk) {
  session.buffer = session.buffer.length === 0 ? chunk : Buffer.concat([session.buffer, chunk]);
  while (session.buffer.length >= 9) {
    const type = session.buffer.readUInt8(0);
    const connectionId = session.buffer.readUInt32BE(1);
    const length = session.buffer.readUInt32BE(5);
    if (length > MAX_FRAME_SIZE) {
      closeSession(session, "oversized frame");
      return;
    }
    if (session.buffer.length < 9 + length) return;
    const payload = session.buffer.subarray(9, 9 + length);
    session.buffer = session.buffer.subarray(9 + length);
    handleFrame(session, type, connectionId, payload);
  }
}

function startPublicListener(session, attempts = 0) {
  if (attempts >= 80) {
    safeWrite(session.control, "ERROR no-free-port\n");
    closeSession(session, "no free public port");
    return;
  }

  const port = crypto.randomInt(PUBLIC_PORT_MIN, PUBLIC_PORT_MAX + 1);
  const publicServer = net.createServer((player) => attachPublicPlayer(session, player));
  publicServer.on("error", (error) => {
    if (error.code === "EADDRINUSE") startPublicListener(session, attempts + 1);
    else closeSession(session, `public listener: ${error.message}`);
  });
  publicServer.listen({ host: "0.0.0.0", port, exclusive: true }, () => {
    session.port = port;
    session.publicServer = publicServer;
    safeWrite(session.control, `PORT ${port}\n`);
    console.log(`${new Date().toISOString()} relay active ip=${session.ip} port=${port}`);
  });
}

function acceptControl(control) {
  const ip = normalizeIp(control.remoteAddress);
  control.setNoDelay(true);
  control.setKeepAlive(true, 30000);

  let handshakeBuffer = Buffer.alloc(0);
  const handshakeTimeout = setTimeout(() => control.destroy(), 10000);

  const onHandshakeData = (chunk) => {
    handshakeBuffer = Buffer.concat([handshakeBuffer, chunk]);
    if (handshakeBuffer.length > 512) {
      control.destroy();
      return;
    }

    const newline = handshakeBuffer.indexOf(10);
    if (newline < 0) return;
    control.off("data", onHandshakeData);
    clearTimeout(handshakeTimeout);

    const line = handshakeBuffer.subarray(0, newline).toString("utf8").trim();
    const remainder = handshakeBuffer.subarray(newline + 1);
    const parts = line.split(/\s+/);
    const suppliedToken = Buffer.from(parts[1] || "");
    const expectedToken = Buffer.from(RELAY_TOKEN);
    const tokenMatches = suppliedToken.length === expectedToken.length &&
      crypto.timingSafeEqual(suppliedToken, expectedToken);
    const ipCount = sessionCountsByIp.get(ip) || 0;

    if (parts[0] !== "UBR1" || !tokenMatches) {
      safeWrite(control, "ERROR unauthorized\n");
      control.end();
      return;
    }
    if (sessions.size >= MAX_SESSIONS || ipCount >= MAX_SESSIONS_PER_IP) {
      safeWrite(control, "ERROR capacity\n");
      control.end();
      return;
    }

    const session = {
      control,
      ip,
      port: 0,
      publicServer: null,
      players: new Map(),
      nextConnectionId: 1,
      buffer: remainder,
      closed: false,
    };
    sessions.add(session);
    sessionCountsByIp.set(ip, ipCount + 1);

    control.on("data", (data) => consumeFrames(session, data));
    control.on("error", (error) => closeSession(session, `control: ${error.message}`));
    control.on("close", () => closeSession(session, "control disconnected"));
    startPublicListener(session);
    if (remainder.length > 0) consumeFrames(session, Buffer.alloc(0));
  };

  control.on("data", onHandshakeData);
  control.on("error", () => control.destroy());
}

const controlServer = net.createServer(acceptControl);
controlServer.on("error", (error) => {
  console.error(error);
  process.exitCode = 1;
});
controlServer.listen({ host: "0.0.0.0", port: CONTROL_PORT }, () => {
  console.log(`UB Relay control listening on ${CONTROL_PORT}; public range ${PUBLIC_PORT_MIN}-${PUBLIC_PORT_MAX}`);
});

function shutdown(signal) {
  console.log(`${signal}: shutting down UB Relay`);
  controlServer.close();
  for (const session of [...sessions]) closeSession(session, signal);
  setTimeout(() => process.exit(0), 500).unref();
}

process.on("SIGTERM", () => shutdown("SIGTERM"));
process.on("SIGINT", () => shutdown("SIGINT"));
