/**
 * Shared test support: a tiny fake DOM (no npm packages), a recording canvas context, a fake Web Audio
 * context, and a guard that fails any test that logs a warning or error.
 * @module support
 */

/** Paths to the code under test. */
export const JS = new URL("../../Pizon.Web.Client/wwwroot/js/terminal/", import.meta.url);

/** The tools folder. */
export const TOOLS = new URL("../../tools/", import.meta.url);

for (const level of ["warn", "error"]) {
  console[level] = (...args) => {
    throw new Error(`console.${level} called during a test: ${args.join(" ")}`);
  };
}

/**
 * A minimal DOM element: children, attributes the terminal uses, class list, style and events.
 */
export class FakeElement {
  /**
   * Creates an element.
   * @param {string} tag The tag name.
   * @param {FakeDocument} doc The owner document.
   */
  constructor(tag, doc) {
    this.tagName = tag.toUpperCase();
    this.ownerDocument = doc;
    this.children = [];
    this.parent = null;
    this.className = "";
    this.textContent = "";
    this.dataset = {};
    this.hidden = false;
    this.value = "";
    this.listeners = {};
    this.focused = false;
    this.rect = { left: 0, top: 0, width: 640, height: 350 };
    const classes = new Set();
    this.classList = {
      add: (c) => classes.add(c),
      remove: (c) => classes.delete(c),
      contains: (c) => classes.has(c),
    };
    const props = {};
    this.style = { setProperty: (k, v) => (props[k] = v), getPropertyValue: (k) => props[k] };
    this.context = null;
  }

  /**
   * Replaces the children.
   * @param {...FakeElement} nodes The new children.
   * @returns {void}
   */
  replaceChildren(...nodes) {
    this.children = nodes;
    nodes.forEach((n) => (n.parent = this));
  }

  /**
   * Appends a child.
   * @param {FakeElement} node The child.
   * @returns {FakeElement} The child.
   */
  appendChild(node) {
    this.children.push(node);
    node.parent = this;
    return node;
  }

  /**
   * Removes this element from its parent.
   * @returns {void}
   */
  remove() {
    if (this.parent) {
      this.parent.children = this.parent.children.filter((c) => c !== this);
    }
    this.removed = true;
  }

  /**
   * Finds the nearest element (this or an ancestor) with a data-key.
   * @param {string} selector Only "[data-key]" is supported.
   * @returns {FakeElement|null} The element.
   */
  closest(selector) {
    if (selector !== "[data-key]") {
      throw new Error("unsupported selector " + selector);
    }
    for (let el = this; el; el = el.parent) {
      if (el.dataset.key !== undefined) {
        return el;
      }
    }
    return null;
  }

  /**
   * Adds an event listener.
   * @param {string} type The event type.
   * @param {Function} fn The listener.
   * @returns {void}
   */
  addEventListener(type, fn) {
    (this.listeners[type] ??= []).push(fn);
  }

  /**
   * Removes an event listener.
   * @param {string} type The event type.
   * @param {Function} fn The listener.
   * @returns {void}
   */
  removeEventListener(type, fn) {
    this.listeners[type] = (this.listeners[type] ?? []).filter((f) => f !== fn);
  }

  /**
   * Dispatches an event to the listeners.
   * @param {string} type The event type.
   * @param {object} event The event fields.
   * @returns {object} The event (with defaultPrevented set if a listener prevented it).
   */
  dispatch(type, event = {}) {
    const e = { target: this, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, ...event };
    for (const fn of this.listeners[type] ?? []) {
      fn(e);
    }
    return e;
  }

  /**
   * Counts listeners of a type.
   * @param {string} type The event type.
   * @returns {number} The count.
   */
  listenerCount(type) {
    return (this.listeners[type] ?? []).length;
  }

  /**
   * Focuses the element.
   * @returns {void}
   */
  focus() {
    this.focused = true;
  }

  /**
   * Blurs the element.
   * @returns {void}
   */
  blur() {
    this.focused = false;
  }

  /**
   * Gets the element's box.
   * @returns {object} The box.
   */
  getBoundingClientRect() {
    return this.rect;
  }

  /**
   * Gets a drawing context (canvas elements only).
   * @param {string} kind Must be "2d".
   * @returns {object|null} The context.
   */
  getContext(kind) {
    if (this.tagName !== "CANVAS" || kind !== "2d") {
      return null;
    }
    this.context ??= recordingContext();
    return this.context;
  }

