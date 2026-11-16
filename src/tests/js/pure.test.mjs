/**
 * Tests for the pure terminal modules: parser, cell buffer, palette, pacing, animations, CP437, transitions,
 * RIPscrip, the handshake schedule and the .sh animation converter.
 * @module pure.test
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { JS, TOOLS, recordingContext } from "./support.mjs";

const { AnsiParser } = await import(new URL("ansi-parser.js", JS));
const { CellBuffer, blankCell, sameCell, isAllowedLink, clamp, COLS, ROWS } = await import(new URL("cell-buffer.js", JS));
const { PALETTE, hex, nearestIndex, buildPalette } = await import(new URL("palette.js", JS));
const { Pacer, charsPerSecond, BAUD_RATES } = await import(new URL("pacing.js", JS));
const anim = await import(new URL("ansimation.js", JS));
const { decodeCp437, stripSauce, decodeArt } = await import(new URL("cp437.js", JS));
const { plan, mulberry32, EFFECTS, DURATIONS, SPRAY_STAGES } = await import(new URL("transitions.js", JS));
const rip = await import(new URL("rip.js", JS));
const { handshakeSchedule, PHONE_NUMBER, DTMF } = await import(new URL("audio.js", JS));
const sh = await import(new URL("sh-to-anim.mjs", TOOLS));

/**
 * Parses text into tokens.
 * @param {string} text The input.
 * @returns {object[]} The tokens.
 */
function tokens(text) {
  const out = [];
  new AnsiParser((t) => out.push(t)).feed(text);
  return out;
}

/**
 * Feeds text into a fresh buffer.
 * @param {string} text The input.
 * @param {number} cols Columns.
 * @param {number} rows Rows.
 * @returns {CellBuffer} The buffer.
 */
function buffer(text, cols = 10, rows = 3) {
  const b = new CellBuffer(cols, rows);
  new AnsiParser((t) => b.apply(t)).feed(text);
  return b;
}

test("parser emits characters, controls, CSI, ESC 7/8 and OSC tokens", () => {
  assert.deepEqual(tokens("a\r\n\x7f\x01\x1b[?25l\x1b[;5H\x1b[m\x1b[ m\x1b7\x1b8\x1bZ\x1b]8;;key:Q\x07\x1b]0;x\x1bQy\x1b\\"), [
    { t: "char", ch: "a" },
    { t: "ctl", ch: "\r" },
    { t: "ctl", ch: "\n" },
    { t: "csi", params: [25], final: "l", priv: "?" },
    { t: "csi", params: [0, 5], final: "H", priv: "" },
    { t: "csi", params: [], final: "m", priv: "" },
    { t: "csi", params: [0], final: "m", priv: "" },
    { t: "csi", params: [], final: "s", priv: "" },
    { t: "csi", params: [], final: "u", priv: "" },
    { t: "osc", data: "8;;key:Q" },
    { t: "osc", data: "0;x\x1bQy" },
  ]);
});

test("buffer wraps, scrolls and handles control characters", () => {
  const b = buffer("abcdefghij\x07", 4, 2);
  assert.equal(b.rowText(0), "efgh");
  assert.equal(b.rowText(1), "ij");
  const t = buffer("ab\tX\bY\rZ");
  assert.equal(t.rowText(0), "Zb      Y");
  assert.equal(COLS, 80);
  assert.equal(ROWS, 25);
});

test("buffer moves the cursor and erases", () => {
  const b = buffer("\x1b[2;3Hx\x1b[A\x1b[2Cy\x1b[5B\x1b[9Dz\x1b[f\x1b[sQ\x1b[u\x1b[?25h");
  assert.deepEqual(b.cursor, { row: 0, col: 0 });
  assert.equal(b.rowText(0), "Q    y");
  assert.equal(b.rowText(1), "  x");
  assert.equal(b.rowText(2), "z");

  const lines = () => buffer("aaaaaaaaaa\r\nbbbbbbbbbb\r\ncccccccccc\x1b[2;5H");
  let e = lines();
  e.apply({ t: "csi", params: [], final: "J", priv: "" });
  assert.deepEqual([e.rowText(0), e.rowText(1), e.rowText(2)], ["aaaaaaaaaa", "bbbb", ""]);
  e = lines();
  e.apply({ t: "csi", params: [1], final: "J", priv: "" });
  assert.deepEqual([e.rowText(0), e.rowText(1)], ["", "     bbbbb"]);
  e = lines();
  e.apply({ t: "csi", params: [2], final: "J", priv: "" });
  assert.deepEqual(e.cursor, { row: 0, col: 0 });
  assert.equal(e.rowText(2), "");
  e = lines();
  e.apply({ t: "csi", params: [], final: "K", priv: "" });
  assert.equal(e.rowText(1), "bbbb");
  e = lines();
  e.apply({ t: "csi", params: [1], final: "K", priv: "" });
  assert.equal(e.rowText(1), "     bbbbb");
  e = lines();
  e.apply({ t: "csi", params: [2], final: "K", priv: "" });
  assert.equal(e.rowText(1), "");
});

