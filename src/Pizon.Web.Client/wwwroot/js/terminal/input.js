/**
 * Keyboard, mouse and touch input for the terminal: maps browser events to BBS keys, turns hotspot
 * clicks into keys, detects "move the mouse to skip" during the dial-up handshake and opens the
 * on-screen keyboard on phones when a line is being typed.
 * @module input
 */

/** Named keys forwarded to .NET (everything else must be a single printable character). */
const NAMED_KEYS = Object.freeze(new Set([
  "Enter", "Escape", "Backspace", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Tab", "Home", "End", "PageUp", "PageDown",
]));

/** Mouse travel in pixels needed before a move counts as "skip". */
export const SKIP_DISTANCE = 10;

/**
 * Maps a keyboard event to a BBS key name.
 * @param {{key: string, ctrlKey?: boolean, metaKey?: boolean, altKey?: boolean}} event The keyboard event.
 * @returns {string|null} The key, or null when the browser should handle it (shortcuts, F-keys…).
 */
export function mapKey(event) {
  if (event.ctrlKey || event.metaKey || event.altKey) {
    return null;
  }
  if (NAMED_KEYS.has(event.key)) {
    return event.key;
  }
  return [...event.key].length === 1 ? event.key : null;
}

/**
 * Wires DOM events to a sink.
 */
export class InputController {
  /**
   * Starts listening.
   * @param {EventTarget} target Where key and pointer events are heard (usually the document).
   * @param {HTMLElement} screen The terminal element (hotspot clicks are delegated from it).
   * @param {HTMLInputElement|null} mobileInput A visually hidden input focused for on-screen keyboards.
   * @param {{onKey: (key: string) => void, onSkip: () => void, onGesture: () => void}} sink Callbacks.
   */
  constructor(target, screen, mobileInput, sink) {
    this.target = target;
    this.screen = screen;
    this.mobileInput = mobileInput;
    this.sink = sink;
    this.skipArmed = false;
    this.travel = 0;
    this.last = null;
    this.lineMode = false;
    this.handlers = {
      keydown: (e) => this.onKeyDown(e),
      mousemove: (e) => this.onMouseMove(e),
      pointerdown: (e) => this.onPointerDown(e),
      click: (e) => this.onClick(e),
      input: (e) => this.onMobileInput(e),
    };
    target.addEventListener("keydown", this.handlers.keydown);
    target.addEventListener("mousemove", this.handlers.mousemove);
    target.addEventListener("pointerdown", this.handlers.pointerdown);
    screen.addEventListener("click", this.handlers.click);
    mobileInput?.addEventListener("input", this.handlers.input);
  }

  /**
   * Stops listening.
   * @returns {void}
   */
  dispose() {
    this.target.removeEventListener("keydown", this.handlers.keydown);
    this.target.removeEventListener("mousemove", this.handlers.mousemove);
    this.target.removeEventListener("pointerdown", this.handlers.pointerdown);
    this.screen.removeEventListener("click", this.handlers.click);
    this.mobileInput?.removeEventListener("input", this.handlers.input);
  }

  /**
   * Arms or disarms "move the mouse / tap / press a key to skip".
   * @param {boolean} armed Whether skipping is allowed now.
   * @returns {void}
   */
  armSkip(armed) {
    this.skipArmed = armed;
    this.travel = 0;
    this.last = null;
  }

  /**
   * Turns line mode on or off; in line mode the hidden input is focused so phones show a keyboard.
   * @param {boolean} on Whether a line is being typed.
   * @returns {void}
   */
  setLineMode(on) {
    this.lineMode = on;
    if (!this.mobileInput) {
      return;
    }
    if (on) {
      this.mobileInput.value = "";
      this.mobileInput.focus({ preventScroll: true });
    } else {
      this.mobileInput.blur();
    }
  }

  /**
   * Fires the skip callback once and disarms.
   * @returns {void}
   */
  skip() {
    if (this.skipArmed) {
      this.skipArmed = false;
      this.sink.onSkip();
    }
  }

  /**
   * Handles keydown.
   * @param {KeyboardEvent} e The event.
   * @returns {void}
   */
  onKeyDown(e) {
    this.sink.onGesture();
    if (e.target === this.mobileInput && [...e.key].length === 1) {
      return; // Printable characters from the on-screen keyboard arrive through the input event.
    }
    const key = mapKey(e);
    if (key === null) {
      return;
    }
    e.preventDefault();
    if (this.skipArmed) {
      this.skip();
      return;
    }
    this.sink.onKey(key);
  }

  /**
   * Handles mouse movement for skip detection.
   * @param {MouseEvent} e The event.
   * @returns {void}
   */
  onMouseMove(e) {
    if (!this.skipArmed) {
      return;
    }
    if (this.last) {
      this.travel += Math.abs(e.clientX - this.last.x) + Math.abs(e.clientY - this.last.y);
    }
    this.last = { x: e.clientX, y: e.clientY };
    if (this.travel > SKIP_DISTANCE) {
      this.skip();
    }
  }

  /**
   * Handles pointer down: unlocks audio, and a touch tap skips the handshake.
   * @param {PointerEvent} e The event.
   * @returns {void}
   */
  onPointerDown(e) {
    this.sink.onGesture();
    if (e.pointerType === "touch") {
      this.skip();
    }
  }

  /**
   * Turns a click on a hotspot button into its key.
   * @param {MouseEvent} e The event.
   * @returns {void}
   */
  onClick(e) {
    const hot = e.target?.closest?.("[data-key]");
    if (!hot) {
      if (this.lineMode) {
        this.setLineMode(true);
      }
      return;
    }
    e.preventDefault();
    if (this.skipArmed) {
      this.skip();
      return;
    }
    this.sink.onKey(hot.dataset.key);
  }

  /**
   * Forwards characters typed on a phone keyboard.
   * @param {InputEvent} e The event.
   * @returns {void}
   */
  onMobileInput(e) {
    const value = e.target.value ?? "";
    e.target.value = "";
    for (const ch of value) {
      this.sink.onKey(ch);
    }
  }
}
