// Turns the demo recording (web/test-results/demo.webm, written by `npm run capture`) into the
// two files the README links: docs/demo/truckroll-demo.mp4 (H.264, plays anywhere) and
// docs/demo/truckroll-demo.gif (inline in the README). Needs ffmpeg on the PATH.
//
// Fails if either file is over 6 MB: GitHub renders a big GIF slowly, and the repo shouldn't
// carry heavy binaries. If it trips, shorten the demo test rather than raising the limit.
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const web = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const source = path.join(web, "test-results", "demo.webm");
const out = path.resolve(web, "..", "docs", "demo");
const mp4 = path.join(out, "truckroll-demo.mp4");
const gif = path.join(out, "truckroll-demo.gif");
const LIMIT = 6 * 1024 * 1024;

if (!existsSync(source)) {
  console.error(`No recording at ${source}. Run \`npm run capture\` first.`);
  process.exit(1);
}
mkdirSync(out, { recursive: true });

const ffmpeg = (...args) => execFileSync("ffmpeg", ["-y", "-loglevel", "error", ...args], { stdio: "inherit" });

// The MP4 keeps full size and real speed; yuv420p and faststart so browsers and phones play it.
ffmpeg("-i", source, "-c:v", "libx264", "-preset", "slow", "-crf", "26", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-an", mp4);

// The GIF is a quicker look: double speed, 800 wide, 8 frames a second, one palette made for the
// whole clip (the app's soft gradients band badly on GIF's default palette).
const filters = "setpts=PTS/2,fps=8,scale=800:-1:flags=lanczos";
ffmpeg(
  "-i",
  source,
  "-filter_complex",
  `[0:v]${filters},split[a][b];[a]palettegen=max_colors=96:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle`,
  gif,
);

let tooBig = false;
for (const file of [mp4, gif]) {
  const size = statSync(file).size;
  console.log(`${path.relative(path.resolve(web, ".."), file)}: ${(size / 1024 / 1024).toFixed(2)} MB`);
  if (size > LIMIT) {
    console.error("  over the 6 MB limit");
    tooBig = true;
  }
}
process.exit(tooBig ? 1 : 0);
