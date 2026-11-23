/**
 * Background animation slots: compiled art playing in a box of the screen while the page goes on (the
 * SysOp cam beside a bulletin). A slot never waits in the terminal's queue and never takes keys; on each
 * animation frame it stamps its current frame into the screen's cells, inside its own box only, so the text
 * and fields around it are never painted over. Clearing the screen stops every slot.
 * @module compositor
 */

import { AnsiParser } from "./ansi-parser.js";
import { CellBuffer } from "./cell-buffer.js";

/** The most frames a slot accepts (the screen compiler's limit too). */
export const MAX_SLOT_FRAMES = 240;

/** The shortest a slot frame shows, in milliseconds. */
export const MIN_DELAY = 33;

/** The longest a slot frame shows, in milliseconds. */
export const MAX_DELAY = 5000;

/**
 * Draws a frame's ANSI into a fresh box of cells.
 * @param {string} text The frame's ANSI, positioned from the box's top left.
 * @param {number} width The box width.
 * @param {number} height The box height.
 * @returns {CellBuffer} The frame's cells.
 */
export function parseFrame(text, width, height) {
  const box = new CellBuffer(width, height);
  new AnsiParser((t) => box.apply(t)).feed(text);
  return box;
}

/**
 * Works out which frame shows after a given time, each frame having its own delay.
 * @param {number[]} delays How long each frame shows, in milliseconds.
 * @param {number} elapsed Milliseconds since the slot started.
 * @param {boolean} loop Whether it starts again after the last frame.
 * @returns {number} The frame index (a finished, non-looping slot rests on its last frame).
 */
export function frameAt(delays, elapsed, loop) {
  const total = delays.reduce((sum, d) => sum + d, 0);
  if (total <= 0) {
    return 0;
  }
  let t = loop ? elapsed % total : Math.min(elapsed, total - 1);
  for (let i = 0; i < delays.length - 1; i++) {
    if (t < delays[i]) {
      return i;
    }
    t -= delays[i];
  }
  return delays.length - 1;
}

/**
 * Copies a box of cells into the screen's cells and marks the rows it touched for redrawing. Cells outside
 * the screen are dropped.
 * @param {CellBuffer} screen The screen buffer.
 * @param {CellBuffer} box The frame.
 * @param {number} row The box's top row (0-based).
 * @param {number} col The box's left column (0-based).
 * @returns {void}
 */
export function stamp(screen, box, row, col) {
  for (let r = 0; r < box.rows; r++) {
    const tr = row + r;
    if (tr < 0 || tr >= screen.rows) {
      continue;
    }
    for (let c = 0; c < box.cols; c++) {
      const tc = col + c;
      if (tc >= 0 && tc < screen.cols) {
        screen.cells[tr * screen.cols + tc] = { ...box.cellAt(r, c) };
      }
    }
    screen.dirty.add(tr);
  }
}

/**
 * The playing slots, by name.
 */
export class SlotLayer {
  /**
   * Creates an empty layer.
   */
  constructor() {
    this.slots = new Map();
  }

  /**
   * Gets whether any slot is playing.
   * @returns {boolean} True while a slot needs animation frames.
   */
  get active() {
    return this.slots.size > 0;
  }

  /**
   * Starts (or replaces) a slot. Its clock starts at the next tick.
   * @param {string} name The slot's name.
   * @param {{row?: number, col?: number, width: number, height: number, frames: string[], delays?: number[],
   *   loop?: boolean, maxMs?: number}} spec The box (0-based) and the frames.
   * @returns {void}
   */
  start(name, spec) {
    const frames = (Array.isArray(spec.frames) ? spec.frames : []).slice(0, MAX_SLOT_FRAMES);
    if (frames.length === 0 || !(spec.width > 0) || !(spec.height > 0)) {
      this.slots.delete(name);
      return;
    }
    const delays = frames.map((_, i) => Math.min(MAX_DELAY, Math.max(MIN_DELAY, Number(spec.delays?.[i]) || 125)));
    this.slots.set(name, {
      row: spec.row ?? 0,
      col: spec.col ?? 0,
      boxes: frames.map((f) => parseFrame(f, spec.width, spec.height)),
      delays,
      loop: spec.loop !== false,
      maxMs: spec.maxMs,
      started: null,
      shown: -1,
    });
  }

  /**
   * Stops a slot, leaving its current frame on screen.
   * @param {string} name The slot's name.
   * @returns {void}
   */
  stop(name) {
    this.slots.delete(name);
  }

  /**
   * Stops every slot (the screen was cleared).
   * @returns {void}
   */
  stopAll() {
    this.slots.clear();
  }

  /**
   * Advances every slot to the given time and stamps the ones whose frame changed. With reduced motion, a
   * slot shows its first frame and stops; one that runs past its time limit rests on its first frame.
   * @param {CellBuffer} screen The screen buffer.
   * @param {number} time The animation frame's timestamp, in milliseconds.
   * @param {boolean} still Whether motion is reduced.
   * @returns {boolean} True when anything was stamped.
   */
  tick(screen, time, still) {
    let drew = false;
    for (const [name, slot] of this.slots) {
      slot.started ??= time;
      const elapsed = time - slot.started;
      const over = still || (slot.maxMs !== undefined && elapsed >= slot.maxMs);
      const index = over ? 0 : frameAt(slot.delays, elapsed, slot.loop);
      if (index !== slot.shown) {
        slot.shown = index;
        stamp(screen, slot.boxes[index], slot.row, slot.col);
        drew = true;
      }
      if (over) {
        this.slots.delete(name);
      }
    }
    return drew;
  }
}
