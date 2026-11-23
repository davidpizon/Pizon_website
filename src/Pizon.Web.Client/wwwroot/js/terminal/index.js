/**
 * The PIZON BBS terminal: ties the ANSI parser, cell buffer, renderer, pacing, transitions,
 * ANSImation player, background animation slots, RIP panel, input and audio together, and exposes them to
 * .NET interop.
 * @module terminal
 */

import { AnsiParser } from "./ansi-parser.js";
import { CellBuffer, COLS, ROWS } from "./cell-buffer.js";
import { DomRenderer } from "./dom-renderer.js";
import { Pacer } from "./pacing.js";
import { plan, DURATIONS, EFFECTS, mulberry32 } from "./transitions.js";
import { parseAnimation, frameAt } from "./ansimation.js";
import { SlotLayer } from "./compositor.js";
import { parseRip, drawRip, hitTest, RIP_WIDTH, RIP_HEIGHT } from "./rip.js";
import { decodeArt } from "./cp437.js";
import { InputController } from "./input.js";
import { ModemAudio } from "./audio.js";
import { PALETTE, hex } from "./palette.js";

/**
 * Builds the CSS rules for the 256 foreground/background classes (f0…f255, b0…b255).
 * @returns {string} The stylesheet text.
 */
export function paletteCss() {
  let css = ":root{";
  PALETTE.forEach((_, i) => {
    css += `--c${i}:${hex(i)};`;
  });
  css += "}";
  PALETTE.forEach((_, i) => {
    css += `.f${i}{color:var(--c${i})}.b${i}{background-color:var(--c${i})}`;
  });
  return css;
}

/**
 * Draws a frame buffer into a region of a larger buffer.
 * @param {object[]} target Destination cells (full screen).
 * @param {number} targetCols Destination width.
 * @param {CellBuffer} source The frame buffer.
 * @param {number} row Top row of the region.
 * @param {number} col Left column of the region.
 * @returns {object[]} A new array of cells with the frame drawn in.
 */
export function blit(target, targetCols, source, row, col) {
  const out = target.map((c) => ({ ...c }));
  for (let r = 0; r < source.rows; r++) {
    for (let c = 0; c < source.cols; c++) {
      const tr = row + r;
      const tc = col + c;
      if (tc < targetCols && tr * targetCols + tc < out.length) {
        out[tr * targetCols + tc] = { ...source.cellAt(r, c) };
      }
    }
  }
  return out;
}

/**
 * The terminal engine. Writes, transitions and animations run one at a time from a queue on
 * requestAnimationFrame, each resolving its promise when finished. Animation slots play alongside the
 * queue (paused while a transition or full-screen animation runs) until stopped or the screen is cleared.
 */
export class Terminal {
  /**
   * Creates the terminal.
   * @param {{screen: HTMLElement, doc: Document, raf: (cb: (t: number) => void) => number,
   *   reducedMotion: boolean, mobileInput?: HTMLInputElement|null, ripCanvas?: HTMLCanvasElement|null,
   *   audioCtor?: (new () => AudioContext), sink: {onKey: (k: string) => void, onSkip: () => void}}} env
   *   The environment (injected so tests can provide fakes).
   */
  constructor(env) {
    this.env = env;
    this.buffer = new CellBuffer(COLS, ROWS);
    this.parser = new AnsiParser((t) => this.onToken(t));
    this.renderer = new DomRenderer(env.screen, env.doc, ROWS);
    this.pacer = new Pacer(0);
    this.queue = [];
    this.slots = new SlotLayer();
    this.active = null;
    this.lastTime = null;
    this.scheduled = false;
    this.seed = 1;
    this.audio = new ModemAudio(env.audioCtor, mulberry32(7));
    this.soundOn = false;
    this.input = new InputController(env.doc, env.screen, env.mobileInput ?? null, {
      onKey: (k) => this.onKey(k),
      onSkip: () => this.env.sink.onSkip(),
      onGesture: () => this.audio.unlock(),
    });
    this.ripButtons = [];
    this.renderer.render(this.buffer, { all: true });
  }

  /**
   * Handles one parsed token (BEL beeps; everything else draws, and clearing the screen stops the slots).
   * @param {object} token The token.
   * @returns {void}
   */
  onToken(token) {
    if (token.t === "ctl" && token.ch === "\x07") {
      this.beep();
      return;
    }
    if (token.t === "csi" && token.final === "J" && token.params[0] === 2) {
      this.slots.stopAll();
    }
    this.buffer.apply(token);
  }

