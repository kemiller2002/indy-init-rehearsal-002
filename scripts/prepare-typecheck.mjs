// Mirror the site's ./vendor layout for the type checker (tsconfig rootDirs), pointing at the pinned packages'
// own type declarations. Nothing here is shipped.
import { mkdirSync, rmSync, symlinkSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const vendor = join(root, ".cache", "typecheck", "vendor");
rmSync(vendor, { recursive: true, force: true });
mkdirSync(join(vendor, "folio"), { recursive: true });
symlinkSync(join(root, "node_modules/@echelon-foundry/limen/dist"), join(vendor, "limen"), "dir");
symlinkSync(join(root, "node_modules/@echelon-foundry/print-components/src/components"), join(vendor, "folio", "components"), "dir");
