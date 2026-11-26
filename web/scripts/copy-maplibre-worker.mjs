// MapLibre 6 draws tiles in a Web Worker loaded from its own ES module, which bundlers don't emit.
// Copy the worker and the module it imports into public/ so they're served at /maplibre/, and the
// map points at them with setWorkerUrl. Runs before `dev` and `build`; the output isn't committed.
import { copyFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const web = join(dirname(fileURLToPath(import.meta.url)), "..");
const source = join(web, "node_modules", "maplibre-gl", "dist");
const target = join(web, "public", "maplibre");

mkdirSync(target, { recursive: true });
for (const file of ["maplibre-gl-worker.mjs", "maplibre-gl-shared.mjs"]) {
  copyFileSync(join(source, file), join(target, file));
}
