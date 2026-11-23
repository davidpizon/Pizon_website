/**
 * Draws the BBS screens as Durdraw (.dur) files in src/Pizon.Web.Client/Screens/Art, where the screen
 * compiler (Pizon.Screens.Generator) turns them into C# at build time. Each screen is written below as 25
 * rows of pipe-code text with the placeholders (@NAME...@, @?NAME@, @^...@, @[K] label@, @ANIM:NAME@)
 * typed in literally; the rows are painted with the real terminal emulator, so the art matches what the
 * browser draws. The files are ordinary Durdraw v8 movies (256 colors, one frame): open them in Durdraw to
 * restyle a screen, keeping the placeholders' size and position. Animated art (several frames, no
 * placeholders, played in a <Slot> of a screen) is written the same way, with a <name>.anim.json sidecar
 * naming its frame ranges. See docs/content.md.
 * Usage: node src/tools/make-screen-art.mjs
 * @module make-screen-art
 */
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";
import { AnsiParser } from "../Pizon.Web.Client/wwwroot/js/terminal/ansi-parser.js";
import { CellBuffer } from "../Pizon.Web.Client/wwwroot/js/terminal/cell-buffer.js";
import { decodeArt } from "../Pizon.Web.Client/wwwroot/js/terminal/cp437.js";

const here = dirname(fileURLToPath(import.meta.url));
const client = join(here, "..", "Pizon.Web.Client");
const outDir = join(client, "Screens", "Art");
const COLS = 80;
const ROWS = 25;

/** Durdraw's foreground numbering for the 16 basic colors swaps some pairs (its color_256_to_ansi_16 table). */
const DUR_FG = new Map([[1, 4], [3, 6], [4, 1], [6, 3], [9, 12], [11, 14], [12, 9], [14, 11]]);

/**
 * Converts the pipe color codes used in the art (|00-|23, |#NNN, |%NNN, |!) to ANSI.
 * @param {string} text Pipe-code text.
 * @returns {string} ANSI text.
 */
function pipeToAnsi(text) {
  return text.replace(/\|(\d\d|#\d{3}|%\d{3}|!)/g, (_, code) => {
    if (code === "!") {
      return "\x1b[0m";
    }
    if (code[0] === "#") {
      return `\x1b[38;5;${Number(code.slice(1))}m`;
    }
    if (code[0] === "%") {
      return `\x1b[48;5;${Number(code.slice(1))}m`;
    }
    const n = Number(code);
    return n <= 15 ? `\x1b[38;5;${n}m` : `\x1b[48;5;${n - 16}m`;
  });
}

/**
 * Measures the visible width of pipe-code text.
 * @param {string} text Pipe-code text.
 * @returns {number} The number of cells it takes.
 */
