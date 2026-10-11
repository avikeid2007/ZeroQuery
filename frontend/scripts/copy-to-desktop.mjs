// Copies the Next.js static export (frontend/out) into the WinUI desktop client's wwwroot
// folder. Implemented in plain Node.js (fs.cpSync) instead of a nested shell invocation
// (e.g. `powershell -Command "Copy-Item ..."`) because spawning a shell-within-a-shell from
// an npm script proved unreliable across different Windows CI environments/npm script-shell
// configurations, even though it worked fine in an interactive local terminal.
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const sourceDir = resolve(__dirname, "..", "out");
const destDir = resolve(__dirname, "..", "..", "Desktop", "ZeroQuery.Desktop", "wwwroot");

if (!existsSync(sourceDir)) {
  console.error(`[copy-to-desktop] Source directory not found: ${sourceDir}`);
  console.error("[copy-to-desktop] Did 'next build' run successfully before this script?");
  process.exit(1);
}

mkdirSync(destDir, { recursive: true });

// Clear previous build output (but keep .gitkeep for the empty-repo case) so stale files
// from a prior export never linger alongside the new one.
for (const entry of readdirSync(destDir)) {
  if (entry === ".gitkeep") continue;
  rmSync(join(destDir, entry), { recursive: true, force: true });
}

cpSync(sourceDir, destDir, { recursive: true });

const indexPath = join(destDir, "index.html");
if (!existsSync(indexPath)) {
  console.error(`[copy-to-desktop] Copy completed but index.html is missing at: ${indexPath}`);
  process.exit(1);
}

console.log(`[copy-to-desktop] Copied '${sourceDir}' -> '${destDir}'`);
