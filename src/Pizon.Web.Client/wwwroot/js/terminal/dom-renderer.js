/**
 * Renders a CellBuffer into the page: one element per row, runs of identical attributes merged into
 * spans, hotspots as real buttons and URLs as links. Only text nodes are used, never innerHTML, so
 * visitor-written text can't inject markup.
 * @module dom-renderer
 */

/**
 * Splits a row of cells into runs of identical colors and link.
 * @param {object[]} cells The row's cells.
 * @returns {{text: string, fg: number, bg: number, link: string|null}[]} The runs.
 */
export function toRuns(cells) {
  const runs = [];
  for (const c of cells) {
    const last = runs[runs.length - 1];
    if (last && last.fg === c.fg && last.bg === c.bg && last.link === c.link) {
      last.text += c.ch;
    } else {
      runs.push({ text: c.ch, fg: c.fg, bg: c.bg, link: c.link });
    }
  }
  return runs;
}

/**
 * Draws a CellBuffer into a container element.
 */
export class DomRenderer {
  /**
   * Creates a renderer and its row elements.
   * @param {HTMLElement} root The terminal screen element.
   * @param {Document} doc The document used to create elements.
   * @param {number} rows Number of rows.
   */
  constructor(root, doc, rows) {
    this.root = root;
    this.doc = doc;
    this.rowElements = [];
    const nodes = [];
    for (let r = 0; r < rows; r++) {
      const el = doc.createElement("div");
      el.className = "term-row";
      this.rowElements.push(el);
      nodes.push(el);
    }
    root.replaceChildren(...nodes);
    this.cursorRow = -1;
  }

  /**
   * Redraws the dirty rows of a buffer (or every row when `all` is set).
   * @param {import("./cell-buffer.js").CellBuffer} buffer The buffer.
   * @param {{all?: boolean, cells?: object[], showCursor?: boolean}} [options] Overrides: `cells` draws
   *   a transition frame instead of the buffer's own cells.
   * @returns {void}
   */
  render(buffer, options = {}) {
    const cells = options.cells ?? buffer.cells;
    const rows = options.all || options.cells ? [...this.rowElements.keys()] : [...buffer.dirty];
    for (const r of rows) {
      this.renderRow(r, cells.slice(r * buffer.cols, (r + 1) * buffer.cols));
    }
    if (!options.cells) {
      buffer.dirty.clear();
    }
    this.placeCursor(buffer, options.showCursor !== false && !options.cells);
  }

  /**
   * Rebuilds one row element.
   * @param {number} r Row index.
   * @param {object[]} rowCells The row's cells.
   * @returns {void}
   */
  renderRow(r, rowCells) {
    const nodes = toRuns(rowCells).map((run) => this.runElement(run));
    this.rowElements[r].replaceChildren(...nodes);
  }

  /**
   * Creates the element for one run.
   * @param {{text: string, fg: number, bg: number, link: string|null}} run The run.
   * @returns {HTMLElement} A span, button (key hotspot) or anchor (URL).
   */
  runElement(run) {
    let el;
    if (run.link && run.link.startsWith("key:")) {
      el = this.doc.createElement("button");
      el.type = "button";
      el.className = "term-hot";
      el.dataset.key = run.link.slice(4);
    } else if (run.link) {
      el = this.doc.createElement("a");
      el.className = "term-link";
      el.href = run.link;
      el.target = "_blank";
      el.rel = "noopener noreferrer";
    } else {
      el = this.doc.createElement("span");
    }
    el.className = `${el.className} f${run.fg} b${run.bg}`.trim();
    el.textContent = run.text;
    return el;
  }

  /**
   * Shows the blinking block cursor at the buffer's cursor position.
   * @param {import("./cell-buffer.js").CellBuffer} buffer The buffer.
   * @param {boolean} visible Whether the cursor should be shown.
   * @returns {void}
   */
  placeCursor(buffer, visible) {
    for (const el of this.rowElements) {
      el.classList.remove("has-cursor");
    }
    if (!visible) {
      return;
    }
    const row = this.rowElements[buffer.cursor.row];
    row.classList.add("has-cursor");
    row.style.setProperty("--cursor-col", String(Math.min(buffer.cursor.col, buffer.cols - 1)));
  }
}
