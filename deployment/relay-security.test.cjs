const { test } = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const path = require('node:path');

function startRelay(token) {
  return spawnSync(process.execPath, [path.join(__dirname, 'ub-relay-server.js')], {
    env: { ...process.env, UB_RELAY_TOKEN: token, UB_RELAY_TLS_CERT: '', UB_RELAY_TLS_KEY: '' },
    encoding: 'utf8', timeout: 5000, windowsHide: true
  });
}

test('relay refuses missing credentials before listening', () => {
  const result = startRelay('');
  assert.equal(result.status, 1);
  assert.match(result.stderr, /UB_RELAY_TOKEN must contain/);
});

test('relay refuses plaintext control-channel startup', () => {
  const result = startRelay('test-only-credential-not-for-production');
  assert.equal(result.status, 1);
  assert.match(result.stderr, /UB_RELAY_TLS_CERT and UB_RELAY_TLS_KEY are required/);
});