  /**
   * Gets the visible text of this element and its children.
   * @returns {string} The text.
   */
  text() {
    return this.textContent + this.children.map((c) => c.text()).join("");
  }
}

/**
 * A minimal document with a head, element lookup by id and a window.
 */
export class FakeDocument extends FakeElement {
  /**
   * Creates a document.
   * @param {object} win The window (defaultView).
   */
  constructor(win = {}) {
    super("#document", null);
    this.ownerDocument = this;
    this.head = new FakeElement("head", this);
    this.body = new FakeElement("body", this);
    this.defaultView = win;
    this.ids = {};
  }

  /**
   * Creates an element.
   * @param {string} tag The tag.
   * @returns {FakeElement} The element.
   */
  createElement(tag) {
    return new FakeElement(tag, this);
  }

  /**
   * Finds an element registered with an id.
   * @param {string} id The id.
   * @returns {FakeElement|null} The element.
   */
  getElementById(id) {
    return this.ids[id] ?? null;
  }
}

/**
 * Creates a canvas context that records every call and property assignment.
 * @returns {object} The context; its "calls" property lists [name, ...args].
 */
export function recordingContext() {
  const calls = [];
  const state = {};
  return new Proxy(state, {
    get(target, key) {
      if (key === "calls") {
        return calls;
      }
      if (key in target) {
        return target[key];
      }
      return (...args) => calls.push([key, ...args]);
    },
    set(target, key, value) {
      target[key] = value;
      calls.push(["set", key, value]);
      return true;
    },
  });
}

/**
 * A fake Web Audio context that records the nodes it creates.
 */
export class FakeAudioContext {
  /** Creates a suspended context. */
  constructor() {
    this.state = "suspended";
    this.currentTime = 10;
    this.destination = { name: "destination" };
    this.nodes = [];
    this.resumed = 0;
  }

  /**
   * Resumes the context.
   * @returns {void}
   */
  resume() {
    this.resumed++;
    this.state = "running";
  }

  /**
   * Creates a gain node.
   * @returns {object} The node.
   */
  createGain() {
    return this.node({ gain: { value: 0, setValueAtTime: recorder("set"), linearRampToValueAtTime: recorder("ramp") } });
  }

  /**
   * Creates an oscillator node.
   * @returns {object} The node.
   */
  createOscillator() {
    return this.node({ type: "", frequency: { value: 0 }, start: recorder("start"), stop: recorder("stop") });
  }

  /**
   * Records and returns a node with a connect method.
   * @param {object} node The node fields.
   * @returns {object} The node.
   */
  node(node) {
    node.connect = recorder("connect");
    this.nodes.push(node);
    return node;
  }
}

/**
 * Creates a function that records its calls.
 * @param {string} name The name.
 * @returns {Function} The recorder; its "calls" property lists the argument arrays.
 */
export function recorder(name) {
  const fn = (...args) => fn.calls.push(args);
  fn.calls = [];
  fn.label = name;
  return fn;
}

/**
 * A manual requestAnimationFrame: callbacks run only when the test pumps them.
 */
export class FrameClock {
  /** Creates a clock at time 0. */
  constructor() {
    this.time = 0;
    this.pending = [];
  }

  /**
   * Schedules a callback (the raf function given to the terminal).
   * @param {Function} cb The callback.
   * @returns {number} A handle.
   */
  raf(cb) {
    this.pending.push(cb);
    return this.pending.length;
  }

  /**
   * Runs frames until nothing is scheduled (or the limit is reached).
   * @param {number} step Milliseconds per frame.
   * @param {number} limit The most frames to run.
   * @returns {number} Frames run.
   */
  pump(step = 16, limit = 10000) {
    let frames = 0;
    while (this.pending.length > 0 && frames < limit) {
      const callbacks = this.pending;
      this.pending = [];
      this.time += step;
      callbacks.forEach((cb) => cb(this.time));
      frames++;
    }
    return frames;
  }
}

/**
 * Waits for queued promise callbacks to run.
 * @returns {Promise<void>} Resolves on the next macrotask.
 */
export function settle() {
  return new Promise((resolve) => setImmediate(resolve));
}
