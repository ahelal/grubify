const { test } = require('node:test');
const assert = require('node:assert/strict');
const { getApiHealthUrl, getHealthReport } = require('./server');

test('uses the API health route for the configured API base URL', () => {
  assert.equal(getApiHealthUrl('https://api.example.com/api'), 'https://api.example.com/health');
});

test('reports uptime, memory, and a healthy API dependency', async () => {
  const report = await getHealthReport({
    apiUrl: 'https://api.example.com/health',
    fetchImpl: async () => ({ ok: true, status: 200 }),
    rssBytes: 50,
    memoryLimitBytes: 100
  });

  assert.equal(report.statusCode, 200);
  assert.equal(report.body.status, 'Healthy');
  assert.equal(report.body.checks.memory.utilizationPercent, 50);
  assert.equal(report.body.checks.downstreamServices.services[0].httpStatus, 200);
  assert.ok(report.body.uptimeSeconds >= 0);
});

test('returns an unhealthy status when a dependency or memory check fails', async () => {
  const apiFailure = await getHealthReport({
    fetchImpl: async () => ({ ok: false, status: 503 }),
    rssBytes: 50,
    memoryLimitBytes: 100
  });
  const memoryFailure = await getHealthReport({
    fetchImpl: async () => ({ ok: true, status: 200 }),
    rssBytes: 91,
    memoryLimitBytes: 100
  });

  assert.equal(apiFailure.statusCode, 503);
  assert.equal(apiFailure.body.checks.downstreamServices.status, 'Unhealthy');
  assert.equal(memoryFailure.statusCode, 503);
  assert.equal(memoryFailure.body.checks.memory.status, 'Unhealthy');
});