  /**
   * Routes a key: while a transition or animation runs, the key fast-forwards it instead.
   * @param {string} key The key.
   * @returns {void}
   */
  onKey(key) {
    if (this.active && this.active.consumesKeys) {
      this.active.skip = true;
      this.schedule();
      return;
    }
    if (this.active) {
      this.active.skip = true;
      this.schedule();
    }
    this.env.sink.onKey(key);
  }

  /**
   * Sets the painting speed.
   * @param {number} baud 0 (instant), 2400 or 9600.
   * @returns {void}
   */
  setBaud(baud) {
    this.pacer.setBaud(this.env.reducedMotion ? 0 : baud);
  }

  /**
   * Turns synthesized sound on or off.
   * @param {boolean} on Whether sound is wanted.
   * @returns {void}
   */
  setSound(on) {
    this.soundOn = on;
    if (!on) {
      this.audio.stop(0);
    }
  }

  /**
   * Queues ANSI text; resolves once it has been painted (at the current baud rate).
   * @param {string} text ANSI text.
   * @returns {Promise<void>} Completes when drawn.
   */
  write(text) {
    return this.enqueue({ kind: "write", text, pos: 0, consumesKeys: false });
  }

  /**
   * Renders ANSI text off-screen and animates from the current screen to it.
   * @param {string} effect One of the transition effects.
   * @param {string} text ANSI text applied on top of the current screen (start it with ESC[2J for a new screen).
   * @returns {Promise<void>} Completes when the effect ends.
   */
  transition(effect, text) {
    const name = EFFECTS.includes(effect) ? effect : "none";
    return this.enqueue({ kind: "transition", effect: name, text, consumesKeys: name !== "none" });
  }

  /**
   * Plays an ANSImation.
   * @param {string} animationText The animation file text.
   * @param {{fps?: number, loop?: boolean, row?: number, col?: number, width?: number, height?: number,
   *   maxMs?: number}} options Playback options; width/height select a region (e.g. the SysOp cam box).
   * @returns {Promise<void>} Completes when playback ends or a key stops it.
   */
  playAnimation(animationText, options = {}) {
    const anim = parseAnimation(animationText, options);
    return this.enqueue({ kind: "anim", anim, options, consumesKeys: true });
  }

  /**
   * Starts an animation playing in a box of the screen, behind the queue (see compositor.js).
   * @param {string} name The slot's name; starting it again replaces it.
   * @param {{row?: number, col?: number, width: number, height: number, frames: string[], delays?: number[],
   *   loop?: boolean, maxMs?: number}} spec The box (0-based) and the frames' ANSI.
   * @returns {void}
   */
  startSlot(name, spec) {
    this.slots.start(name, spec);
    this.schedule();
  }

  /**
   * Stops a slot, leaving its current frame on screen.
   * @param {string} name The slot's name.
   * @returns {void}
   */
  stopSlot(name) {
    this.slots.stop(name);
  }

  /**
   * Clears the screen immediately.
   * @returns {Promise<void>} Completes when cleared.
   */
  clear() {
    return this.write("\x1b[0m\x1b[2J\x1b[H");
  }

  /**
   * Arms or disarms skip-on-mouse-move/tap/key.
   * @param {boolean} armed Whether skipping is allowed.
   * @returns {void}
   */
  armSkip(armed) {
    this.input.armSkip(armed);
  }

  /**
   * Turns line-input mode on or off (focuses the hidden input for phone keyboards).
   * @param {boolean} on Whether a line is being typed.
   * @returns {void}
   */
  setLineMode(on) {
    this.input.setLineMode(on);
  }

  /**
   * Plays the dial-up handshake when sound is on.
   * @returns {number} Its length in seconds (0 when silent).
   */
  playHandshake() {
    return this.soundOn ? this.audio.playHandshake() : 0;
  }

  /**
   * Stops the handshake audio with a short fade.
   * @returns {void}
   */
  stopAudio() {
    this.audio.stop(150);
  }

  /**
   * Beeps when sound is on.
   * @returns {void}
   */
  beep() {
    if (this.soundOn) {
      this.audio.beep();
    }
  }

