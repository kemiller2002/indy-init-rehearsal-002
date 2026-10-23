// MOCKED GitHub: a small in-memory git object store behind page.route("https://api.github.com/**").
// It answers exactly the REST reads and the GraphQL createCommitOnBranch write src/engine/GitHub.fs uses, with the JSON fields it reads, and computes real
// git blob ids so the engine's read-back verification behaves as it would against GitHub. Tests that use it are
// labelled "MOCKED:" — they prove UI behavior only, never live persistence.
//
// Shape: every request is handled by a pure function `answer(state, request) -> { state, response }`. The only
// mutable cell is the per-fake `current` state (one fake per test), replaced wholesale after each answer, plus an
// append-only request log the tests read.
import { createHash } from "node:crypto";

const sha1 = (data) => createHash("sha1").update(data).digest("hex");

/** Git's object id for file bytes: SHA-1 of "blob <n>\0" ‖ bytes. */
export const gitBlobSha = (bytes) => sha1(Buffer.concat([Buffer.from(`blob ${bytes.length}\0`, "ascii"), bytes]));

/** A deterministic 40-hex id for a tree (path → blob sha map). */
const treeSha = (files) => sha1("tree\0" + JSON.stringify(Object.entries(files).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))));
const commitSha = (tree, parents, message) => sha1(`commit\0${tree}\0${parents.join(",")}\0${message}`);

/** base64 wrapped at 60 columns with "\n", as the GitHub blobs API returns it. */
const wrappedBase64 = (bytes) => (bytes.toString("base64").match(/.{1,60}/g) ?? [""]).join("\n") + "\n";

/** Every directory path implied by a set of file paths ("a/b/c.txt" → "a", "a/b"). */
const directoriesOf = (paths) =>
  [...new Set(paths.flatMap((p) => p.split("/").slice(0, -1).map((_, i, parts) => parts.slice(0, i + 1).join("/"))))].sort();

/** Initial store: blobs by sha, one tree, one root commit, the branch ref. */
export const initialState = ({ files, branch }) => {
  const blobs = Object.fromEntries(Object.values(files).map((bytes) => [gitBlobSha(bytes), bytes]));
  const tree = Object.fromEntries(Object.entries(files).map(([path, bytes]) => [path, gitBlobSha(bytes)]));
  const tsha = treeSha(tree);
  const csha = commitSha(tsha, [], "initial");
  return {
    blobs,
    trees: { [tsha]: tree },
    commits: { [csha]: { tree: tsha, parents: [] } },
    refs: { [branch]: csha },
  };
};

const json = (status, body) => ({ status, body: JSON.stringify(body) });
const notFound = () => json(404, { message: "Not Found", documentation_url: "https://docs.github.com/rest" });

/** Is `ancestor` reachable from `head` by parent links? */
const reachable = (commits, ancestor, head) =>
  head === ancestor || (commits[head]?.parents ?? []).some((p) => reachable(commits, ancestor, p));

/**
 * The pure request handler. `config` is static (owner, repo, branch, permissions, overrides); `state` is the store.
 * `request` is { method, path, query, body } with `path` relative to /repos/{owner}/{repo}.
 */
export const answer = (config, state, request) => {
  const { method, path, body } = request;
  const same = (response) => ({ state, response });

  if (method === "GET" && path === "") {
    if (config.repoStatus !== undefined) return same(json(config.repoStatus, { message: config.repoStatus === 401 ? "Bad credentials" : "Refused" }));
    return same(json(200, {
      full_name: `${config.owner}/${config.repo}`,
      private: config.private ?? true,
      permissions: { admin: false, maintain: false, push: config.push ?? false, triage: true, pull: true },
    }));
  }

  let m;
  if (method === "GET" && (m = path.match(/^\/git\/ref\/heads\/(.+)$/))) {
    const sha = state.refs[decodeURIComponent(m[1])];
    return same(sha === undefined ? notFound() : json(200, { ref: `refs/heads/${m[1]}`, object: { sha, type: "commit" } }));
  }
  if (method === "GET" && (m = path.match(/^\/git\/commits\/([0-9a-f]{40})$/))) {
    const commit = state.commits[m[1]];
    return same(commit === undefined ? notFound() : json(200, { sha: m[1], tree: { sha: commit.tree }, parents: commit.parents.map((sha) => ({ sha })) }));
  }
  if (method === "GET" && (m = path.match(/^\/git\/trees\/([0-9a-f]{40})$/))) {
    const tree = state.trees[m[1]];
    if (tree === undefined) return same(notFound());
    const dirs = directoriesOf(Object.keys(tree)).map((p) => ({ path: p, mode: "040000", type: "tree", sha: sha1("dir\0" + p + m[1]) }));
    const blobs = Object.entries(tree).map(([p, sha]) => ({ path: p, mode: "100644", type: "blob", sha, size: state.blobs[sha]?.length ?? 0 }));
    return same(json(200, { sha: m[1], truncated: false, tree: [...dirs, ...blobs].sort((a, b) => (a.path < b.path ? -1 : 1)) }));
  }
  if (method === "GET" && (m = path.match(/^\/git\/blobs\/([0-9a-f]{40})$/))) {
    const bytes = state.blobs[m[1]];
    return same(bytes === undefined ? notFound() : json(200, { sha: m[1], size: bytes.length, encoding: "base64", content: wrappedBase64(bytes) }));
  }
  if (method === "GET" && (m = path.match(/^\/compare\/([0-9a-f]{40})\.\.\.([0-9a-f]{40})$/))) {
    const [, base, head] = m;
    const status = base === head ? "identical" : reachable(state.commits, base, head) ? "ahead" : reachable(state.commits, head, base) ? "behind" : "diverged";
    return same(json(200, { status }));
  }

  return same(notFound());
};

