/**
 * A small RIPscrip renderer: parses a subset of RIPscrip 1.54 commands and draws them on a 640x350
 * canvas using palette colors 0-15. Supported: c (color), S (fill style), L (line), R (rectangle),
 * B (bar), C (circle), o (filled oval), P/p (polygon / filled polygon), @ (text), Y (font size),
 * * (reset) and 1U (mouse button).
 * @module rip
 */

import { hex as paletteHex } from "./palette.js";

/** RIPscrip screen width. */
export const RIP_WIDTH = 640;

/** RIPscrip screen height (EGA). */
export const RIP_HEIGHT = 350;

/**
 * RIPscrip colors 0-15 use DOS/EGA order (1 blue, 4 red, 14 yellow); the BBS palette uses ANSI order
 * (1 red, 4 blue, 11 yellow). This maps a RIP color to its palette index.
 */
export const DOS_TO_ANSI = Object.freeze([0, 4, 2, 6, 1, 5, 3, 7, 8, 12, 10, 14, 9, 13, 11, 15]);

/**
 * Gets the CSS color for a RIP (DOS-order) color number.
 * @param {number} color A RIP color 0-15.
 * @returns {string} The hex color.
 */
export function hex(color) {
  return paletteHex(DOS_TO_ANSI[color & 15]);
}

/**
 * Decodes a MegaNum (base-36, digits 0-9 then A-Z).
 * @param {string} s The digits.
 * @returns {number} The value (NaN when invalid).
 */
export function megaNum(s) {
  let v = 0;
  for (const ch of s.toUpperCase()) {
    const d = Number.parseInt(ch, 36);
    if (Number.isNaN(d)) {
      return Number.NaN;
    }
    v = v * 36 + d;
  }
  return v;
}

/**
 * Encodes a number as a MegaNum of a fixed width (used by tools that generate .rip files).
 * @param {number} value A non-negative integer.
 * @param {number} [width=2] Number of digits.
 * @returns {string} The upper-case MegaNum.
 */
export function toMegaNum(value, width = 2) {
  return Math.max(0, Math.floor(value)).toString(36).toUpperCase().padStart(width, "0");
}

/**
 * Reads consecutive 2-digit MegaNums from an argument string.
 * @param {string} args The command arguments.
 * @param {number} count How many numbers to read.
 * @returns {number[]} The numbers.
 */
function nums(args, count) {
  const out = [];
  for (let i = 0; i < count; i++) {
    out.push(megaNum(args.slice(i * 2, i * 2 + 2)));
  }
  return out;
}

/**
 * Parses RIPscrip text into drawing operations.
 * @param {string} text The .rip file text.
 * @returns {object[]} Operations such as {op:"line",x0,y0,x1,y1,color}.
 */
export function parseRip(text) {
  const ops = [];
  const state = { color: 15, fill: 15, size: 1 };
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line.startsWith("!")) {
      continue;
    }
    for (const command of line.slice(1).split("|").filter((c) => c.length > 0)) {
      parseCommand(command, state, ops);
    }
  }
  return ops;
}

/**
 * Parses one command into the operation list.
 * @param {string} command The command text without the leading pipe.
 * @param {{color: number, fill: number, size: number}} state Current drawing state.
 * @param {object[]} ops The operation list to append to.
 * @returns {void}
 */
function parseCommand(command, state, ops) {
  const level1 = command[0] === "1";
  const name = level1 ? command.slice(0, 2) : command[0];
  const args = command.slice(name.length);
  switch (name) {
    case "c":
      state.color = megaNum(args.slice(0, 2)) & 15;
      break;
    case "S":
      state.fill = megaNum(args.slice(2, 4)) & 15;
      break;
    case "Y":
      state.size = Math.max(1, megaNum(args.slice(6, 8)) || 1);
      break;
    case "*":
      ops.push({ op: "reset" });
      break;
    case "L":
    case "R":
    case "B": {
      const [x0, y0, x1, y1] = nums(args, 4);
      const op = name === "L" ? "line" : name === "R" ? "rect" : "bar";
      ops.push({ op, x0, y0, x1, y1, color: name === "B" ? state.fill : state.color });
      break;
    }
    case "C": {
      const [x, y, r] = nums(args, 3);
      ops.push({ op: "circle", x, y, r, color: state.color });
      break;
    }
    case "o": {
      const [x, y, rx, ry] = nums(args, 4);
      ops.push({ op: "oval", x, y, rx, ry, color: state.color, fill: state.fill });
      break;
    }
    case "P":
    case "p": {
      const n = megaNum(args.slice(0, 2));
      const flat = nums(args.slice(2), n * 2);
      const points = [];
      for (let i = 0; i < flat.length; i += 2) {
        points.push([flat[i], flat[i + 1]]);
      }
      ops.push({ op: name === "p" ? "fillpoly" : "poly", points, color: state.color, fill: state.fill });
      break;
    }
    case "@": {
      const [x, y] = nums(args, 2);
      ops.push({ op: "text", x, y, text: args.slice(4), color: state.color, size: state.size });
      break;
    }
    case "1U": {
      const [x0, y0, x1, y1, hotkey] = nums(args, 5);
      const parts = args.slice(16).split("<>");
      const label = parts.length > 1 ? parts[1] : parts[0];
      ops.push({ op: "button", x0, y0, x1, y1, key: String.fromCharCode(hotkey), label });
      break;
    }
    default:
      // Unsupported commands are skipped so richer files still render partially.
      break;
  }
}