test("SGR sets 16, 256 and truecolor attributes", () => {
  const cell = (seq) => buffer(seq + "x").cellAt(0, 0);
  assert.deepEqual(cell("\x1b[1;31m"), { ch: "x", fg: 9, bg: 0, link: null });
  assert.deepEqual(cell("\x1b[1;22;5;44m"), { ch: "x", fg: 7, bg: 12, link: null });
  assert.deepEqual(cell("\x1b[5;25;7;32;41m"), { ch: "x", fg: 1, bg: 2, link: null });
  assert.deepEqual(cell("\x1b[7;27;39;49m"), { ch: "x", fg: 7, bg: 0, link: null });
  assert.deepEqual(cell("\x1b[93;104m"), { ch: "x", fg: 11, bg: 12, link: null });
  assert.deepEqual(cell("\x1b[38;5;201;48;5;300m"), { ch: "x", fg: 201, bg: 255, link: null });
  assert.deepEqual(cell("\x1b[38;2;255;0;0;48;2m"), { ch: "x", fg: 9, bg: 0, link: null });
  assert.deepEqual(cell("\x1b[38;5m"), { ch: "x", fg: 0, bg: 0, link: null });
  assert.deepEqual(cell("\x1b[31m\x1b[m"), { ch: "x", fg: 7, bg: 0, link: null });
  assert.deepEqual(cell("\x1b[38;9m"), { ch: "x", fg: 7, bg: 0, link: null });
});

test("OSC 8 links and hotspots are kept only when safe", () => {
  const b = buffer("\x1b]8;;key:Q\x1b\\Q\x1b]8;;\x1b\\x\x1b]8;;https://pizon.com/\x07L\x1b]8;;javascript:alert(1)\x07J\x1b]2;title\x07T");
  assert.equal(b.cellAt(0, 0).link, "key:Q");
  assert.equal(b.cellAt(0, 1).link, null);
  assert.equal(b.cellAt(0, 2).link, "https://pizon.com/");
  assert.equal(b.cellAt(0, 3).link, null);
  assert.equal(b.cellAt(0, 4).link, null);
  assert.equal(isAllowedLink("key:12345678901234567"), false);
  assert.equal(isAllowedLink("HTTP://x.y/"), true);
});

test("buffer copies, replaces cells and helpers behave", () => {
  const b = buffer("hi");
  const copy = b.clone();
  copy.put("!");
  assert.equal(b.rowText(0), "hi");
  assert.equal(copy.rowText(0), "hi!");
  b.dirty.clear();
  b.setCells(copy.cells);
  assert.equal(b.rowText(0), "hi!");
  assert.equal(b.dirty.size, 3);
  assert.equal(b.row(0).length, 10);
  assert.ok(sameCell(blankCell(), blankCell()));
  assert.ok(!sameCell(blankCell(1), blankCell(2)));
  assert.equal(clamp(5, 0, 3), 3);
  assert.equal(clamp(-1, 0, 3), 0);
});

test("palette matches the chart and snaps colors", () => {
  assert.equal(PALETTE.length, 256);
  assert.equal(buildPalette().length, 256);
  assert.equal(hex(1), "#800000");
  assert.equal(hex(16 + 36 + 12 + 3), "#5f87af");
  assert.equal(hex(255), "#eeeeee");
  assert.equal(nearestIndex(255, 0, 0), 9);
  assert.equal(nearestIndex(1, 1, 1), 0);
});