  /**
   * Shows the RIP graphics panel and waits for a button or key.
   * @param {string} ripText The .rip file text.
   * @returns {Promise<string>} The key chosen ("Enter" when any other key or a click outside buttons).
   */
  showRip(ripText) {
    const canvas = this.env.ripCanvas;
    const ctx = canvas?.getContext?.("2d");
    if (!canvas || !ctx) {
      return Promise.resolve("Enter");
    }
    canvas.width = RIP_WIDTH;
    canvas.height = RIP_HEIGHT;
    this.ripButtons = drawRip(ctx, parseRip(ripText));
    canvas.hidden = false;
    this.env.screen.hidden = true;
    return new Promise((resolve) => {
      const done = (key) => {
        canvas.hidden = true;
        this.env.screen.hidden = false;
        canvas.removeEventListener("click", onClick);
        this.ripResolve = null;
        resolve(key);
      };
      const onClick = (e) => {
        const rect = canvas.getBoundingClientRect();
        const x = ((e.clientX - rect.left) / rect.width) * RIP_WIDTH;
        const y = ((e.clientY - rect.top) / rect.height) * RIP_HEIGHT;
        done(hitTest(this.ripButtons, x, y) ?? "Enter");
      };
      canvas.addEventListener("click", onClick);
      this.ripResolve = done;
    });
  }

  /**
   * Gets the plain text of the screen (for tests and accessibility snapshots).
   * @returns {string[]} The 25 row texts.
   */
  snapshot() {
    return Array.from({ length: this.buffer.rows }, (_, r) => this.buffer.rowText(r));
  }

  /**
   * Adds an item to the queue.
   * @param {object} item The queue item.
   * @returns {Promise<void>} Resolves when the item finishes.
   */
  enqueue(item) {
    return new Promise((resolve) => {
      item.resolve = resolve;
      this.queue.push(item);
      this.schedule();
    });
  }

  /**
   * Requests an animation frame if one is not already pending.
   * @returns {void}
   */
  schedule() {
    if (!this.scheduled) {
      this.scheduled = true;
      this.env.raf((t) => this.frame(t));
    }
  }

  /**
   * Runs one animation frame: advances the active item, then the slots, and draws.
   * @param {number} time The frame timestamp in milliseconds.
   * @returns {void}
   */
  frame(time) {
    this.scheduled = false;
    const elapsed = this.lastTime === null ? 16 : Math.min(100, time - this.lastTime);
    this.lastTime = time;
    const busy = this.runQueue(time, elapsed);
    if ((!this.active || this.active.kind === "write") && this.slots.tick(this.buffer, time, this.env.reducedMotion)) {
      this.renderer.render(this.buffer);
    }
    if (busy || this.slots.active) {
      this.schedule();
    } else {
      this.lastTime = null;
    }
  }

  /**
   * Advances the queue: finishes as many items as this frame allows.
   * @param {number} time The frame timestamp in milliseconds.
   * @param {number} elapsed Milliseconds since the last frame.
   * @returns {boolean} True when an item is still running.
   */
  runQueue(time, elapsed) {
    while (this.active || this.queue.length > 0) {
      if (!this.active) {
        this.active = this.queue.shift();
        this.active.started = time;
      }
      if (!this.step(this.active, time, elapsed)) {
        return true;
      }
      const done = this.active;
      this.active = null;
      done.resolve();
    }
    return false;
  }

  /**
   * Advances an item.
   * @param {object} item The active item.
   * @param {number} time The frame time.
   * @param {number} elapsed Milliseconds since the last frame.
   * @returns {boolean} True when the item is complete.
   */
  step(item, time, elapsed) {
    if (item.kind === "write") {
      return this.stepWrite(item, elapsed);
    }
    if (item.kind === "transition") {
      return this.stepTransition(item, time);
    }
    return this.stepAnimation(item, time);
  }

  /**
   * Paints as much of a write as the baud budget allows.
   * @param {object} item The write item.
   * @param {number} elapsed Milliseconds since the last frame.
   * @returns {boolean} True when everything is drawn.
   */
  stepWrite(item, elapsed) {
    const budget = item.skip ? Infinity : this.pacer.take(elapsed);
    const chars = [...item.text.slice(item.pos)];
    const take = budget === Infinity ? chars.length : Math.min(chars.length, budget);
    const chunk = chars.slice(0, take).join("");
    this.parser.feed(chunk);
    item.pos += chunk.length;
    this.renderer.render(this.buffer);
    return item.pos >= item.text.length;
  }

