// Generates src/Pizon.Web.Client/wwwroot/content/welcome.rip: the PIZON BBS RIPscrip welcome card
// (a synthwave sun over a perspective grid, the logo, and a clickable button bar).
// Usage: node src/tools/make-welcome-rip.mjs
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { toMegaNum, RIP_WIDTH } from "../Pizon.Web.Client/wwwroot/js/terminal/rip.js";

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, "..", "Pizon.Web.Client", "wwwroot", "content", "welcome.rip");

/**
 * Encodes numbers as two-digit MegaNums.
 * @param {...number} values The numbers.
 * @returns {string} The encoded text.
 */
function m(...values) {
  return values.map((v) => toMegaNum(v)).join("");
}

const lines = ["!|*"];

/**
 * Adds one RIP command line.
 * @param {string} command The command without the leading "!|".
 * @returns {void}
 */
function cmd(command) {
  lines.push(`!|${command}`);
}

const horizon = 196;
const vanishX = RIP_WIDTH / 2;

// Sun: stacked yellow-to-red bands with black slits.
cmd(`S01${m(14)}`);
cmd(`c${m(14)}`);
cmd(`o${m(vanishX, 150, 70, 70)}`);
for (const [i, color] of [12, 12, 4, 4].entries()) {
  cmd(`S01${m(color)}`);
  const y = 150 + i * 12;
  cmd(`B${m(vanishX - 72, y, vanishX + 72, y + 6)}`);
  cmd(`S01${m(0)}`);
  cmd(`B${m(vanishX - 72, y + 7, vanishX + 72, y + 8 + i)}`);
}

// Cover the bottom of the sun with the ground.
cmd(`S01${m(0)}`);
cmd(`B${m(0, horizon, RIP_WIDTH - 1, 349)}`);

// Perspective grid: horizontal lines bunch up toward the horizon, verticals meet at the vanishing point.
cmd(`c${m(13)}`);
for (let i = 0; i < 9; i++) {
  const y = Math.round(horizon + (i * i * 112) / 64);
  if (y < 306) {
    cmd(`L${m(0, y, RIP_WIDTH - 1, y)}`);
  }
}
cmd(`c${m(5)}`);
for (let x = -640; x <= 1280; x += 80) {
  const bottomX = Math.max(0, Math.min(RIP_WIDTH - 1, x));
  const t = x === bottomX ? 1 : (bottomX - vanishX) / (x - vanishX);
  const y = Math.round(horizon + (305 - horizon) * t);
  cmd(`L${m(vanishX, horizon, bottomX, y)}`);
}

// Logo with a drop shadow, and a tagline.
cmd(`Y000000${m(6)}`);
cmd(`c${m(5)}`);
cmd(`@${m(190, 40)}PIZON BBS`);
cmd(`c${m(11)}`);
cmd(`@${m(186, 36)}PIZON BBS`);
cmd(`Y000000${m(2)}`);
cmd(`c${m(7)}`);
cmd(`@${m(196, 92)}RIPscrip 1.54 terminal detected`);

// Button bar.
const buttons = [
  ["1", "Bulletins"],
  ["2", "Messages"],
  ["3", "Files"],
  ["4", "Doors"],
  ["5", "Users"],
  ["P", "Page SysOp"],
  ["G", "Logoff"],
];
cmd(`S01${m(1)}`);
cmd(`B${m(0, 308, RIP_WIDTH - 1, 349)}`);
const width = 86;
const gap = (RIP_WIDTH - buttons.length * width) / (buttons.length + 1);
buttons.forEach(([key, label], i) => {
  const x0 = Math.round(gap + i * (width + gap));
  cmd(`1U${m(x0, 316, x0 + width, 340, key.charCodeAt(0))}000000<>${label}<>`);
});

writeFileSync(out, lines.join("\r\n") + "\r\n", "utf8");
console.log(`Wrote ${out} (${lines.length} commands)`);
