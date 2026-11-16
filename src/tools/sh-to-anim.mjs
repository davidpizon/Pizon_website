// Converts a shell-script ANSI animation (for example the ".sh" export of a GIF-to-ANSI converter) into
// the PIZON BBS animation JSON: { "fps": N, "frames": ["<ansi>", ...] }.
//
// Supported script lines (best effort; check one real export and adjust if needed):
//   printf '...' / printf "..."      - text added to the current frame (\e \033 \x1b \n \r \t \\ escapes)
//   echo -e "..." / echo -ne "..."   - same (echo -e also ends the text with a newline)
//   clear / tput clear               - starts a new frame
//   sleep N                          - ends the current frame; the first sleep sets the fps
//
// Usage: node src/tools/sh-to-anim.mjs input.sh output.json [--fps 10]
import { readFileSync, writeFileSync } from "node:fs";

/**
 * Unescapes the escapes a shell printf/echo understands.
 * @param {string} text Escaped text.
 * @returns {string} The real characters.
 */
export function unescapeShell(text) {
  return text.replace(/\\(e|033|x1[bB]|n|r|t|\\|'|")/g, (_, code) => {
    switch (code) {
      case "e":
      case "033":
      case "x1b":
      case "x1B":
        return "\x1b";
      case "n":
        return "\n";
      case "r":
        return "\r";
      case "t":
        return "\t";
      default:
        return code;
    }
  });
}

/**
 * Pulls the quoted argument out of a printf/echo line.
 * @param {string} line The script line.
 * @returns {string | null} The argument, or null when the line has none.
 */
export function quotedArgument(line) {
  const match = /^(?:printf|echo(?:\s+-[neE]+)?)\s+(['"])([\s\S]*)\1\s*;?\s*$/.exec(line.trim());
  return match ? match[2] : null;
}

/**
 * Converts a script to animation JSON.
 * @param {string} script The script text.
 * @param {number | null} fpsOverride A fixed fps, or null to use the first sleep.
 * @returns {{fps: number, frames: string[]}} The animation.
 */
export function convert(script, fpsOverride) {
  const frames = [];
  let current = "";
  let fps = fpsOverride;
  const flush = () => {
    if (current.length > 0) {
      frames.push(current);
      current = "";
    }
  };
  for (const raw of script.split(/\r?\n/)) {
    const line = raw.trim();
    if (line.length === 0 || line.startsWith("#")) {
      continue;
    }
    const sleep = /^sleep\s+([0-9.]+)/.exec(line);
    if (sleep) {
      const seconds = Number.parseFloat(sleep[1]);
      if (fps === null && seconds > 0) {
        fps = Math.max(1, Math.min(30, Math.round(1 / seconds)));
      }
      flush();
      continue;
    }
    if (/^(clear|tput\s+clear)\b/.test(line)) {
      flush();
      continue;
    }
    const arg = quotedArgument(line);
    if (arg !== null) {
      current += unescapeShell(arg) + (/^echo\s+-e\b/.test(line) || /^echo\s+["']/.test(line) ? "\n" : "");
    }
  }
  flush();
  return { fps: fps ?? 10, frames: frames.map((f) => f.replace(/\r?\n/g, "\r\n")) };
}

const isMain = process.argv[1] && import.meta.url.endsWith(process.argv[1].replace(/\\/g, "/").split("/").pop());
if (isMain) {
  const [input, output, flag, value] = process.argv.slice(2);
  if (!input || !output) {
    console.error("Usage: node src/tools/sh-to-anim.mjs input.sh output.json [--fps 10]");
    process.exit(1);
  }
  const fps = flag === "--fps" ? Number.parseInt(value, 10) : null;
  const animation = convert(readFileSync(input, "utf8"), Number.isFinite(fps) ? fps : null);
  writeFileSync(output, JSON.stringify(animation));
  console.log(`Wrote ${output}: ${animation.frames.length} frames at ${animation.fps} fps`);
}