test("pacing hands out characters per elapsed time, carrying fractions", () => {
  assert.deepEqual(BAUD_RATES, [0, 2400, 9600]);
  assert.equal(charsPerSecond(0), Infinity);
  assert.equal(charsPerSecond(9600), 960);
  const p = new Pacer(2400);
  assert.equal(p.instant, false);
  assert.equal(p.take(10), 2);
  assert.equal(p.take(10), 2);
  p.setBaud(2400);
  assert.equal(p.take(1), 0);
  assert.equal(p.take(4), 1);
  assert.equal(p.take(-5), 0);
  p.setBaud(0);
  assert.equal(p.instant, true);
  assert.equal(p.take(10), Infinity);
});

test("animations parse from JSON or split from ANSI movies", () => {
  const json = anim.parseAnimation('  { "fps": 99, "loop": true, "frames": ["a", 5, "b"] }');
  assert.deepEqual(json, { fps: 30, loop: true, frames: ["a", "b"] });
  assert.deepEqual(anim.parseAnimation('{"frames": "x"}'), { fps: anim.DEFAULT_FPS, loop: false, frames: [] });

  const movie = "\x1b[2J\x1b[Hone\x1b[2J\x1b[H\x1b[0m \x1b[1;1Htwo\x1b]8;;x\x1b\\";
  assert.deepEqual(anim.splitMovie(movie), ["\x1b[2J\x1b[Hone", "\x1b[2J\x1b[H\x1b[0m \x1b[1;1Htwo\x1b]8;;x\x1b\\"]);
  assert.deepEqual(anim.parseAnimation(movie, { fps: "5", loop: true }).fps, 5);
  assert.equal(anim.splitMovie("\x1b[2J".repeat(3)).length, 0);
  assert.equal(anim.splitMovie(("\x1b[2Jx").repeat(300)).length, anim.MAX_FRAMES);
  assert.equal(anim.hasContent("\x1b[31m  "), false);

  assert.equal(anim.validFps(0), anim.DEFAULT_FPS);
  assert.equal(anim.validFps("nope"), anim.DEFAULT_FPS);
  assert.equal(anim.validFps(0.5), 1);
  assert.equal(anim.frameAt({ frames: [], fps: 10, loop: false }, 0), -1);
  assert.equal(anim.frameAt({ frames: ["a", "b"], fps: 10, loop: false }, 150), 1);
  assert.equal(anim.frameAt({ frames: ["a", "b"], fps: 10, loop: false }, 250), -1);
  assert.equal(anim.frameAt({ frames: ["a", "b"], fps: 10, loop: true }, 250), 0);
});

test("art decodes as UTF-8, or CP437 when it isn't, without the SAUCE record", () => {
  assert.equal(decodeCp437(new Uint8Array([0x41, 0xdb, 0xb0, 0xfe])), "A█░■");
  assert.deepEqual([...stripSauce(new Uint8Array([1, 2, 0x1a, 3]))], [1, 2]);
  assert.deepEqual([...stripSauce(new Uint8Array([1, 2]))], [1, 2]);
  assert.equal(decodeArt(new TextEncoder().encode("█ok")), "█ok");
  assert.equal(decodeArt(new Uint8Array([0xdb, 0x41, 0x1a, 0x53])), "█A");
});

test("transitions move from the old screen to the new one", () => {
  const cols = 4;
  const from = Array.from({ length: 8 }, (_, i) => ({ ...blankCell(), ch: i % 2 ? "x" : " " }));
  const to = Array.from({ length: 8 }, (_, i) => ({ ...blankCell(), ch: i < 4 ? "A" : " ", fg: i === 7 ? 7 : 3 }));
  to[6] = { ...to[6], fg: 7 };
  const text = (cells) => cells.map((c) => c.ch).join("");

  assert.deepEqual(EFFECTS, Object.keys(DURATIONS));
  for (const effect of EFFECTS) {
    const render = plan(effect, from, to, cols, 3);
    assert.equal(text(render(1)), text(to), effect);
    assert.equal(render(-1).length, 8, effect);
    assert.equal(render(0.5).length, 8, effect);
  }
  assert.equal(text(plan("bogus", from, to, cols, 1)(0)), text(to));

  const wipe = plan("wipe", from, to, cols, 1);
  assert.equal(text(wipe(0.5)), "AAAA" + text(from.slice(4)));
  assert.equal(text(plan("scrolloff", from, to, cols, 1)(0.5)), text(from.slice(4)) + text(to.slice(4)));
  assert.ok(text(plan("draw", from, to, cols, 1)(0.1)).includes("█"));
  assert.ok(text(plan("undraw", from, to, cols, 1)(0.1)).includes("█"));

  const spray = plan("spray", from, to, cols, 5);
  const seen = new Set();
  for (let p = 0; p < 1; p += 0.01) {
    spray(p).forEach((c) => seen.add(c.ch));
  }
  for (const stage of SPRAY_STAGES) {
    assert.ok(seen.has(stage), stage);
  }
  const random = mulberry32(1);
  const r = random();
  assert.ok(r >= 0 && r < 1);
  assert.notEqual(random(), r);
});

