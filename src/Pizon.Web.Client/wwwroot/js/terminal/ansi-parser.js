/**
 * A streaming ANSI tokenizer. Text may arrive in arbitrary chunks (baud pacing splits it), so the
 * parser keeps state between calls to feed().
 * @module ansi-parser
 */

/** Parser states. */
const State = Object.freeze({ GROUND: 0, ESCAPE: 1, CSI: 2, OSC: 3, OSC_ESCAPE: 4 });

/** Control characters passed through as "ctl" tokens. */
const CONTROLS = new Set(["\r", "\n", "\b", "\t", "\x07"]);

/**
 * Tokenizes ANSI text into printable characters, control characters, CSI and OSC sequences.
 */
export class AnsiParser {
  /**
   * Creates a parser.
   * @param {(token: object) => void} onToken Receives each token:
   *   {t:"char",ch} | {t:"ctl",ch} | {t:"csi",params:number[],final:string,priv:string} | {t:"osc",data:string}.
   */
  constructor(onToken) {
    this.onToken = onToken;
    this.state = State.GROUND;
    this.buffer = "";
  }

  /**
   * Feeds a chunk of text.
   * @param {string} text The chunk.
   * @returns {void}
   */
  feed(text) {
    for (const ch of text) {
      this.step(ch);
    }
  }

  /**
   * Processes one character.
   * @param {string} ch The character.
   * @returns {void}
   */
  step(ch) {
    switch (this.state) {
      case State.GROUND:
        if (ch === "\x1b") {
          this.state = State.ESCAPE;
        } else if (CONTROLS.has(ch)) {
          this.onToken({ t: "ctl", ch });
        } else if (ch >= " " && ch !== "\x7f") {
          this.onToken({ t: "char", ch });
        }
        break;
      case State.ESCAPE:
        if (ch === "[") {
          this.state = State.CSI;
          this.buffer = "";
        } else if (ch === "]") {
          this.state = State.OSC;
          this.buffer = "";
        } else {
          this.state = State.GROUND;
          if (ch === "7" || ch === "8") {
            this.onToken({ t: "csi", params: [], final: ch === "7" ? "s" : "u", priv: "" });
          }
        }
        break;
      case State.CSI:
        if (ch >= "@" && ch <= "~") {
          this.emitCsi(ch);
          this.state = State.GROUND;
        } else {
          this.buffer += ch;
        }
        break;
      case State.OSC:
        if (ch === "\x07") {
          this.onToken({ t: "osc", data: this.buffer });
          this.state = State.GROUND;
        } else if (ch === "\x1b") {
          this.state = State.OSC_ESCAPE;
        } else {
          this.buffer += ch;
        }
        break;
      default:
        // OSC_ESCAPE: "ESC \" (string terminator) ends the OSC; anything else is kept literally.
        if (ch === "\\") {
          this.onToken({ t: "osc", data: this.buffer });
          this.state = State.GROUND;
        } else {
          this.buffer += "\x1b" + ch;
          this.state = State.OSC;
        }
        break;
    }
  }

  /**
   * Emits the CSI sequence collected so far.
   * @param {string} final The final byte.
   * @returns {void}
   */
  emitCsi(final) {
    let body = this.buffer;
    let priv = "";
    if (body.length > 0 && "?<=>".includes(body[0])) {
      priv = body[0];
      body = body.slice(1);
    }
    const params = body.length === 0 ? [] : body.split(";").map((p) => (p === "" ? 0 : Number.parseInt(p, 10) || 0));
    this.onToken({ t: "csi", params, final, priv });
  }
}