const graphqlError = (type, message) => json(200, { data: { createCommitOnBranch: null }, errors: [{ type, path: ["createCommitOnBranch"], message }] });

/**
 * The one write: GraphQL createCommitOnBranch. GitHub applies it atomically and only while the branch still names
 * expectedHeadOid; otherwise it answers 200 with a STALE_DATA error and changes nothing.
 */
export const answerGraphql = (config, state, body) => {
  const same = (response) => ({ state, response });
  const input = body?.variables?.input;
  if (input === undefined) return same(graphqlError("INVALID", "Variable $input was not provided"));
  const branch = input.branch?.branchName;
  const current = state.refs[branch];
  if (input.branch?.repositoryNameWithOwner !== `${config.owner}/${config.repo}` || current === undefined)
    return same(graphqlError("NOT_FOUND", "Could not resolve to a Ref"));
  if (!config.push) return same(graphqlError("FORBIDDEN", "Resource not accessible by personal access token"));
  if (input.expectedHeadOid !== current)
    return same(graphqlError("STALE_DATA", `Expected branch to point to "${input.expectedHeadOid}" but it did not. Pull and try again.`));
  const additions = (input.fileChanges?.additions ?? []).map((a) => [a.path, Buffer.from(a.contents, "base64")]);
  const tree = { ...state.trees[state.commits[current].tree], ...Object.fromEntries(additions.map(([p, bytes]) => [p, gitBlobSha(bytes)])) };
  const tsha = treeSha(tree);
  const message = [input.message?.headline, input.message?.body].filter(Boolean).join("\n\n");
  const csha = commitSha(tsha, [current], message);
  return {
    state: {
      blobs: { ...state.blobs, ...Object.fromEntries(additions.map(([, bytes]) => [gitBlobSha(bytes), bytes])) },
      trees: { ...state.trees, [tsha]: tree },
      commits: { ...state.commits, [csha]: { tree: tsha, parents: [current], message } },
      refs: { ...state.refs, [branch]: csha },
    },
    response: json(200, { data: { createCommitOnBranch: { commit: { oid: csha } } } }),
  };
};

const cors = {
  "access-control-allow-origin": "*",
  "access-control-allow-headers": "authorization, accept, content-type, x-github-api-version",
  "access-control-allow-methods": "GET, POST, OPTIONS",
  "access-control-expose-headers": "etag, link",
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/**
 * Install a fake on `page`. Returns { log, head(), state() } where `log` is the append-only list of
 * { method, path, authorization, body } the page sent.
 *
 * options: owner, repo, branch, files (repo path → Buffer), push, private, repoStatus, latencyMs, writeLatencyMs.
 */
export const installFakeGitHub = async (page, options) => {
  const config = { owner: "causa-test", repo: "causa-data", branch: "main", push: false, latencyMs: 0, writeLatencyMs: 0, ...options };
  const log = [];
  let current = initialState(config);
  const prefix = `/repos/${config.owner}/${config.repo}`;

  await page.route("https://api.github.com/**", async (route) => {
    const request = route.request();
    const method = request.method();
    if (method === "OPTIONS") return route.fulfill({ status: 204, headers: cors });
    const url = new URL(request.url());
    const body = request.postData() ? JSON.parse(request.postData()) : null;
    log.push({ method, path: url.pathname + url.search, authorization: request.headers()["authorization"] ?? null, body });
    const isWrite = method !== "GET";
    await sleep(isWrite ? config.writeLatencyMs : config.latencyMs);
    const inRepo = url.pathname === prefix || url.pathname.startsWith(prefix + "/");
    const result =
      method === "POST" && url.pathname === "/graphql"
        ? answerGraphql(config, current, body)
        : inRepo
          ? answer(config, current, { method, path: url.pathname.slice(prefix.length), query: url.search, body })
          : { state: current, response: notFound() };
    current = result.state;
    return route.fulfill({ status: result.response.status, headers: { ...cors, "content-type": "application/json; charset=utf-8" }, body: result.response.body });
  });

  return { log, config, state: () => current, head: () => current.refs[config.branch] };
};

/** Fail any request to api.github.com and record it (for tests that must prove no request is made). */
export const recordGitHubRequests = async (page) => {
  const seen = [];
  await page.route("https://api.github.com/**", (route) => {
    seen.push({ method: route.request().method(), url: route.request().url() });
    return route.abort("blockedbyclient");
  });
  page.on("request", (r) => {
    if (r.url().startsWith("https://api.github.com") && !seen.some((s) => s.url === r.url())) seen.push({ method: r.method(), url: r.url() });
  });
  return seen;
};