test("RIPscrip commands parse, draw and hit-test", () => {
  assert.equal(rip.megaNum("1Z"), 71);
  assert.ok(Number.isNaN(rip.megaNum("!!")));
  assert.equal(rip.toMegaNum(71), "1Z");
  assert.equal(rip.toMegaNum(-3, 3), "000");
  assert.equal(rip.hex(1), hex(4));
  assert.equal(rip.hex(17), hex(4));
  assert.deepEqual(rip.DOS_TO_ANSI.length, 16);

  const text = [
    "not a rip line",
    "!|c0E|S000C|Y00000002|*|L00001010|R00001010|B00001010|C0A0A05|o0A0A0503",
    "!|P0200000A0A|p020000050A|@0A0BHello|X99|1Q00",
    "!|1U00000Z0Z1D000000<>Bulletins|1U1010202032000000Plain",
    "",
  ].join("\r\n");
  const ops = rip.parseRip(text);
  assert.deepEqual(ops.map((o) => o.op), ["reset", "line", "rect", "bar", "circle", "oval", "poly", "fillpoly", "text", "button", "button"]);
  assert.equal(ops[1].color, 14);
  assert.equal(ops[3].color, 12);
  assert.equal(ops[8].size, 2);
  assert.equal(ops[8].text, "Hello");
  assert.deepEqual(ops[6].points, [[0, 0], [10, 10]]);
  assert.equal(ops[9].key, "1");
  assert.equal(ops[9].label, "Bulletins");
  assert.equal(ops[10].label, "Plain");
  assert.equal(rip.parseRip("!|Y0000000000|@0000x")[0].size, 1);

  const ctx = recordingContext();
  const buttons = rip.drawRip(ctx, ops);
  assert.equal(buttons.length, 2);
  for (const name of ["fillRect", "strokeRect", "arc", "ellipse", "fill", "fillText", "closePath", "lineTo"]) {
    assert.ok(ctx.calls.some((c) => c[0] === name), name);
  }
  assert.equal(rip.hitTest(buttons, 5, 5), "1");
  assert.equal(rip.hitTest(buttons, 600, 300), null);
  assert.equal(rip.RIP_WIDTH, 640);
  assert.equal(rip.RIP_HEIGHT, 350);
});

test("the handshake dials the BBS number and is repeatable", () => {
  const a = handshakeSchedule(mulberry32(7));
  const b = handshakeSchedule(mulberry32(7));
  assert.deepEqual(a, b);
  assert.deepEqual(a[1].freqs, DTMF[PHONE_NUMBER[0]]);
  assert.equal(a.length, 1 + PHONE_NUMBER.length + 1 + 24 + 1);
  assert.ok(a.every((e, i) => i === 0 || e.at >= a[i - 1].at));
});

test("the .sh converter builds frames from printf, echo, clear and sleep", () => {
  assert.equal(sh.unescapeShell("\\e[1m\\033\\x1b\\x1B\\n\\r\\t\\\\\\'\\\""), "\x1b[1m\x1b\x1b\x1b\n\r\t\\'\"");
  assert.equal(sh.quotedArgument("printf 'hi';"), "hi");
  assert.equal(sh.quotedArgument('echo -ne "x"'), "x");
  assert.equal(sh.quotedArgument("ls -l"), null);
  const script = ["# comment", "", "clear", "printf '\\e[31mA'", "echo -e \"B\"", "sleep 0.25", "tput clear", "echo 'C'", "sleep 0", "ls"].join("\n");
  assert.deepEqual(sh.convert(script, null), { fps: 4, frames: ["\x1b[31mAB\r\n", "C\r\n"] });
  assert.equal(sh.convert("printf 'x'\nsleep 0.5", 12).fps, 12);
  assert.equal(sh.convert("", null).fps, 10);
});
