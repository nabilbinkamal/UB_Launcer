# Security rollout checklist

These source changes have NOT been deployed to the production admin or relay server.

## Exposed credentials: action required

- `sg1gb1.key` was tracked in Git. It has been removed from the index and ignored; the local file was preserved. Revoke the corresponding authorized key/certificate at its actual service and issue a replacement. Do not rely on deleting the file or rewriting history as revocation.
- The previous relay credential was embedded in the client source. Revoke it on the relay and issue a fresh random credential. Do not reuse it or put the replacement in source, build logs, or screenshots.
- Assess repository history, old release binaries, forks and downloaded archives as possible copies. Coordinate any history cleanup separately; no history rewrite was performed here.

## Relay: coordinated client/server deployment required

The control protocol now requires TLS. A newly built client will not connect to the old plaintext relay.

1. Provision a trusted TLS certificate whose hostname matches the client's configured `RelayHost`. Keep certificate renewal and restrictive private-key permissions in the server deployment.
2. Set server `UB_RELAY_TLS_CERT` to the certificate/full-chain file, `UB_RELAY_TLS_KEY` to its key file, and `UB_RELAY_TOKEN` to the new credential (minimum 24 characters; use a cryptographically random value).
3. Review existing `UB_RELAY_CONTROL_PORT`, public port range and session/player limits. Restrict firewall ingress to the required ports only.
4. Provide `UB_RELAY_TOKEN` to the launcher process through an appropriate local secret provisioning mechanism. Do not bake it into the binary. Missing credentials now fail with an actionable error.
5. Stage both ends together, test valid and invalid certificates, wrong/expired credentials, disconnects, reconnects, capacity and slow-client behavior before production rollout.

A shared token distributed to clients is not per-user authorization. It remains extractable by a client owner. A production public launcher should evolve to server-issued, short-lived, scoped credentials with revocation and account-level quotas. TLS only protects the control connection; public Minecraft protocol traffic has its own authentication/encryption semantics.

## Admin deployment

- Back up the existing data directory before deployment. Keep its permissions restricted to the service account; do not place it in a public static directory.
- Serve behind HTTPS. Set `UB_PUBLIC_BASE_URL` to the actual public HTTPS origin and use a strong unique bootstrap password, even though the backward-compatible minimum remains six characters.
- Enable `UB_TRUST_PROXY=1` only for the documented loopback reverse proxy which overwrites `X-Forwarded-For`. Direct deployments should leave it disabled.
- Deploy the matching nginx request-size/header changes with the backend. Test login limits, session revocation, asset uploads and concurrent publishing in staging.
- Partner status checks intentionally refuse private/reserved addresses. Internal/private game servers are no longer probed by this public admin endpoint.
- Corrupted administrator storage now fails closed. Restore a trusted backup instead of expecting automatic bootstrap-account recreation.
- Config ETags prevent conflicts for the updated UI. Legacy API clients that omit `If-Match` still use last-writer-wins behavior.

## Release verification still needed

- Re-run the full Windows Release build in an approved environment. The final local attempt was blocked by Windows Application Control loading `Avalonia.Generators.dll` (0x800711C7). No security policy was disabled or bypassed.
- Exercise Microsoft sign-in/refresh/sign-out with a real authorized account, game downloads and launches, Forge/NeoForge installers, and relay integration in staging.
- Benchmark a fixed game version, modpack, world, graphics settings and hardware with repeatable frametime measurements. No FPS improvement percentage has been measured or claimed.