/**
 * Draws parsed operations on a 2D canvas context.
 * @param {CanvasRenderingContext2D} ctx The canvas context (640x350).
 * @param {object[]} ops Operations from parseRip().
 * @returns {{x0: number, y0: number, x1: number, y1: number, key: string, label: string}[]} The clickable buttons.
 */
export function drawRip(ctx, ops) {
  const buttons = [];
  ctx.fillStyle = hex(0);
  ctx.fillRect(0, 0, RIP_WIDTH, RIP_HEIGHT);
  for (const o of ops) {
    ctx.strokeStyle = hex(o.color ?? 15);
    ctx.fillStyle = hex(o.fill ?? o.color ?? 15);
    switch (o.op) {
      case "reset":
        ctx.fillStyle = hex(0);
        ctx.fillRect(0, 0, RIP_WIDTH, RIP_HEIGHT);
        break;
      case "line":
        ctx.beginPath();
        ctx.moveTo(o.x0 + 0.5, o.y0 + 0.5);
        ctx.lineTo(o.x1 + 0.5, o.y1 + 0.5);
        ctx.stroke();
        break;
      case "rect":
        ctx.strokeRect(o.x0 + 0.5, o.y0 + 0.5, o.x1 - o.x0, o.y1 - o.y0);
        break;
      case "bar":
        ctx.fillStyle = hex(o.color);
        ctx.fillRect(o.x0, o.y0, o.x1 - o.x0 + 1, o.y1 - o.y0 + 1);
        break;
      case "circle":
        ctx.beginPath();
        ctx.arc(o.x, o.y, o.r, 0, Math.PI * 2);
        ctx.stroke();
        break;
      case "oval":
        ctx.beginPath();
        ctx.ellipse(o.x, o.y, o.rx, o.ry, 0, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();
        break;
      case "poly":
      case "fillpoly":
        tracePolygon(ctx, o.points);
        if (o.op === "fillpoly") {
          ctx.fill();
        }
        ctx.stroke();
        break;
      case "text":
        ctx.fillStyle = hex(o.color);
        ctx.font = `${8 * o.size}px monospace`;
        ctx.textBaseline = "top";
        ctx.fillText(o.text, o.x, o.y);
        break;
      default:
        drawButton(ctx, o);
        buttons.push({ x0: o.x0, y0: o.y0, x1: o.x1, y1: o.y1, key: o.key, label: o.label });
        break;
    }
  }
  return buttons;
}

/**
 * Traces a closed polygon path.
 * @param {CanvasRenderingContext2D} ctx The canvas context.
 * @param {number[][]} points The vertices.
 * @returns {void}
 */
function tracePolygon(ctx, points) {
  ctx.beginPath();
  points.forEach(([x, y], i) => (i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y)));
  ctx.closePath();
}

/**
 * Draws a beveled RIP-style 3D button.
 * @param {CanvasRenderingContext2D} ctx The canvas context.
 * @param {{x0: number, y0: number, x1: number, y1: number, label: string}} b The button.
 * @returns {void}
 */
function drawButton(ctx, b) {
  ctx.fillStyle = hex(7);
  ctx.fillRect(b.x0, b.y0, b.x1 - b.x0, b.y1 - b.y0);
  ctx.fillStyle = hex(15);
  ctx.fillRect(b.x0, b.y0, b.x1 - b.x0, 2);
  ctx.fillRect(b.x0, b.y0, 2, b.y1 - b.y0);
  ctx.fillStyle = hex(8);
  ctx.fillRect(b.x0, b.y1 - 2, b.x1 - b.x0, 2);
  ctx.fillRect(b.x1 - 2, b.y0, 2, b.y1 - b.y0);
  ctx.fillStyle = hex(0);
  ctx.font = "8px monospace";
  ctx.textBaseline = "middle";
  ctx.textAlign = "center";
  ctx.fillText(b.label, (b.x0 + b.x1) / 2, (b.y0 + b.y1) / 2);
  ctx.textAlign = "start";
}

/**
 * Finds the button under a point given in canvas display coordinates.
 * @param {{x0: number, y0: number, x1: number, y1: number, key: string}[]} buttons Buttons from drawRip().
 * @param {number} x X in RIP coordinates (0-639).
 * @param {number} y Y in RIP coordinates (0-349).
 * @returns {string|null} The button's hotkey, or null.
 */
export function hitTest(buttons, x, y) {
  const b = buttons.find((btn) => x >= btn.x0 && x <= btn.x1 && y >= btn.y0 && y <= btn.y1);
  return b ? b.key : null;
}
