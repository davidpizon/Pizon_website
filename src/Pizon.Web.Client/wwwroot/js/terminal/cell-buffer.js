/**
 * The 80x25 character-cell screen: applies ANSI tokens (from AnsiParser) to a grid of cells.
 * Each cell holds a character, foreground and background palette indices and an optional link
 * ("key:X" hotspot or an http(s) URL).
 * @module cell-buffer
 */

import { nearestIndex } from "./palette.js";

/** Default screen width in columns. */
export const COLS = 80;

/** Default screen height in rows. */
export const ROWS = 25;

/**
 * Creates a blank cell.
 * @param {number} [bg=0] Background palette index.
 * @returns {{ch: string, fg: number, bg: number, link: string|null}} The cell.
 */
export function blankCell(bg = 0) {
  return { ch: " ", fg: 7, bg, link: null };
}

/**
 * Compares two cells for equal appearance.
 * @param {object} a First cell.
 * @param {object} b Second cell.
 * @returns {boolean} True when they look identical.
 */
export function sameCell(a, b) {
  return a.ch === b.ch && a.fg === b.fg && a.bg === b.bg && a.link === b.link;
}

/**
 * A grid of cells with a cursor and current drawing attributes.
 */
export class CellBuffer {
  /**
   * Creates a cleared buffer.
   * @param {number} [cols=COLS] Width in columns.
   * @param {number} [rows=ROWS] Height in rows.
   */
  constructor(cols = COLS, rows = ROWS) {
    this.cols = cols;
    this.rows = rows;
    this.cells = Array.from({ length: cols * rows }, () => blankCell());
    this.cursor = { row: 0, col: 0 };
    this.saved = { row: 0, col: 0 };
    this.attr = { fg: 7, bg: 0, bold: false, blink: false, reverse: false, link: null };
    this.dirty = new Set();
    this.markAll();
  }

  /**
   * Copies cells, cursor and attributes into a new buffer.
   * @returns {CellBuffer} The copy.
   */
  clone() {
    const copy = new CellBuffer(this.cols, this.rows);
    copy.cells = this.cells.map((c) => ({ ...c }));
    copy.cursor = { ...this.cursor };
    copy.saved = { ...this.saved };
    copy.attr = { ...this.attr };
    return copy;
  }

  /**
   * Replaces all cells (used by transitions) and marks everything dirty.
   * @param {object[]} cells A full set of cells.
   * @returns {void}
   */
  setCells(cells) {
    this.cells = cells.map((c) => ({ ...c }));
    this.markAll();
  }

  /**
   * Marks every row as needing a redraw.
   * @returns {void}
   */
  markAll() {
    for (let r = 0; r < this.rows; r++) {
      this.dirty.add(r);
    }
  }

  /**
   * Gets the cell at a position.
   * @param {number} row 0-based row.
   * @param {number} col 0-based column.
   * @returns {object} The cell.
   */
  cellAt(row, col) {
    return this.cells[row * this.cols + col];
  }

  /**
   * Gets one row of cells.
   * @param {number} row 0-based row.
   * @returns {object[]} The row's cells.
   */
  row(row) {
    return this.cells.slice(row * this.cols, (row + 1) * this.cols);
  }

  /**
   * Gets the plain text of a row (handy for tests and screen readers).
   * @param {number} row 0-based row.
   * @returns {string} The row text without trailing spaces.
   */
  rowText(row) {
    return this.row(row).map((c) => c.ch).join("").trimEnd();
  }

  /**
   * Applies one parser token.
   * @param {object} token A token from AnsiParser.
   * @returns {void}
   */
  apply(token) {
    switch (token.t) {
      case "char":
        this.put(token.ch);
        break;
      case "ctl":
        this.control(token.ch);
        break;
      case "csi":
        this.csi(token);
        break;
      default:
        this.osc(token.data);
        break;
    }
  }

  /**
   * Writes a printable character at the cursor and advances it.
   * @param {string} ch The character.
   * @returns {void}
   */
  put(ch) {
    if (this.cursor.col >= this.cols) {
      this.cursor.col = 0;
      this.lineFeed();
    }
    const { fg, bg } = this.effectiveColors();
    this.cells[this.cursor.row * this.cols + this.cursor.col] = { ch, fg, bg, link: this.attr.link };
    this.dirty.add(this.cursor.row);
    this.cursor.col++;
  }

  /**
   * Computes the colors to draw with, applying bold-brightens, iCE blink-brightens and reverse.
   * @returns {{fg: number, bg: number}} The palette indices.
   */
  effectiveColors() {
    let fg = this.attr.fg;
    let bg = this.attr.bg;
    if (this.attr.bold && fg < 8) {
      fg += 8;
    }
    if (this.attr.blink && bg < 8) {
      bg += 8;
    }
    return this.attr.reverse ? { fg: bg, bg: fg } : { fg, bg };
  }

  /**
   * Handles a control character.
   * @param {string} ch The control character.
   * @returns {void}
   */
  control(ch) {
    switch (ch) {
      case "\r":
        this.cursor.col = 0;
        break;
      case "\n":
        this.lineFeed();
        break;
      case "\b":
        this.cursor.col = Math.max(0, this.cursor.col - 1);
        break;
      case "\t":
        this.cursor.col = Math.min(this.cols - 1, (Math.floor(this.cursor.col / 8) + 1) * 8);
        break;
      default:
        // BEL is handled by the terminal (beep); nothing to draw.
        break;
    }
  }

