// Test-only access to the causa-data checkout (tag causa-storage-v1.0.0) at .cache/causa-data.
// The data is read here, at test time, and handed to the MOCKED GitHub fake; it is never copied into src/ or dist/.
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";

export const dataRoot = resolve(import.meta.dirname, "../../../.cache/causa-data");
export const fixturesRoot = join(dataRoot, "fixtures/v1.0.0");

export const fixturesAvailable = () => existsSync(join(fixturesRoot, "manifest.json"));

/** The dataset manifest: { investigations: [{ investigationId, title, expectedVersion, ... }] }. */
export const datasetManifest = () => JSON.parse(readFileSync(join(fixturesRoot, "manifest.json"), "utf8"));

/** [{ id, title, expectedVersion, expectedLifecycle }] for the five frozen fixture investigations. */
export const fixtureInvestigations = () =>
  datasetManifest().investigations.map((i) => ({
    id: i.investigationId,
    title: i.title,
    expectedVersion: i.expectedVersion,
    expectedLifecycle: i.expectedLifecycle,
  }));

const fileNames = ["manifest.json", "events.ndjson", "snapshot.json"];

/** The three files of one fixture investigation as Buffers, keyed by file name. */
export const investigationFiles = (id) =>
  Object.fromEntries(fileNames.map((name) => [name, readFileSync(join(fixturesRoot, "investigations", id, name))]));

/** Repository-relative path → Buffer for every fixture investigation under fixtures/v1.0.0/investigations/. */
export const fixtureRepoFiles = () =>
  Object.fromEntries(
    readdirSync(join(fixturesRoot, "investigations")).flatMap((id) =>
      Object.entries(investigationFiles(id)).map(([name, bytes]) => [`fixtures/v1.0.0/investigations/${id}/${name}`, bytes]),
    ),
  );

/** The parsed snapshot state of a fixture investigation (for choosing entities to select). */
export const snapshotState = (id) => JSON.parse(readFileSync(join(fixturesRoot, "investigations", id, "snapshot.json"), "utf8")).state;

/** A copy of `bytes` with exactly one byte changed (inside a JSON string, so the line stays well-formed JSON). */
export const flipOneByte = (bytes) => {
  const at = bytes.indexOf('"title":"') + '"title":"'.length;
  const copy = Buffer.from(bytes);
  copy[at] = copy[at] === 0x58 ? 0x59 : 0x58; // 'X' or 'Y'
  return copy;
};
