/**
 * Tests for the modules that touch the page: the DOM renderer, input, Web Audio player and the
 * terminal engine with its interop exports, all against the hand-made fakes in support.mjs.
 * @module dom.test
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { JS, FakeDocument, FakeElement, FakeAudioContext, FrameClock, recorder, settle } from "./support.mjs";

const { DomRenderer, toRuns } = await import(new URL("dom-renderer.js", JS));
const { InputController, mapKey, SKIP_DISTANCE } = await import(new URL("input.js", JS));
const { ModemAudio } = await import(new URL("audio.js", JS));
const { CellBuffer, blankCell } = await import(new URL("cell-buffer.js", JS));
const { mulberry32 } = await import(new URL("transitions.js", JS));
const terminal = await import(new URL("index.js", JS));

const ripText = "!|1U00000Z0Z1D000000<>Bulletins";

/** The original localStorage property (Node's built-in one must not be touched: reading it opens a store). */
const storageDescriptor = Object.getOwnPropertyDescriptor(globalThis, "localStorage");

/**
 * Replaces globalThis.localStorage without reading the original.
 * @param {object} value The fake storage.
 * @returns {void}
 */
function replaceStorage(value) {
  Object.defineProperty(globalThis, "localStorage", { value, configurable: true, writable: true });
}

/**
 * Puts the original localStorage property back.
 * @returns {void}
 */
function restoreStorage() {
  if (storageDescriptor) {
    Object.defineProperty(globalThis, "localStorage", storageDescriptor);
  } else {
    delete globalThis.localStorage;
  }
}

/**
 * Builds a terminal on a fake page.
 * @param {object} [overrides] Environment overrides.
 * @returns {{t: object, doc: FakeDocument, screen: FakeElement, clock: FrameClock, keys: string[], skips: number[]}} The rig.
 */
function rig(overrides = {}) {
  const doc = new FakeDocument();
  const screen = doc.createElement("div");
  const clock = new FrameClock();
  const keys = [];
  const skips = [];
  const t = new terminal.Terminal({
    screen,
    doc,
    raf: (cb) => clock.raf(cb),
    reducedMotion: false,
    sink: { onKey: (k) => keys.push(k), onSkip: () => skips.push(1) },
    ...overrides,
  });
  return { t, doc, screen, clock, keys, skips };
}

test("runs merge identical cells and the renderer builds spans, buttons and links", () => {
  const cells = [blankCell(), blankCell(), { ...blankCell(), link: "key:Q" }, { ...blankCell(), link: "https://x.y/" }];
  assert.equal(toRuns(cells).length, 3);

  const doc = new FakeDocument();
  const root = doc.createElement("div");
  const r = new DomRenderer(root, doc, 2);
  const b = new CellBuffer(4, 2);
  b.setCells([...cells, ...cells]);
  r.render(b, { all: true });
  const [span, button, link] = root.children[0].children;
  assert.equal(span.tagName, "SPAN");
  assert.equal(button.tagName, "BUTTON");
  assert.equal(button.dataset.key, "Q");
  assert.equal(link.tagName, "A");
  assert.equal(link.rel, "noopener noreferrer");
  assert.ok(root.children[0].classList.contains("has-cursor"));
  assert.equal(b.dirty.size, 0);

  r.render(b, { cells: b.cells });
  assert.ok(!root.children[0].classList.contains("has-cursor"));
  b.cursor.col = 9;
  r.render(b, {});
  assert.equal(root.children[0].style.getPropertyValue("--cursor-col"), "3");
});