  /**
   * Moves the cursor down one row, scrolling at the bottom.
   * @returns {void}
   */
  lineFeed() {
    if (this.cursor.row < this.rows - 1) {
      this.cursor.row++;
      return;
    }
    this.cells.splice(0, this.cols);
    for (let i = 0; i < this.cols; i++) {
      this.cells.push(blankCell(this.attr.bg));
    }
    this.markAll();
  }

  /**
   * Handles a CSI sequence.
   * @param {{params: number[], final: string, priv: string}} token The sequence.
   * @returns {void}
   */
  csi(token) {
    const p = token.params;
    const n = Math.max(1, p[0] || 1);
    switch (token.final) {
      case "A":
        this.cursor.row = Math.max(0, this.cursor.row - n);
        break;
      case "B":
        this.cursor.row = Math.min(this.rows - 1, this.cursor.row + n);
        break;
      case "C":
        this.cursor.col = Math.min(this.cols - 1, this.cursor.col + n);
        break;
      case "D":
        this.cursor.col = Math.max(0, this.cursor.col - n);
        break;
      case "H":
      case "f":
        this.cursor.row = clamp((p[0] || 1) - 1, 0, this.rows - 1);
        this.cursor.col = clamp((p[1] || 1) - 1, 0, this.cols - 1);
        break;
      case "J":
        this.eraseDisplay(p[0] || 0);
        break;
      case "K":
        this.eraseLine(p[0] || 0);
        break;
      case "m":
        this.sgr(p.length === 0 ? [0] : p);
        break;
      case "s":
        this.saved = { ...this.cursor };
        break;
      case "u":
        this.cursor = { ...this.saved };
        break;
      default:
        // Unsupported sequences (cursor visibility, modes) are ignored.
        break;
    }
  }

  /**
   * Erases part or all of the screen (ED).
   * @param {number} mode 0 = cursor to end, 1 = start to cursor, 2 = whole screen.
   * @returns {void}
   */
  eraseDisplay(mode) {
    const at = this.cursor.row * this.cols + this.cursor.col;
    const [from, to] = mode === 0 ? [at, this.cells.length] : mode === 1 ? [0, at + 1] : [0, this.cells.length];
    for (let i = from; i < to; i++) {
      this.cells[i] = blankCell(this.attr.bg);
    }
    if (mode === 2) {
      this.cursor = { row: 0, col: 0 };
    }
    this.markAll();
  }

  /**
   * Erases part or all of the current line (EL).
   * @param {number} mode 0 = cursor to end, 1 = start to cursor, 2 = whole line.
   * @returns {void}
   */
  eraseLine(mode) {
    const start = this.cursor.row * this.cols;
    const at = start + Math.min(this.cursor.col, this.cols - 1);
    const [from, to] = mode === 0 ? [at, start + this.cols] : mode === 1 ? [start, at + 1] : [start, start + this.cols];
    for (let i = from; i < to; i++) {
      this.cells[i] = blankCell(this.attr.bg);
    }
    this.dirty.add(this.cursor.row);
  }

  /**
   * Applies Select Graphic Rendition parameters.
   * @param {number[]} params The SGR parameters.
   * @returns {void}
   */
  sgr(params) {
    for (let i = 0; i < params.length; i++) {
      const v = params[i];
      if (v === 0) {
        Object.assign(this.attr, { fg: 7, bg: 0, bold: false, blink: false, reverse: false });
      } else if (v === 1) {
        this.attr.bold = true;
      } else if (v === 22) {
        this.attr.bold = false;
      } else if (v === 5) {
        this.attr.blink = true;
      } else if (v === 25) {
        this.attr.blink = false;
      } else if (v === 7) {
        this.attr.reverse = true;
      } else if (v === 27) {
        this.attr.reverse = false;
      } else if (v >= 30 && v <= 37) {
        this.attr.fg = v - 30;
      } else if (v === 39) {
        this.attr.fg = 7;
      } else if (v >= 40 && v <= 47) {
        this.attr.bg = v - 40;
      } else if (v === 49) {
        this.attr.bg = 0;
      } else if (v >= 90 && v <= 97) {
        this.attr.fg = v - 90 + 8;
      } else if (v >= 100 && v <= 107) {
        this.attr.bg = v - 100 + 8;
      } else if ((v === 38 || v === 48) && params[i + 1] === 5) {
        this.attr[v === 38 ? "fg" : "bg"] = clamp(params[i + 2] ?? 0, 0, 255);
        i += 2;
      } else if ((v === 38 || v === 48) && params[i + 1] === 2) {
        this.attr[v === 38 ? "fg" : "bg"] = nearestIndex(params[i + 2] ?? 0, params[i + 3] ?? 0, params[i + 4] ?? 0);
        i += 4;
      }
    }
  }

  /**
   * Handles an OSC string; only OSC 8 (hyperlinks/hotspots) is supported.
   * @param {string} data The OSC payload, e.g. "8;;key:1".
   * @returns {void}
   */
  osc(data) {
    if (!data.startsWith("8;")) {
      return;
    }
    const target = data.slice(data.indexOf(";", 2) + 1);
    this.attr.link = isAllowedLink(target) ? target : null;
  }
}

/**
 * Checks that a link target is a key hotspot or an http(s) URL (never javascript: or data:).
 * @param {string} target The OSC 8 target.
 * @returns {boolean} True when the target is safe to render.
 */
export function isAllowedLink(target) {
  return /^key:.{1,16}$/.test(target) || /^https?:\/\/[^\s"'<>]+$/i.test(target);
}

/**
 * Clamps a number into a range.
 * @param {number} v The value.
 * @param {number} lo Lower bound.
 * @param {number} hi Upper bound.
 * @returns {number} The clamped value.
 */
export function clamp(v, lo, hi) {
  return Math.min(hi, Math.max(lo, v));
}
