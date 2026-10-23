// EXT-DATA-001 / EXT-DATA-009 gate: the application source and the production site contain no demonstration
// investigation records. The frozen causa-data v1.0.0 dataset (fetched test-only into .cache/causa-data by
// tools/data/fetch-causa-data.sh) supplies the needles: every investigation id, title, scenario key and event id,
// and the events/manifest/snapshot/state digests. Any hit in src/ or dist/site/ fails the build.
import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const data = process.env.CAUSA_DATA_DIR ?? join(root, ".cache", "causa-data");
const manifestPath = join(data, "fixtures", "v1.0.0", "manifest.json");
if (!existsSync(manifestPath)) {
  console.error(`no-demo-data: ${manifestPath} missing; run tools/data/fetch-causa-data.sh first`);
  process.exit(2);
}

const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
const eventIds = (id) =>
  readFileSync(join(data, "fixtures", "v1.0.0", "investigations", id, "events.ndjson"), "utf8")
    .split("\n").filter(Boolean).map((line) => JSON.parse(line).eventId);
const needles = manifest.investigations.flatMap((i) => [
  i.investigationId, i.title, `"${i.scenarioKey}"`, i.eventsSha256, i.manifestSha256, i.snapshotSha256, i.expectedStateSha256,
  ...eventIds(i.investigationId),
]);

const walk = (dir) =>
  readdirSync(dir, { withFileTypes: true }).flatMap((e) => {
    const p = join(dir, e.name);
    if (e.isDirectory()) return ["bin", "obj", "node_modules"].includes(e.name) ? [] : walk(p);
    return [p];
  });

const scanned = ["src", "dist/site"].map((d) => join(root, d)).filter(existsSync).flatMap(walk).filter((p) => statSync(p).size < 64 * 1048576);
const hits = scanned.flatMap((file) => {
  const text = readFileSync(file).toString("latin1");
  const utf16 = Buffer.from(text, "latin1").toString("utf16le");
  return needles.filter((n) => text.includes(n) || utf16.includes(n)).map((n) => `${relative(root, file)}: ${n}`);
});

console.log(`no-demo-data: ${scanned.length} files scanned for ${needles.length} demonstration identifiers`);
if (hits.length > 0) {
  console.error(hits.join("\n"));
  process.exit(1);
}
console.log("no-demo-data: PASS (zero demonstration investigations in source or production assets)");