test("keys map to BBS keys and hotspot clicks, skips and phone input reach the sink", () => {
  assert.equal(mapKey({ key: "a", ctrlKey: true }), null);
  assert.equal(mapKey({ key: "Enter" }), "Enter");
  assert.equal(mapKey({ key: "F5" }), null);
  assert.equal(mapKey({ key: "é" }), "é");

  const doc = new FakeDocument();
  const screen = doc.createElement("div");
  const mobile = doc.createElement("input");
  const keys = [];
  let skips = 0;
  let gestures = 0;
  const input = new InputController(doc, screen, mobile, {
    onKey: (k) => keys.push(k),
    onSkip: () => skips++,
    onGesture: () => gestures++,
  });

  assert.equal(doc.dispatch("keydown", { key: "x" }).defaultPrevented, true);
  doc.dispatch("keydown", { key: "F1" });
  doc.dispatch("keydown", { key: "y", target: mobile });
  doc.dispatch("keydown", { key: "Enter", target: mobile });
  const hot = doc.createElement("button");
  hot.dataset.key = "M";
  const label = doc.createElement("span");
  hot.appendChild(label);
  screen.dispatch("click", { target: label });
  screen.dispatch("click", { target: screen });
  screen.dispatch("click", { target: null });
  mobile.value = "hi";
  mobile.dispatch("input", { target: mobile });
  mobile.dispatch("input", { target: {} });
  assert.deepEqual(keys, ["x", "Enter", "M", "h", "i"]);
  assert.equal(gestures, 4);

  input.setLineMode(true);
  assert.ok(mobile.focused);
  mobile.blur();
  screen.dispatch("click", { target: screen });
  assert.ok(mobile.focused);
  input.setLineMode(false);
  assert.ok(!mobile.focused);

  doc.dispatch("mousemove", { clientX: 0, clientY: 0 });
  input.armSkip(true);
  doc.dispatch("mousemove", { clientX: 0, clientY: 0 });
  doc.dispatch("mousemove", { clientX: SKIP_DISTANCE, clientY: 0 });
  assert.equal(skips, 0);
  doc.dispatch("mousemove", { clientX: SKIP_DISTANCE, clientY: 1 });
  assert.equal(skips, 1);
  input.armSkip(true);
  doc.dispatch("keydown", { key: "z" });
  input.armSkip(true);
  screen.dispatch("click", { target: hot });
  input.armSkip(true);
  doc.dispatch("pointerdown", { pointerType: "mouse" });
  doc.dispatch("pointerdown", { pointerType: "touch" });
  doc.dispatch("pointerdown", { pointerType: "touch" });
  assert.equal(skips, 4);

  input.dispose();
  assert.equal(doc.listenerCount("keydown") + screen.listenerCount("click") + mobile.listenerCount("input"), 0);

  const bare = new InputController(doc, screen, null, { onKey() {}, onSkip() {}, onGesture() {} });
  bare.setLineMode(true);
  bare.dispose();
});

test("modem audio waits for a gesture, plays tones and fades out", () => {
  const silent = new ModemAudio(undefined, mulberry32(1));
  silent.unlock();
  assert.equal(silent.playHandshake(), 0);
  silent.beep();
  silent.stop();

  const audio = new ModemAudio(FakeAudioContext, mulberry32(1));
  assert.equal(audio.playHandshake(), 0);
  audio.stop();
  audio.unlock();
  audio.unlock();
  assert.equal(audio.ctx.resumed, 1);
  assert.ok(audio.playHandshake() > 5);
  const master = audio.master;
  audio.beep();
  const oscillators = audio.ctx.nodes.filter((n) => n.frequency);
  assert.ok(oscillators.some((o) => o.type === "square"));
  assert.ok(oscillators.every((o) => o.start.calls.length === 1 && o.stop.calls.length === 1));
  audio.stop(100);
  assert.deepEqual(master.gain.linearRampToValueAtTime.calls, [[0, 10.1]]);
  assert.equal(audio.master, null);
});

test("the terminal paints writes at the baud rate and passes keys on", async () => {
  const { t, screen, clock, keys } = rig();
  assert.equal(screen.children.length, 25);
  let done = false;
  t.write("Hello\x07").then(() => (done = true));
  clock.pump();
  await settle();
  assert.ok(done);
  assert.equal(t.snapshot()[0], "Hello");

  t.setBaud(2400);
  const slow = t.write("x".repeat(20));
  assert.equal(clock.pump(16, 1), 1);
  assert.ok(t.snapshot()[0].length < 25);
  t.onKey("A");
  clock.pump();
  await slow;
  assert.equal(t.snapshot()[0], "Hello" + "x".repeat(20));
  t.onKey("B");
  assert.deepEqual(keys, ["A", "B"]);

  t.setSound(true);
  t.beep();
  assert.equal(t.playHandshake(), 0);
  t.stopAudio();
  t.setSound(false);
  assert.equal(t.playHandshake(), 0);
  t.armSkip(true);
  t.setLineMode(true);
  await Promise.all([t.clear(), clock.pump()]);
  assert.equal(t.snapshot()[0], "");
});

