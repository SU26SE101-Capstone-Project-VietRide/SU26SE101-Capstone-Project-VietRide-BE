import http from 'node:http';

const port = Number.parseInt(process.env.GOOGLE_ROUTES_E2E_STUB_PORT ?? '39091', 10);

const server = http.createServer((request, response) => {
  if (request.method !== 'POST' || request.url !== '/directions/v2:computeRoutes') {
    response.writeHead(404, { 'content-type': 'application/json' });
    response.end(JSON.stringify({ error: 'not_found' }));
    return;
  }

  let body = '';
  request.setEncoding('utf8');
  request.on('data', (chunk) => {
    body += chunk;
  });
  request.on('end', () => {
    try {
      const payload = JSON.parse(body);
      if (!payload?.origin?.location?.latLng || !payload?.destination?.location?.latLng) {
        throw new Error('origin and destination latLng are required');
      }
      response.writeHead(200, { 'content-type': 'application/json' });
      response.end(JSON.stringify({ routes: [{ duration: '3600s', distanceMeters: 50_000 }] }));
    } catch (error) {
      response.writeHead(400, { 'content-type': 'application/json' });
      response.end(JSON.stringify({ error: error.message }));
    }
  });
});

server.listen(port, '0.0.0.0', () => {
  console.log(`Google Routes E2E stub listening on ${port}`);
});

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => server.close(() => process.exit(0)));
}