  /**
   * Advances a transition effect.
   * @param {object} item The transition item.
   * @param {number} time The frame time.
   * @returns {boolean} True when the effect is complete.
   */
  stepTransition(item, time) {
    if (!item.render) {
      const from = this.buffer.cells.map((c) => ({ ...c }));
      this.parser.feed(item.text);
      item.render = plan(item.effect, from, this.buffer.cells, this.buffer.cols, this.seed++);
      item.duration = this.env.reducedMotion ? 0 : DURATIONS[item.effect];
    }
    const progress = item.skip || item.duration === 0 ? 1 : (time - item.started) / item.duration;
    if (progress >= 1) {
      this.renderer.render(this.buffer, { all: true });
      return true;
    }
    this.renderer.render(this.buffer, { cells: item.render(progress) });
    return false;
  }

  /**
   * Advances an ANSImation.
   * @param {object} item The animation item.
   * @param {number} time The frame time.
   * @returns {boolean} True when playback is over.
   */
  stepAnimation(item, time) {
    const { anim, options } = item;
    const elapsed = time - item.started;
    const overTime = options.maxMs !== undefined && elapsed >= options.maxMs;
    let index = frameAt(anim, elapsed);
    const stop = item.skip || overTime || index < 0 || this.env.reducedMotion;
    if (stop) {
      index = this.env.reducedMotion && anim.frames.length > 0 ? 0 : index;
      if (index >= 0 && (this.env.reducedMotion || item.lastIndex === undefined)) {
        this.drawFrame(anim.frames[index], options);
      }
      if (options.width) {
        this.renderer.render(this.buffer, { all: true });
      }
      return true;
    }
    if (index !== item.lastIndex) {
      item.lastIndex = index;
      this.drawFrame(anim.frames[index], options);
    }
    return false;
  }

  /**
   * Draws one animation frame, full screen or into a region.
   * @param {string} frameText The frame's ANSI text.
   * @param {{row?: number, col?: number, width?: number, height?: number}} options Region options.
   * @returns {void}
   */
  drawFrame(frameText, options) {
    if (!options.width) {
      this.parser.feed("\x1b[0m\x1b[2J\x1b[H" + frameText);
      this.renderer.render(this.buffer, { all: true });
      return;
    }
    const box = new CellBuffer(options.width, options.height ?? 12);
    const boxParser = new AnsiParser((t) => box.apply(t));
    boxParser.feed(frameText);
    const merged = blit(this.buffer.cells, this.buffer.cols, box, options.row ?? 0, options.col ?? 0);
    this.buffer.setCells(merged);
    this.renderer.render(this.buffer);
  }
}

/** The page's terminal instance. */
let current = null;

/**
 * Creates the terminal for the page (called by Terminal.razor).
 * @param {HTMLElement} screen The screen element.
 * @param {{invokeMethodAsync: (name: string, ...args: unknown[]) => Promise<unknown>}} dotnet The .NET callback object.
 * @param {HTMLInputElement|null} mobileInput The hidden input for phone keyboards.
 * @param {HTMLCanvasElement|null} ripCanvas The RIP graphics canvas.
 * @returns {void}
 */
export function init(screen, dotnet, mobileInput, ripCanvas) {
  if (current) {
    current.input.dispose();
  }
  const doc = screen.ownerDocument;
  const style = doc.createElement("style");
  style.textContent = paletteCss();
  doc.head.appendChild(style);
  const win = doc.defaultView;
  current = new Terminal({
    screen,
    doc,
    raf: (cb) => win.requestAnimationFrame(cb),
    reducedMotion: Boolean(win.matchMedia?.("(prefers-reduced-motion: reduce)").matches),
    mobileInput,
    ripCanvas,
    audioCtor: win.AudioContext ?? win.webkitAudioContext,
    sink: {
      onKey: (k) => {
        if (current?.ripResolve) {
          current.ripResolve(k);
          return;
        }
        dotnet.invokeMethodAsync("OnKey", k);
      },
      onSkip: () => dotnet.invokeMethodAsync("OnSkip"),
    },
  });
  doc.getElementById("boot")?.remove();
}

/**
 * Gets the page's terminal (throws if init() has not run).
 * @returns {Terminal} The terminal.
 */
function term() {
  if (!current) {
    throw new Error("Terminal not initialized.");
  }
  return current;
}

/**
 * Interop: writes ANSI text and waits for it to be painted.
 * @param {string} text ANSI text.
 * @returns {Promise<void>} Completes when drawn.
 */
export function write(text) {
  return term().write(text);
}

