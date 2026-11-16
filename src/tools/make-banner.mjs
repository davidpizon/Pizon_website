// Generates src/Pizon.Web.Client/wwwroot/content/pizon.ans: the graffiti-style PIZON banner
// (slanted block letters, a fire gradient fill, magenta overspray, a drop shadow and paint drips).
// The output is plain ANSI (UTF-8, 256-color SGR), so it can be refined in Moebius or PabloDraw.
// Usage: node src/tools/make-banner.mjs
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, "..", "Pizon.Web.Client", "wwwroot", "content", "pizon.ans");

const GLYPHS = {
  P: ["######.", "##...##", "##...##", "######.", "##.....", "##.....", "##.....", "##....."],
  I: ["######", "..##..", "..##..", "..##..", "..##..", "..##..", "..##..", "######"],
  Z: ["#######", ".....##", "....##.", "...##..", "..##...", ".##....", "##.....", "#######"],
  O: [".#####.", "##...##", "##...##", "##...##", "##...##", "##...##", "##...##", ".#####."],
  N: ["##...##", "###..##", "####.##", "##.####", "##..###", "##...##", "##...##", "##...##"],
};

const WORD = "PIZON";
const ROWS = 8;
const WIDTH = 80;
const HEIGHT = ROWS + 5;
const FILL = [196, 202, 208, 214, 220, 226, 227, 229];
const SPRAY = 201;
const SHADOW = 90;
const SPARKLE = 231;

/** @type {{ch: string, fg: number}[][]} */
const grid = Array.from({ length: HEIGHT }, () => Array.from({ length: WIDTH }, () => ({ ch: " ", fg: 7 })));
/** @type {boolean[][]} */
const solid = Array.from({ length: HEIGHT }, () => Array(WIDTH).fill(false));

// Lay out the letters, doubled horizontally and slanted (each row shifted right toward the top).
let x = 2;
for (const letter of WORD) {
  const glyph = GLYPHS[letter];
  glyph.forEach((row, r) => {
    const slant = Math.floor((ROWS - 1 - r) / 2);
    [...row].forEach((cell, c) => {
      if (cell === "#") {
        for (const dx of [0, 1]) {
          const cx = x + slant + c * 2 + dx;
          solid[r + 1][cx] = true;
        }
      }
    });
  });
  x += glyph[0].length * 2 + 1;
}

/**
 * Checks whether a cell is part of a letter.
 * @param {number} r Row.
 * @param {number} c Column.
 * @returns {boolean} True when filled.
 */
function filled(r, c) {
  return r >= 0 && r < HEIGHT && c >= 0 && c < WIDTH && solid[r][c];
}

// Drop shadow (down-right), then overspray around the letters.
for (let r = 0; r < HEIGHT; r++) {
  for (let c = 0; c < WIDTH; c++) {
    if (!filled(r, c) && filled(r - 1, c - 1)) {
      grid[r][c] = { ch: "▒", fg: SHADOW };
    }
  }
}
for (let r = 0; r < HEIGHT; r++) {
  for (let c = 0; c < WIDTH; c++) {
    const near = filled(r, c - 1) || filled(r, c + 1) || filled(r - 1, c) || filled(r + 1, c);
    if (!filled(r, c) && grid[r][c].ch === " " && near && (r * 131 + c * 71) % 11 < 3) {
      grid[r][c] = { ch: "░", fg: SPRAY };
    }
  }
}

// The fill: a fire gradient by row, with a half-block highlight on top edges.
for (let r = 0; r < HEIGHT; r++) {
  for (let c = 0; c < WIDTH; c++) {
    if (filled(r, c)) {
      const color = FILL[Math.min(FILL.length - 1, Math.max(0, r - 1))];
      grid[r][c] = { ch: filled(r - 1, c) ? "█" : "▄", fg: color };
    }
  }
}

// Paint drips under a few letter bottoms.
for (const [col, length] of [[7, 2], [24, 3], [31, 1], [49, 2], [66, 3], [73, 1]]) {
  let top = HEIGHT - 1;
  while (top > 0 && !filled(top, col)) {
    top--;
  }
  if (top === 0) {
    continue;
  }
  for (let i = 1; i <= length; i++) {
    grid[top + i][col] = { ch: i === length ? "▀" : "█", fg: FILL[FILL.length - 1] };
  }
}

// A couple of sparkles.
for (const [r, c] of [[1, 20], [2, 58], [4, 76]]) {
  if (!filled(r, c)) {
    grid[r][c] = { ch: "*", fg: SPARKLE };
  }
}

// Emit ANSI, changing color only when needed; trim trailing blanks.
const lines = grid.map((row) => {
  let end = row.length;
  while (end > 0 && row[end - 1].ch === " ") {
    end--;
  }
  let line = "";
  let current = -1;
  for (let c = 0; c < end; c++) {
    const cell = row[c];
    if (cell.ch !== " " && cell.fg !== current) {
      line += `\x1b[38;5;${cell.fg}m`;
      current = cell.fg;
    }
    line += cell.ch;
  }
  return line + "\x1b[0m";
});

// Drop fully blank rows at the end.
while (lines.length > 0 && lines[lines.length - 1] === "\x1b[0m") {
  lines.pop();
}

writeFileSync(out, lines.join("\r\n") + "\r\n", "utf8");
console.log(`Wrote ${out} (${lines.length} rows)`);
console.log(lines.map((l) => l.replace(/\x1b\[[0-9;]*m/g, "")).join("\n"));
