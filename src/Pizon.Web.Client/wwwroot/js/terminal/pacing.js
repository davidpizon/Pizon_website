/**
 * Baud-rate pacing: how many characters may be drawn in an animation frame.
 * @module pacing
 */

/** The supported speeds; 0 means "instant". */
export const BAUD_RATES = Object.freeze([0, 2400, 9600]);

/**
 * Converts a baud rate into characters per second (8N1: ten bits per character).
 * @param {number} baud The baud rate; 0 for instant.
 * @returns {number} Characters per second (Infinity for instant).
 */
export function charsPerSecond(baud) {
  return baud > 0 ? baud / 10 : Infinity;
}

/**
 * Hands out a character budget per frame, carrying fractions between frames.
 */
export class Pacer {
  /**
   * Creates a pacer.
   * @param {number} baud The baud rate; 0 for instant.
   */
  constructor(baud) {
    this.setBaud(baud);
  }

  /**
   * Changes the speed and drops any carried budget.
   * @param {number} baud The baud rate; 0 for instant.
   * @returns {void}
   */
  setBaud(baud) {
    this.baud = baud;
    this.carry = 0;
  }

  /**
   * Gets whether the pacer draws everything immediately.
   * @returns {boolean} True for instant mode.
   */
  get instant() {
    return !(this.baud > 0);
  }

  /**
   * Takes the number of characters allowed for the elapsed time.
   * @param {number} elapsedMs Milliseconds since the previous frame.
   * @returns {number} Whole characters that may be drawn now (Infinity in instant mode).
   */
  take(elapsedMs) {
    if (this.instant) {
      return Infinity;
    }
    const budget = this.carry + (charsPerSecond(this.baud) * Math.max(0, elapsedMs)) / 1000;
    const whole = Math.floor(budget);
    this.carry = budget - whole;
    return whole;
  }
}