test("transitions animate to the new screen and a key skips them", async () => {
  const { t, clock, keys } = rig();
  const wipe = t.transition("wipe", "\x1b[2JNew");
  clock.pump(16, 2);
  t.onKey("Q");
  clock.pump();
  await wipe;
  assert.equal(t.snapshot()[0], "New");
  assert.deepEqual(keys, []);

  const full = t.transition("dissolve", "\x1b[2JDone");
  clock.pump(100);
  await full;
  assert.equal(t.snapshot()[0], "Done");

  const none = t.transition("bogus", "!");
  clock.pump();
  await none;
  assert.equal(t.snapshot()[0], "Done!");

  const calm = rig({ reducedMotion: true });
  calm.t.setBaud(2400);
  const quick = calm.t.transition("spray", "Calm");
  assert.equal(calm.clock.pump(), 1);
  await quick;
  assert.equal(calm.t.snapshot()[0], "Calm");
});

test("animations play full screen or in a box, and stop on a key, a time limit or reduced motion", async () => {
  const movie = JSON.stringify({ fps: 10, frames: ["one", "two", "three"] });
  const { t, clock } = rig();
  const play = t.playAnimation(movie);
  clock.pump(50, 3);
  assert.equal(t.snapshot()[0], "two");
  clock.pump(50);
  await play;
  assert.equal(t.snapshot()[0], "three");

  const looped = t.playAnimation(JSON.stringify({ fps: 10, loop: true, frames: ["a", "b"] }), { row: 1, col: 2, width: 3, height: 1 });
  clock.pump(16, 3);
  assert.equal(t.snapshot()[1], "  a");
  t.onKey("x");
  clock.pump();
  await looped;

  const capped = t.playAnimation(JSON.stringify({ fps: 10, loop: true, frames: ["c"] }), { maxMs: 100, width: 1 });
  clock.pump(60);
  await capped;
  assert.equal(t.snapshot()[0], "chree");

  const skipped = t.playAnimation(movie);
  t.onKey("y");
  clock.pump();
  await skipped;

  const calm = rig({ reducedMotion: true });
  const still = calm.t.playAnimation(movie);
  calm.clock.pump();
  await still;
  assert.equal(calm.t.snapshot()[0], "one");

  const empty = calm.t.playAnimation(JSON.stringify({ frames: [] }));
  calm.clock.pump();
  await empty;
});

test("the RIP panel draws, then resolves with a clicked button or Enter", async () => {
  const plain = rig();
  assert.equal(await plain.t.showRip(ripText), "Enter");

  const doc = new FakeDocument();
  const canvas = doc.createElement("canvas");
  const { t, screen } = rig({ ripCanvas: canvas });
  const first = t.showRip(ripText);
  assert.equal(canvas.hidden, false);
  assert.equal(screen.hidden, true);
  canvas.dispatch("click", { clientX: 10, clientY: 10 });
  assert.equal(await first, "1");
  assert.equal(screen.hidden, false);
  assert.equal(canvas.listenerCount("click"), 0);

  const second = t.showRip(ripText);
  canvas.dispatch("click", { clientX: 600, clientY: 340 });
  assert.equal(await second, "Enter");
});

test("blit copies a box into the screen without spilling over the edge", () => {
  const box = new CellBuffer(3, 2);
  box.put("a");
  const target = Array.from({ length: 8 }, () => blankCell());
  const out = terminal.blit(target, 4, box, 1, 2);
  assert.equal(out[6].ch, "a");
  assert.equal(target[6].ch, " ");
  assert.equal(terminal.paletteCss().split(".f").length, 257);
});

