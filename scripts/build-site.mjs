// Assemble the static production site in dist/site from the published WASM engine and the pinned Echelon web
// packages. Pure file assembly: no network, no generation of content. The site contains no investigation data
// (EXT-DATA-001); scripts/check-no-demo-data.mjs verifies that.
import { cpSync, existsSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const out = join(root, "dist", "site");
const wasmOut = join(root, "dist", "wasm");
const commit = process.env.CAUSA_BUILD_COMMIT ?? execFileSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).trim();
const skipPublish = process.argv.includes("--skip-publish");

const run = (cmd, args) => execFileSync(cmd, args, { cwd: root, stdio: "inherit" });

if (!skipPublish) {
  // A clean output folder: stale assemblies from earlier builds must never ship. No PDBs (they embed build paths).
  rmSync(wasmOut, { recursive: true, force: true });
  run("dotnet", ["publish", "src/wasm/Causa.Wasm.csproj", "-c", "Release", "-o", wasmOut, `-p:SourceRevisionId=${commit}`,
    "-p:DebugType=none", "-p:DebugSymbols=false", "-nologo"]);
}

rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });

const copy = (from, to, filter = () => true) =>
  cpSync(join(root, from), join(out, to), { recursive: true, filter: (src) => statSync(src).isDirectory() || filter(src) });

// Application shell.
for (const file of ["main.js", "app.css", "causa-icon.svg"]) copy(join("src/kernel", file), file);
copy("src/kernel/brands", "brands");
writeFileSync(join(out, "index.html"), readFileSync(join(root, "src/kernel/index.html"), "utf8").replaceAll("__CAUSA_BUILD_COMMIT__", commit));

// Pinned Echelon web packages (exact versions from package-lock.json; Limen 0.9.0, Forma 0.9.0, Folio 0.4.0).
const pkg = (name) => join("node_modules", "@echelon-foundry", name);
copy(join(pkg("limen"), "dist"), "vendor/limen", (src) => src.endsWith(".js"));
copy(join(pkg("design-system"), "dist", "all.css"), "vendor/forma/all.css");
copy(join(pkg("print-components"), "src", "components", "register.js"), "vendor/folio/components/register.js");
copy(join(pkg("print-components"), "src", "styles", "print.css"), "vendor/folio/print.css");

// The .NET WebAssembly runtime and engine. Precompressed variants are dropped: GitHub Pages does not negotiate them.
copy(relative(root, join(wasmOut, "wwwroot", "_framework")), "wasm/_framework", (src) => !src.endsWith(".gz") && !src.endsWith(".br") && !src.endsWith(".pdb"));

// GitHub Pages must not run Jekyll (it hides the _framework directory).
writeFileSync(join(out, ".nojekyll"), "");

// Build identity for exact-SHA deployment verification (G9).
const lock = JSON.parse(readFileSync(join(root, "package-lock.json"), "utf8"));
const versionOf = (name) => lock.packages?.[`node_modules/${name}`]?.version ?? null;
writeFileSync(
  join(out, "build-info.json"),
  JSON.stringify(
    {
      application: "causa",
      sourceCommit: commit,
      storageContract: "causa-storage-v1.0.0",
      echelon: {
        limen: versionOf("@echelon-foundry/limen"),
        forma: versionOf("@echelon-foundry/design-system"),
        folio: versionOf("@echelon-foundry/print-components"),
      },
    },
    null,
    2,
  ) + "\n",
);

const size = (dir) => readdirSync(dir, { withFileTypes: true }).reduce((n, e) => n + (e.isDirectory() ? size(join(dir, e.name)) : statSync(join(dir, e.name)).size), 0);
console.log(`site: ${out} (${(size(out) / 1048576).toFixed(1)} MiB) commit ${commit}`);
if (!existsSync(join(out, "wasm/_framework/dotnet.js"))) throw new Error("dotnet.js missing from the site");
