const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const v8 = require('node:v8');

const staticRoot = path.join(__dirname, 'build');
const mimeTypes = {
  '.css': 'text/css; charset=utf-8',
  '.eot': 'application/vnd.ms-fontobject',
  '.gif': 'image/gif',
  '.html': 'text/html; charset=utf-8',
  '.ico': 'image/x-icon',
  '.jpeg': 'image/jpeg',
  '.jpg': 'image/jpeg',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.txt': 'text/plain; charset=utf-8',
  '.ttf': 'font/ttf',
  '.webp': 'image/webp',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2'
};

function getApiHealthUrl(apiBaseUrl = process.env.REACT_APP_API_BASE_URL || 'http://localhost:5291/api') {
  const url = new URL(apiBaseUrl);
  url.pathname = `${url.pathname.replace(/\/api\/?$/i, '').replace(/\/$/, '')}/health`;
  return url.toString();
}

function getMemoryLimitBytes() {
  for (const filename of ['/sys/fs/cgroup/memory.max', '/sys/fs/cgroup/memory/memory.limit_in_bytes']) {
    try {
      const value = fs.readFileSync(filename, 'utf8').trim();
      const limit = Number(value);
      if (value !== 'max' && Number.isFinite(limit) && limit > 0 && limit < 1e15) {
        return limit;
      }
    } catch {
      // Try the next source of the memory limit.
    }
  }

  return v8.getHeapStatistics().heap_size_limit;
}

async function getHealthReport({
  apiUrl = getApiHealthUrl(),
  fetchImpl = fetch,
  rssBytes = process.memoryUsage().rss,
  memoryLimitBytes = getMemoryLimitBytes()
} = {}) {
  let apiStatus = 'Unhealthy';
  let apiHttpStatus = null;
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 3000);

  try {
    const response = await fetchImpl(apiUrl, { signal: controller.signal });
    apiHttpStatus = response.status;
    if (response.ok) {
      apiStatus = 'Healthy';
    }
    await response.body?.cancel();
  } catch {
    // An unavailable API makes the frontend unready.
  } finally {
    clearTimeout(timeout);
  }

  const memoryUtilization = memoryLimitBytes > 0 ? rssBytes / memoryLimitBytes : 1;
  const memoryHealthy = memoryUtilization < 0.9;
  const healthy = memoryHealthy && apiStatus === 'Healthy';

  return {
    statusCode: healthy ? 200 : 503,
    body: {
      status: healthy ? 'Healthy' : 'Unhealthy',
      uptimeSeconds: Math.floor(process.uptime()),
      checks: {
        memory: {
          status: memoryHealthy ? 'Healthy' : 'Unhealthy',
          usedBytes: rssBytes,
          limitBytes: memoryLimitBytes,
          utilizationPercent: Math.round(memoryUtilization * 1000) / 10
        },
        downstreamServices: {
          status: apiStatus,
          services: [{ name: 'api', status: apiStatus, httpStatus: apiHttpStatus }]
        }
      }
    }
  };
}

function substituteApiBaseUrl() {
  const apiBaseUrl = process.env.REACT_APP_API_BASE_URL;
  if (!apiBaseUrl) {
    return;
  }

  const jsDirectory = path.join(staticRoot, 'static', 'js');
  if (!fs.existsSync(jsDirectory)) {
    return;
  }

  for (const filename of fs.readdirSync(jsDirectory)) {
    if (!filename.endsWith('.js')) {
      continue;
    }

    const filePath = path.join(jsDirectory, filename);
    const content = fs.readFileSync(filePath, 'utf8');
    const updatedContent = content.replaceAll('http://localhost:5291/api', apiBaseUrl);
    if (updatedContent !== content) {
      fs.writeFileSync(filePath, updatedContent);
    }
  }
}

async function handleRequest(request, response) {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    response.writeHead(405);
    response.end();
    return;
  }

  if (request.url === '/health' || request.url.startsWith('/health?')) {
    const report = await getHealthReport();
    const body = JSON.stringify(report.body);
    response.writeHead(report.statusCode, {
      'Content-Type': 'application/json; charset=utf-8',
      'Content-Length': Buffer.byteLength(body)
    });
    response.end(request.method === 'HEAD' ? undefined : body);
    return;
  }

  try {
    const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
    const filePath = path.resolve(staticRoot, `.${pathname}`);
    if (filePath !== staticRoot && !filePath.startsWith(`${staticRoot}${path.sep}`)) {
      response.writeHead(403);
      response.end();
      return;
    }

    let selectedPath = filePath;
    try {
      if (fs.statSync(selectedPath).isDirectory()) {
        selectedPath = path.join(selectedPath, 'index.html');
      }
      fs.accessSync(selectedPath, fs.constants.R_OK);
    } catch {
      if (path.extname(filePath)) {
        response.writeHead(404);
        response.end();
        return;
      }
      selectedPath = path.join(staticRoot, 'index.html');
    }

    const content = await fs.promises.readFile(selectedPath);
    response.writeHead(200, {
      'Content-Type': mimeTypes[path.extname(selectedPath)] || 'application/octet-stream',
      'Content-Length': content.length
    });
    response.end(request.method === 'HEAD' ? undefined : content);
  } catch {
    response.writeHead(400);
    response.end();
  }
}

if (require.main === module) {
  substituteApiBaseUrl();
  http.createServer((request, response) => {
    handleRequest(request, response).catch(() => {
      if (!response.headersSent) {
        response.writeHead(500);
      }
      response.end();
    });
  }).listen(Number(process.env.PORT) || 80, '0.0.0.0');
}

module.exports = { getApiHealthUrl, getHealthReport };