test("module exports drive the page terminal and fall back safely", async (t) => {
  terminal.setCurrentForTests(null);
  assert.throws(() => terminal.setBaud(0), /not initialized/);

  const clock = new FrameClock();
  const calls = [];
  const win = {
    requestAnimationFrame: (cb) => clock.raf(cb),
    matchMedia: () => ({ matches: false }),
    AudioContext: FakeAudioContext,
  };
  const doc = new FakeDocument(win);
  const screen = doc.createElement("div");
  const boot = doc.createElement("div");
  doc.body.appendChild(boot);
  doc.ids.boot = boot;
  const canvas = doc.createElement("canvas");
  const dotnet = { invokeMethodAsync: (...args) => calls.push(args) };
  terminal.init(screen, dotnet, null, canvas);
  assert.ok(boot.removed);
  assert.equal(doc.head.children.length, 1);
  terminal.init(screen, dotnet, null, canvas);
  assert.equal(doc.listenerCount("keydown"), 1);

  const write = terminal.write("Hi");
  clock.pump();
  await write;
  const fx = terminal.transition("none", "!");
  clock.pump();
  await fx;
  terminal.setBaud(9600);
  terminal.setSound(true);
  terminal.armSkip(true);
  doc.dispatch("keydown", { key: "s" });
  terminal.setLineMode(false);
  assert.ok(terminal.playHandshake() > 0);
  terminal.stopAudio();
  terminal.beep();
  assert.equal(terminal.prefersReducedMotion(), false);
  doc.dispatch("keydown", { key: "k" });
  assert.deepEqual(calls, [["OnSkip"], ["OnKey", "k"]]);

  const files = {
    "/anim.json": JSON.stringify({ frames: ["frame"] }),
    "/card.rip": ripText,
  };
  t.mock.method(globalThis, "fetch", async (url) => {
    if (url === "/boom") {
      throw new Error("offline");
    }
    const body = files[url];
    return { ok: body !== undefined, arrayBuffer: async () => new TextEncoder().encode(body ?? "").buffer };
  });
  assert.equal(await terminal.fetchArt("/missing"), null);
  assert.equal(await terminal.fetchArt("/boom"), null);
  assert.equal(await terminal.playAnimationUrl("/missing"), false);
  const played = terminal.playAnimationUrl("/anim.json");
  await settle();
  clock.pump();
  assert.equal(await played, true);
  const withOptions = terminal.playAnimationUrl("/anim.json", { maxMs: 0 });
  await settle();
  clock.pump();
  assert.equal(await withOptions, true);
  assert.equal(await terminal.showRipUrl("/missing"), null);
  const rip = terminal.showRipUrl("/card.rip");
  await settle();
  doc.dispatch("keydown", { key: "3" });
  assert.equal(await rip, "3");

  const store = new Map();
  replaceStorage({ getItem: (k) => store.get(k), setItem: (k, v) => store.set(k, v) });
  terminal.storageSet("baud", "2400");
  assert.equal(terminal.storageGet("baud"), "2400");
  assert.equal(terminal.storageGet("other"), null);
  const broken = () => {
    throw new Error("blocked");
  };
  replaceStorage({ getItem: broken, setItem: broken });
  terminal.storageSet("baud", "0");
  assert.equal(terminal.storageGet("baud"), null);
  restoreStorage();

  const open = recorder("open");
  globalThis.open = open;
  terminal.openUrl("javascript:alert(1)");
  terminal.openUrl("https://pizon.com/");
  delete globalThis.open;
  terminal.openUrl("https://pizon.com/");
  assert.deepEqual(open.calls, [["https://pizon.com/", "_blank", "noopener,noreferrer"]]);
  assert.match(terminal.localDate(), /^\d{4}-\d{2}-\d{2}$/);

  const quiet = new FakeDocument({ requestAnimationFrame: (cb) => clock.raf(cb), webkitAudioContext: FakeAudioContext });
  terminal.init(quiet.createElement("div"), dotnet, null, null);
  assert.equal(terminal.prefersReducedMotion(), false);
  terminal.setCurrentForTests(null);
  assert.ok(new FakeElement("div", quiet).text() === "");
});
