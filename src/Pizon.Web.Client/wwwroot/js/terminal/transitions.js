/**
 * Character-cell transitions. Each effect is a pure function from (from cells, to cells, progress)
 * to the cells to show, so effects are deterministic (seeded) and testable without a DOM.
 * @module transitions
 */

import { blankCell, sameCell } from "./cell-buffer.js";

/** The available effects. Names match the C# Transition enum (lower-cased). */
export const EFFECTS = Object.freeze(["none", "wipe", "dissolve", "spray", "draw", "undraw", "scrolloff"]);

/** Default duration of each effect in milliseconds (all under ~600 ms, spray a little longer). */
export const DURATIONS = Object.freeze({ none: 0, wipe: 350, dissolve: 450, spray: 1400, draw: 550, undraw: 400, scrolloff: 400 });

/** Dither characters used for the spray-paint overspray, lightest first. */
export const SPRAY_STAGES = Object.freeze(["░", "▒", "▓"]);

/**
 * A small seeded random generator (mulberry32).
 * @param {number} seed Any 32-bit integer.
 * @returns {() => number} A function returning floats in [0, 1).
 */
export function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * Builds a transition plan: a function that renders the cells for a given progress (0..1).
 * @param {string} effect One of EFFECTS.
 * @param {object[]} from The cells currently shown.
 * @param {object[]} to The cells to end on.
 * @param {number} cols Screen width.
 * @param {number} seed Random seed.
 * @returns {(progress: number) => object[]} The frame renderer; progress 1 always yields `to`.
 */
export function plan(effect, from, to, cols, seed) {
  const rng = mulberry32(seed);
  const finish = (fn) => (p) => (p >= 1 ? to.map((c) => ({ ...c })) : fn(Math.max(0, p)));
  switch (effect) {
    case "wipe":
      return finish(wipe(from, to, cols));
    case "dissolve":
      return finish(dissolve(from, to, rng));
    case "spray":
      return finish(spray(from, to, cols, rng));
    case "draw":
      return finish(draw(from, to, false));
    case "undraw":
      return finish(draw(from, to, true));
    case "scrolloff":
      return finish(scrollOff(from, to, cols));
    default:
      return () => to.map((c) => ({ ...c }));
  }
}

/**
 * Top-down row wipe: rows switch to the target one by one.
 * @param {object[]} from Current cells.
 * @param {object[]} to Target cells.
 * @param {number} cols Screen width.
 * @returns {(p: number) => object[]} Frame renderer.
 */
function wipe(from, to, cols) {
  const rows = to.length / cols;
  return (p) => {
    const done = Math.floor(p * rows);
    return to.map((c, i) => ({ ...(Math.floor(i / cols) < done ? c : from[i]) }));
  };
}

/**
 * Random-cell dissolve.
 * @param {object[]} from Current cells.
 * @param {object[]} to Target cells.
 * @param {() => number} rng Random source.
 * @returns {(p: number) => object[]} Frame renderer.
 */
function dissolve(from, to, rng) {
  const order = to.map(() => rng());
  return (p) => to.map((c, i) => ({ ...(order[i] < p ? c : from[i]) }));
}

/**
 * Spray-paint reveal: changed cells appear in random clusters, passing through ░▒▓ before settling;
 * blank neighbours get brief overspray.
 * @param {object[]} from Current cells.
 * @param {object[]} to Target cells.
 * @param {number} cols Screen width.
 * @param {() => number} rng Random source.
 * @returns {(p: number) => object[]} Frame renderer.
 */
function spray(from, to, cols, rng) {
  const rows = to.length / cols;
  const centers = Array.from({ length: 7 }, () => ({ r: rng() * rows, c: rng() * cols, t: rng() * 0.45 }));
  const appear = to.map((cell, i) => {
    const r = Math.floor(i / cols);
    const c = i % cols;
    let best = Infinity;
    for (const k of centers) {
      const d = Math.hypot((r - k.r) * 2, c - k.c) / cols + k.t;
      best = Math.min(best, d);
    }
    return Math.min(0.8, best * 0.9 + rng() * 0.08);
  });
  const stageSpan = 0.12;
  return (p) =>
    to.map((cell, i) => {
      if (sameCell(cell, from[i])) {
        return { ...cell };
      }
      const local = (p - appear[i]) / stageSpan;
      if (local < 0) {
        return { ...from[i] };
      }
      if (local >= SPRAY_STAGES.length) {
        return { ...cell };
      }
      if (cell.ch === " " && cell.bg === 0) {
        // Overspray on blank target cells: a light speckle that fades back to blank.
        return local < 1 ? { ...blankCell(), ch: SPRAY_STAGES[0], fg: cell.fg === 7 ? 8 : cell.fg } : { ...cell };
      }
      return { ...cell, ch: SPRAY_STAGES[Math.floor(local)] };
    });
}

/**
 * Cursor draw: changed cells are revealed in reading order behind a moving block cursor.
 * With reverse=true the cells are removed from the end backwards (menu hide).
 * @param {object[]} from Current cells.
 * @param {object[]} to Target cells.
 * @param {boolean} reverse Draw backwards (undraw).
 * @returns {(p: number) => object[]} Frame renderer.
 */
function draw(from, to, reverse) {
  const changed = [];
  to.forEach((c, i) => {
    if (!sameCell(c, from[i])) {
      changed.push(i);
    }
  });
  if (reverse) {
    changed.reverse();
  }
  return (p) => {
    const count = Math.floor(p * changed.length);
    const out = from.map((c) => ({ ...c }));
    for (let k = 0; k < count; k++) {
      out[changed[k]] = { ...to[changed[k]] };
    }
    if (count < changed.length) {
      const at = changed[count];
      out[at] = { ...out[at], ch: "█", fg: 15 };
    }
    return out;
  };
}

/**
 * Scroll-off: the old screen scrolls up line by line, revealing the target from the bottom.
 * @param {object[]} from Current cells.
 * @param {object[]} to Target cells.
 * @param {number} cols Screen width.
 * @returns {(p: number) => object[]} Frame renderer.
 */
function scrollOff(from, to, cols) {
  const rows = to.length / cols;
  return (p) => {
    const shift = Math.floor(p * rows);
    return to.map((c, i) => {
      const r = Math.floor(i / cols);
      const source = r + shift;
      return source < rows ? { ...from[source * cols + (i % cols)] } : { ...c };
    });
  };
}
