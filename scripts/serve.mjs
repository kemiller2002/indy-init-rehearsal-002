// Minimal static server for dist/site (local development and browser tests). Serves files only; no proxying.
import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize, sep } from "node:path";
import { fileURLToPath } from "node:url";

const root = normalize(process.argv[2] ?? join(fileURLToPath(new URL(".", import.meta.url)), "..", "dist", "site"));
const port = Number(process.env.PORT ?? process.argv[3] ?? 8080);
const types = {
  ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".mjs": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8", ".json": "application/json", ".svg": "image/svg+xml", ".wasm": "application/wasm",
  ".dat": "application/octet-stream", ".pdb": "application/octet-stream", ".txt": "text/plain; charset=utf-8",
};

const resolvePath = (url) => {
  const path = decodeURIComponent(new URL(url, "http://localhost").pathname);
  const candidate = normalize(join(root, path));
  return candidate === root || candidate.startsWith(root + sep) ? candidate : null;
};

createServer(async (req, res) => {
  const target = resolvePath(req.url ?? "/");
  if (target === null) { res.writeHead(403).end(); return; }
  try {
    const info = await stat(target);
    const file = info.isDirectory() ? join(target, "index.html") : target;
    const body = await readFile(file);
    res.writeHead(200, { "content-type": types[extname(file)] ?? "application/octet-stream", "cache-control": "no-store" }).end(body);
  } catch {
    res.writeHead(404, { "content-type": "text/plain" }).end("not found");
  }
}).listen(port, "127.0.0.1", () => console.log(`Causa: http://127.0.0.1:${port}/`));