function visible(text) {
  return [...text.replace(/\|(\d\d|#\d{3}|%\d{3}|!)/g, "")].length;
}

/**
 * Pads pipe-code text with spaces to a visible width.
 * @param {string} text Pipe-code text.
 * @param {number} width The width.
 * @returns {string} The padded text.
 */
function pad(text, width) {
  return text + " ".repeat(Math.max(0, width - visible(text)));
}

/**
 * Centers pipe-code text in a width.
 * @param {string} text Pipe-code text.
 * @param {number} [width=80] The width.
 * @returns {string} The centered text.
 */
function center(text, width = COLS) {
  return " ".repeat(Math.floor((width - visible(text)) / 2)) + text;
}

/**
 * Writes a placeholder exactly `width` cells wide.
 * @param {string} name The placeholder body before the fill (for example "HANDLE", "?MESSAGE", "^").
 * @param {number} width The whole token's width, both @ signs included.
 * @param {string} [fill="."] The fill character: "." left, ">" right, "=" centered.
 * @returns {string} The token.
 */
function slot(name, width, fill = ".") {
  const room = width - name.length - 2;
  if (room < 0) {
    throw new Error(`@${name}@ needs at least ${name.length + 2} cells`);
  }
  return `@${name}${fill.repeat(room)}@`;
}

/**
 * Writes a block placeholder: the named token on the first row and @^@ continuations below.
 * @param {string} name The placeholder name.
 * @param {number} width The width.
 * @param {number} height The number of rows.
 * @param {string} [fill="."] The fill character.
 * @returns {string[]} One token per row.
 */
function block(name, width, height, fill = ".") {
  return [slot(name, width, fill), ...Array.from({ length: height - 1 }, () => slot("^", width, fill))];
}

/**
 * Writes a menu hotspot in the house style, "[K] label": clicking the label sends K. Only the two @ cells
 * around it turn blank, so the label stays where it is drawn.
 * @param {string} key The key (a single key, or a name such as "Enter").
 * @param {string} label The label (pipe codes allowed).
 * @returns {string} The token.
 */
function item(key, label) {
  return `@|14[${key}]|07 ${label}|07@`;
}

/**
 * Builds the shared section header (rows 1-3: a bar, the title and "PIZON BBS", a bar).
 * @param {string|null} title The title in capitals, or null for a @TITLE@ field.
 * @returns {string[]} Three rows.
 */
function header(title) {
  const bar = `|#033${"═".repeat(78)}`;
  const name = title === null ? slot("TITLE", 60) : pad(title, 60);
  return [bar, ` |11${name}|#244${" ".repeat(8)}PIZON BBS`, bar];
}

/**
 * Lays rows out on a 25-row screen: `rows[i]` is drawn on row i + 1 (missing rows are blank).
 * @param {Record<number, string|string[]>} at Rows keyed by 1-based row; an array fills consecutive rows.
 * @returns {string[]} 25 rows.
 */
function screen(at) {
  const rows = Array(ROWS).fill("");
  for (const [key, value] of Object.entries(at)) {
    const lines = Array.isArray(value) ? value : [value];
    lines.forEach((line, i) => {
      rows[Number(key) - 1 + i] = line;
    });
  }
  return rows;
}

/**
 * Overlays two lists of rows column-wise (for side-by-side blocks).
 * @param {string[]} left The left rows.
 * @param {number} width The left column's width.
 * @param {string[]} right The right rows.
 * @returns {string[]} The joined rows.
 */
function beside(left, width, right) {
  return Array.from({ length: Math.max(left.length, right.length) }, (_, i) => pad(left[i] ?? "", width) + (right[i] ?? ""));
}

/**
 * The screens, by file name: 25 rows each, plus optional ANSI art painted underneath first.
 * @returns {Record<string, {rows: string[], under?: string}>} The screens.
 */
function screens() {
  const box = (row) => `|#039   ║ ${row}|#039║`;
  const menuRow = (left, right) => box(pad(left, 32) + pad(right, 31));
  const banner = decodeArt(new Uint8Array(readFileSync(join(client, "wwwroot", "content", "pizon.ans"))));
  return {
    connect: {
      rows: screen({
        3: "|08  PIZON TERMINAL v1.0 · 80x25 · ANSI/256 · RIPscrip 1.54",
        4: "|08  (c) 2026 PIZON.COM · All rights reserved, most wrongs regretted.",
        6: "|07  Modem ready. Phonebook entry: |15PIZON BBS|07 · 555-0199",
        8: `    ${item("C", "|15CONNECT")}  ${item("S", "Sound:")}|11${slot("SOUND", 7)}`,
        10: `|08  Press |14C|08 or |14Enter|08 to dial. Tip: try it with sound on. |07${slot("CURSOR", 8)}`,
      }),
    },
    welcome: {
      under: banner,
      rows: screen({
        14: `|#045${center("B · B · S")}`,
        15: `|08${slot("TAGLINE", 80, "=")}`,
        17: `|08SysOp: |15${slot("SYSOP", 18)}|08 · Node |11${slot("NODE", 7, ">")}|08 of |11${slot("NODES", 7)}|08 · |07${slot("DATE", 17)}`,
        18: `|08Mood: |13${slot("MOOD", 30)}|08 · Location: |13${slot("LOCATION", 26)}`,
        19: `|08Caller: |14${slot("CALLER", 18)}|08 · Level |14${slot("LEVEL", 7)}`,
        21: `|09${slot("STATUS", 80)}`,
        22: `|11${slot("PROMPT", 80)}`,
        23: `  ${slot("OPTIONS", 76)}`,
        24: `|07${slot("CURSOR", 8)}`,
      }),
    },
    rip: {
      rows: screen({
        1: "|08Loading the RIPscrip welcome card...",
      }),
    },
    "main-menu": {
      rows: screen({
        1: header("MAIN MENU"),
        5: `|#039   ╔${"═".repeat(64)}╗`,
        6: [
          menuRow(item("1", "System Bulletins"), item("P", "Page the SysOp")),
          menuRow(item("2", "Message Boards"), ` ${slot("ITEM_ACCOUNT", 30)}`),
          menuRow(item("3", "File Areas"), item("S", "Terminal Settings")),
          menuRow(item("4", "Door Games"), item("R", "RIP Welcome Card")),
          menuRow(item("5", "User List & Hall of Fame"), ` ${slot("ITEM_LOGOUT", 30)}`),
          menuRow("", item("G", "|13Goodbye - Logoff")),
          menuRow(` ${slot("ITEM_SYSOP", 31)}`, ""),
        ],
        13: `|#039   ╚${"═".repeat(64)}╝`,
        15: `|08   Connected as |14${slot("HANDLE", 16)}|08 Level |14${slot("LEVEL", 7)}|08 · Press a key or click.`,
        17: `|11   Your choice: |07${slot("CURSOR", 8)}`,
        25: `|08${slot("ONLINE", 80)}`,
      }),
    },
    bulletins: {
      rows: screen({
        1: header("SYSTEM BULLETINS"),
        5: "|08   Notices from the SysOp. Read them; there may be a quiz (there won't).",
        7: block("LIST", 75, 12).map((t) => `    |07${t}`),
        20: `   ${item("T", "Top Players (door game scores)")}`,
        22: `   ${item("Q", "Back to the main menu")}`,
        24: `|11   Read which bulletin? |07${slot("CURSOR", 8)}`,
      }),
    },
    bulletin: {
      rows: screen({
        1: header(null),
        5: block("BODY", 76, 17).map((t) => `  |07${t}`),
        23: `  ${slot("MORE", 60)}`,
        24: `  |07${slot("CURSOR", 8)}`,
      }),
    },
    "bulletin-cam": {
      rows: screen({
        1: header(null),
        4: [pad("", 53) + slot("ANIM:CAM", 24), ...beside(block("BODY", 50, 17).map((t) => `  |07${t}`), 53, block("^", 24, 11))],
        23: `  ${slot("MORE", 60)}`,
        24: `  |07${slot("CURSOR", 8)}`,
      }),
    },
    "top-players": {
      rows: screen({
        1: header("TOP PLAYERS"),
        5: block("BODY", 76, 17).map((t) => `  |07${t}`),
        23: `  ${slot("MORE", 60)}`,
        24: `  |07${slot("CURSOR", 8)}`,
      }),
    },
    board: {
      rows: screen({
        1: header("MESSAGE BOARDS - THE GRAFFITI WALL"),
        5: block("BODY", 78, 16).map((t) => `|07${t}`),
        21: `  ${slot("MORE", 60)}`,
        22: `  ${item("N", "New message")}  ${item("R", "Reply")}  ${item("Q", "Main menu")}`,
        23: `|11  Command: |07${slot("CURSOR", 8)}`,
      }),
    },
    "board-compose": {
      rows: screen({
        1: header(null),
        5: `|11  ${slot("PROMPT", 22, ">")}|07${slot("?NUMBER", 11)}`,
        7: `  ${slot("INFO", 76)}`,
        9: block("?BODY", 76, 7).map((t) => `|07${t}`),
        17: `  ${slot("STATUS", 76)}`,
        19: `|07${slot("CURSOR", 8)}`,
      }),
    },
    files: {
      rows: screen({
        1: header("FILE AREAS"),
        5: "|08   All files virus-scanned with a magnifying glass. Downloads open in a new tab.",
        7: block("LIST", 75, 12).map((t) => `    |07${t}`),
        20: `   ${item("Q", "Back to the main menu")}`,
        22: `|11   Select an area: |07${slot("CURSOR", 8)}`,
      }),
    },
    "file-area": {
      rows: screen({
        1: header(null),
        5: "|08   #  FILENAME.EXT       SIZE  DESCRIPTION",
        6: `|08   ${"─".repeat(72)}`,
        7: block("LIST", 77, 9).map((t) => `  |07${t}`),
        17: `   ${item("Q", "Back to the areas")}`,
        19: `|11   Download which file (1-9)? |07${slot("CURSOR", 8)}`,
      }),
    },
    doors: {
      rows: screen({
        1: header("DOOR GAMES"),
        5: "|08   External programs. The BBS steps aside while you play.",
        7: block("LIST", 75, 12).map((t) => `    |07${t}`),
        20: `   ${item("Q", "Back to the main menu")}`,
        22: `|11   Open which door? |07${slot("CURSOR", 8)}`,
      }),
    },
    users: {
      rows: screen({
        1: header("USER LIST & HALL OF FAME"),
        5: block("BODY", 80, 17).map((t) => `|07${t}`),
        23: `  ${slot("MORE", 60)}`,
        24: `  |07${slot("CURSOR", 8)}`,
      }),
    },
    page: {
      rows: screen({
        1: header("PAGE THE SYSOP"),
        5: "|07  The SysOp may be asleep, at work, or elbow-deep in a modem. Keep it short.",
        6: "|08  ESC cancels.",
        8: `|11  Message: |07${slot("?MESSAGE", 64)}`,
        10: [`  ${slot("STATUS", 76)}`, `  ${slot("^", 76)}`],
        13: `|07${slot("CURSOR", 8)}`,
      }),
    },
    handle: {
      rows: screen({
        1: header(null),
        5: "|07  Your handle is how the board knows you: |153-16|07 letters, digits, |15_|07 or |15-|07.",
        6: "|08  Edit the suggestion or type your own. ESC to cancel.",
        8: `|11  Handle: |15${slot("?HANDLE", 18)}`,
        10: [`  ${slot("STATUS", 76)}`, `  ${slot("^", 76)}`],
        13: `|07${slot("CURSOR", 8)}`,
      }),
    },
    settings: {
      rows: screen({
        1: header("TERMINAL SETTINGS"),
        5: `   ${item("B", "Baud rate .........")} |11${slot("BAUD", 10)}`,
        6: `   ${item("A", "Modem sounds ......")} ${slot("SOUND", 8)}`,
        7: `   ${item("R", "RIP welcome card ..")} ${slot("RIP", 8)}`,
        9: "|08   Settings stay in this browser only. INSTANT also turns off most animation.",
        11: `   ${item("Q", "Back to the main menu")}`,
        13: `|11   Change which? |07${slot("CURSOR", 8)}`,
      }),
    },
    goodbye: {
      rows: screen({
        3: `|#201${center("THANKS FOR CALLING PIZON BBS")}`,
        5: `|07${slot("FAREWELL", 80, "=")}`,
        6: `|08${center("Your call has been logged on paper and filed in a shoebox.")}`,
        8: `|07${center("Call again soon. The line is always open (unless someone is on it).")}`,
        10: `|07${slot("CURSOR", 8)}`,
      }),
    },
    "not-found": {
      rows: screen({
        2: "|09ERROR 404: FILE NOT FOUND ON THIS DRIVE.",
        3: "|08Try drive B:, or go back to the main menu.",
        5: `|07${slot("CURSOR", 8)}`,
      }),
    },
    sysop: {
      rows: screen({
        1: header("SYSOP CONSOLE"),
        5: [`   ${item("1", "Moderation queue")}`, `   ${item("2", "Pages inbox")}`, `   ${item("3", "Delete a message")}`],
        9: `   ${item("Q", "Back to the main menu")}`,
        11: `|11   SysOp command: |07${slot("CURSOR", 8)}`,
      }),
    },
    "sysop-delete": {
      rows: screen({
        1: header("DELETE A MESSAGE"),
        5: "|07   Deletes the message and all its replies. There is no undo. ESC goes back.",
        7: `|11   Message #: |07${slot("?NUMBER", 11)}`,
        9: [`   ${slot("STATUS", 76)}`, `   ${slot("^", 76)}`],
        12: `|07   ${slot("CURSOR", 8)}`,
      }),
    },
    "sysop-queue": {
      rows: screen({
        1: header("MODERATION QUEUE"),
        5: `  ${slot("HEAD", 78)}`,
        7: block("BODY", 74, 13).map((t) => `    |07${t}`),
        21: `  ${item("A", "Approve")}  ${item("X", "Reject")}  ${item("B", "|09Ban author")}  ${item("N", "Skip")}  ${item("Q", "Done")}`,
        23: `  ${slot("STATUS", 76)}`,
        24: `  |07${slot("CURSOR", 8)}`,
      }),
    },
    "sysop-pages": {
      rows: screen({
        1: header(null),
        5: block("BODY", 80, 17).map((t) => `|07${t}`),
        23: `  ${slot("MORE", 60)}`,
        24: `  |07${slot("CURSOR", 8)}`,
      }),
    },
  };
}

/**
 * The animated art, by file name: each frame is rows of pipe-code text, plus the named frame ranges
 * written to the .anim.json sidecar.
 * TODO-SYSOP: the SysOp cam is a stand-in drawing; draw the real cam in Durdraw and save it over sysop-cam.dur
 * (keep it 24 x 12, and keep or rename the ranges in sysop-cam.anim.json).
 * @returns {Record<string, {cols: number, rows: number, frames: string[][], ranges: Record<string, number[]>}>} The animations.
 */
function animations() {
  const cols = 24;
  const rows = 12;
  const frame = (label, blink, eyes, hands, noise) => {
    const inner = [
      `|09${blink ? "\u25cf" : " "} ${label}|08${" ".repeat(9 - label.length)}|11SYSOP CAM `,
      "",
      "|07       .---.        ",
      `|07      ( ${eyes} )       `,
      "|07       \\ - /        ",
      "|07     ___|_|___      ",
      `|07    |  [${hands}]  |     `,
      "|07    '---------'     ",
      "",
      "",
    ].map((line, i) => (i + 1 === noise ? "|08" + "\u2591\u2592\u2591\u2591\u2592\u2591\u2591\u2591\u2592\u2591\u2591\u2592\u2591\u2591\u2591\u2592\u2591\u2591\u2592\u2591\u2591\u2591" : line));
    return [
      `|08\u250c${"\u2500".repeat(cols - 2)}\u2510`,
      ...inner.map((line) => `|08\u2502${pad(line, cols - 2)}|08\u2502`),
      `|08\u2514${"\u2500".repeat(cols - 2)}\u2518`,
    ];
  };
  const idle = Array.from({ length: 8 }, (_, i) => frame("REC", i < 4, i === 6 ? "- -" : "o o", "===", 2 + i));
  const busy = Array.from({ length: 8 }, (_, i) => frame("LIVE", i % 2 === 0, i % 4 < 2 ? "o o" : "O O", i % 2 === 0 ? "=#=" : "#=#", 2 + i));
  return { "sysop-cam": { cols, rows, frames: [...idle, ...busy], ranges: { idle: [0, 7], busy: [8, 15] } } };
}

/**
 * Paints a screen's rows (over optional ANSI art) with the terminal emulator.
 * @param {string[]} rows 25 rows of pipe-code text (or the art's own height).
 * @param {string} [under] ANSI drawn first (the welcome banner).
 * @param {number} [cols=80] The art's width.
 * @param {number} [height=25] The art's height.
 * @returns {CellBuffer} The painted cells.
 */
function paint(rows, under, cols = COLS, height = ROWS) {
  const buffer = new CellBuffer(cols, height);
  const parser = new AnsiParser((t) => buffer.apply(t));
  if (under) {
    parser.feed(under);
  }
  rows.forEach((row, i) => {
    if (visible(row) > cols) {
      throw new Error(`row ${i + 1} is ${visible(row)} cells wide: ${row}`);
    }
    if (row) {
      parser.feed(`\x1b[0m\x1b[${i + 1};1H` + pipeToAnsi(row) + "\x1b[0m");
    }
  });
  return buffer;
}

/**
 * Builds a Durdraw v8 movie (256 colors) from painted frames, each shown for one frame at 8 per second.
 * @param {string} name The movie's name.
 * @param {CellBuffer[]} buffers The frames' cells (all the same size).
 * @returns {object} The JSON object Durdraw saves.
 */
function durMovie(name, buffers) {
  const frames = buffers.map((buffer, i) => {
    const contents = [];
    const colorMap = [];
    for (let r = 0; r < buffer.rows; r++) {
      const cells = buffer.row(r);
      contents.push(cells.map((c) => c.ch).join(""));
      colorMap.push(cells.map((c) => [DUR_FG.get(c.fg) ?? c.fg, c.bg]));
    }
    return { frameNumber: i + 1, delay: 0, contents, colorMap };
  });
  return {
    DurMovie: {
      formatVersion: 8,
      colorFormat: "256",
      preferredFont: "default",
      encoding: "utf-8",
      name,
      artist: "PIZON BBS",
      framerate: 8.0,
      columns: buffers[0].cols,
      lines: buffers[0].rows,
      extra: null,
      frames,
    },
  };
}

/**
 * Writes an animation's .anim.json sidecar, one range per line.
 * @param {Record<string, number[]>} ranges The named ranges: first and last frame, counting from 0.
 * @returns {string} The file text.
 */
function sidecar(ranges) {
  const lines = Object.entries(ranges).map(([name, [first, last]]) => `    "${name}": [${first}, ${last}]`);
  return `{\n  "ranges": {\n${lines.join(",\n")}\n  }\n}\n`;
}

mkdirSync(outDir, { recursive: true });
for (const [name, def] of Object.entries(screens())) {
  const json = JSON.stringify(durMovie(name, [paint(def.rows, def.under)]));
  writeFileSync(join(outDir, `${name}.dur`), gzipSync(Buffer.from(json, "utf8"), { mtime: 0 }));
  console.log(`wrote ${name}.dur`);
}
for (const [name, def] of Object.entries(animations())) {
  const json = JSON.stringify(durMovie(name, def.frames.map((rows) => paint(rows, undefined, def.cols, def.rows))));
  writeFileSync(join(outDir, `${name}.dur`), gzipSync(Buffer.from(json, "utf8"), { mtime: 0 }));
  writeFileSync(join(outDir, `${name}.anim.json`), sidecar(def.ranges));
  console.log(`wrote ${name}.dur (${def.frames.length} frames) and ${name}.anim.json`);
}