/**
 * Interop: runs a transition to new content.
 * @param {string} effect The effect name.
 * @param {string} text The ANSI text.
 * @returns {Promise<void>} Completes when the effect ends.
 */
export function transition(effect, text) {
  return term().transition(effect, text);
}

/**
 * Interop: plays an animation from a URL; resolves false when it can't be loaded.
 * @param {string} url The animation URL.
 * @param {object} options Playback options (see Terminal.playAnimation).
 * @returns {Promise<boolean>} Whether an animation played.
 */
export async function playAnimationUrl(url, options) {
  const text = await fetchArt(url);
  if (text === null) {
    return false;
  }
  await term().playAnimation(text, options ?? {});
  return true;
}

/**
 * Interop: starts an animation slot (see Terminal.startSlot).
 * @param {string} name The slot's name.
 * @param {object} spec The box and frames.
 * @returns {void}
 */
export function startSlot(name, spec) {
  term().startSlot(name, spec);
}

/**
 * Interop: stops an animation slot.
 * @param {string} name The slot's name.
 * @returns {void}
 */
export function stopSlot(name) {
  term().stopSlot(name);
}

/**
 * Interop: loads an art file (.ans in CP437 or UTF-8) and returns its text.
 * @param {string} url The file URL.
 * @returns {Promise<string|null>} The decoded text, or null when missing.
 */
export async function fetchArt(url) {
  try {
    const response = await fetch(url, { cache: "no-cache" });
    if (!response.ok) {
      return null;
    }
    return decodeArt(new Uint8Array(await response.arrayBuffer()));
  } catch {
    return null;
  }
}

/**
 * Interop: shows the RIP panel from a URL.
 * @param {string} url The .rip URL.
 * @returns {Promise<string|null>} The chosen key, or null when the file can't be loaded.
 */
export async function showRipUrl(url) {
  const text = await fetchArt(url);
  return text === null ? null : term().showRip(text);
}

/**
 * Interop: sets the baud rate.
 * @param {number} baud 0, 2400 or 9600.
 * @returns {void}
 */
export function setBaud(baud) {
  term().setBaud(baud);
}

/**
 * Interop: turns sound on or off.
 * @param {boolean} on Whether sound is on.
 * @returns {void}
 */
export function setSound(on) {
  term().setSound(on);
}

/**
 * Interop: arms or disarms skip.
 * @param {boolean} armed Whether skip is armed.
 * @returns {void}
 */
export function armSkip(armed) {
  term().armSkip(armed);
}

/**
 * Interop: toggles line-input mode.
 * @param {boolean} on Whether a line is being typed.
 * @returns {void}
 */
export function setLineMode(on) {
  term().setLineMode(on);
}

/**
 * Interop: plays the handshake audio.
 * @returns {number} Its length in seconds.
 */
export function playHandshake() {
  return term().playHandshake();
}

/**
 * Interop: stops audio.
 * @returns {void}
 */
export function stopAudio() {
  term().stopAudio();
}

/**
 * Interop: beeps.
 * @returns {void}
 */
export function beep() {
  term().beep();
}

/**
 * Interop: whether the visitor prefers reduced motion.
 * @returns {boolean} True when animations should be skipped.
 */
export function prefersReducedMotion() {
  return term().env.reducedMotion;
}

/**
 * Interop: reads a localStorage value (null when storage is unavailable).
 * @param {string} key The key.
 * @returns {string|null} The value.
 */
export function storageGet(key) {
  try {
    return globalThis.localStorage?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

/**
 * Interop: writes a localStorage value (failures are ignored).
 * @param {string} key The key.
 * @param {string} value The value.
 * @returns {void}
 */
export function storageSet(key, value) {
  try {
    globalThis.localStorage?.setItem(key, value);
  } catch {
    // Private mode or blocked storage: settings simply aren't remembered.
  }
}

/**
 * Interop: opens a URL in a new tab.
 * @param {string} url An http(s) URL.
 * @returns {void}
 */
export function openUrl(url) {
  if (/^https?:\/\//i.test(url)) {
    globalThis.open?.(url, "_blank", "noopener,noreferrer");
  }
}

/**
 * Interop: the local date as yyyy-MM-dd (for daily door rules).
 * @returns {string} Today's local date.
 */
export function localDate() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/**
 * Test hook: replaces the page terminal.
 * @param {Terminal|null} terminal The terminal to use.
 * @returns {void}
 */
export function setCurrentForTests(terminal) {
  current = terminal;
}
