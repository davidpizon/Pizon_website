/**
 * ANSImation loading: multi-frame ANSI art, either a JSON file ({ fps, frames, loop }) or an
 * ".ANS movie" (one stream of frames separated by cursor-home / clear-screen codes).
 * @module ansimation
 */

/** Frames per second used when a movie does not say. */
export const DEFAULT_FPS = 10;

/** The most frames accepted (keeps memory and download size sane). */
export const MAX_FRAMES = 240;

/**
 * Splits an ANS movie into frames at each clear-screen (ESC[2J) or cursor-home (ESC[H / ESC[1;1H)
 * that follows drawn content.
 * @param {string} text The decoded movie text.
 * @returns {string[]} The frames (each starting with a home sequence).
 */
export function splitMovie(text) {
  const marker = /\x1b\[(?:2J|H|1;1H|0;0H)/g;
  const frames = [];
  let start = 0;
  let match;
  while ((match = marker.exec(text)) !== null) {
    const chunk = text.slice(start, match.index);
    if (hasContent(chunk)) {
      frames.push(chunk);
      start = match.index;
    }
  }
  const last = text.slice(start);
  if (hasContent(last)) {
    frames.push(last);
  }
  return frames.slice(0, MAX_FRAMES);
}

/**
 * Checks whether a chunk draws anything besides escape sequences and whitespace.
 * @param {string} chunk A piece of ANSI text.
 * @returns {boolean} True when it contains visible characters.
 */
export function hasContent(chunk) {
  return chunk.replace(/\x1b\[[0-9;?]*[@-~]/g, "").replace(/\x1b\][^\x1b]*\x1b\\/g, "").trim().length > 0;
}

/**
 * Parses an animation file.
 * @param {string} text The file text.
 * @param {{fps?: number, loop?: boolean}} [sidecar] Settings from a sidecar .json for movie files.
 * @returns {{fps: number, loop: boolean, frames: string[]}} The animation.
 */
export function parseAnimation(text, sidecar = {}) {
  const trimmed = text.trimStart();
  if (trimmed.startsWith("{")) {
    const data = JSON.parse(trimmed);
    const frames = Array.isArray(data.frames) ? data.frames.filter((f) => typeof f === "string") : [];
    return { fps: validFps(data.fps), loop: data.loop === true, frames: frames.slice(0, MAX_FRAMES) };
  }
  return { fps: validFps(sidecar.fps), loop: sidecar.loop === true, frames: splitMovie(text) };
}

/**
 * Normalizes a frame rate.
 * @param {unknown} fps The requested rate.
 * @returns {number} A rate between 1 and 30.
 */
export function validFps(fps) {
  const n = Number(fps);
  return Number.isFinite(n) && n > 0 ? Math.min(30, Math.max(1, n)) : DEFAULT_FPS;
}

/**
 * Works out which frame to show after a given time.
 * @param {{fps: number, loop: boolean, frames: string[]}} anim The animation.
 * @param {number} elapsedMs Time since playback started.
 * @returns {number} The frame index, or -1 when a non-looping animation has finished.
 */
export function frameAt(anim, elapsedMs) {
  if (anim.frames.length === 0) {
    return -1;
  }
  const index = Math.floor((elapsedMs * anim.fps) / 1000);
  if (anim.loop) {
    return index % anim.frames.length;
  }
  return index < anim.frames.length ? index : -1;
}
