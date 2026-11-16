/**
 * Synthesized modem sounds (Web Audio): dial tone, DTMF dialing, answer tone, handshake warble and
 * the Page-SysOp beep. Nothing is played until a user gesture unlocks the AudioContext, and only when
 * the caller has turned sound on.
 * @module audio
 */

/** DTMF frequency pairs. */
export const DTMF = Object.freeze({
  1: [697, 1209], 2: [697, 1336], 3: [697, 1477],
  4: [770, 1209], 5: [770, 1336], 6: [770, 1477],
  7: [852, 1209], 8: [852, 1336], 9: [852, 1477],
  0: [941, 1336],
});

/** The number the BBS "dials". */
export const PHONE_NUMBER = "5550199";

/**
 * Builds the handshake schedule: a list of tones with start times (seconds) and durations.
 * @param {() => number} rng Random source for the warble.
 * @returns {{at: number, dur: number, freqs: number[], gain: number, type?: string}[]} The schedule.
 */
export function handshakeSchedule(rng) {
  const events = [{ at: 0, dur: 0.9, freqs: [350, 440], gain: 0.12 }];
  let t = 1.0;
  for (const digit of PHONE_NUMBER) {
    events.push({ at: t, dur: 0.08, freqs: DTMF[digit], gain: 0.12 });
    t += 0.13;
  }
  t += 0.4;
  events.push({ at: t, dur: 0.9, freqs: [2100], gain: 0.08 });
  t += 1.0;
  for (let i = 0; i < 24; i++) {
    const base = 600 + Math.floor(rng() * 2600);
    events.push({ at: t, dur: 0.07, freqs: [base, base * 1.5], gain: 0.05, type: rng() > 0.5 ? "square" : "sawtooth" });
    t += 0.07;
  }
  events.push({ at: t, dur: 0.6, freqs: [1200, 2400], gain: 0.04, type: "square" });
  return events;
}

/**
 * Plays synthesized modem audio through an AudioContext.
 */
export class ModemAudio {
  /**
   * Creates the player. The AudioContext is created lazily on the first gesture.
   * @param {(new () => AudioContext)|undefined} AudioContextCtor The AudioContext constructor, if supported.
   * @param {() => number} rng Random source.
   */
  constructor(AudioContextCtor, rng) {
    this.Ctor = AudioContextCtor;
    this.rng = rng;
    this.ctx = null;
    this.master = null;
  }

  /**
   * Creates or resumes the AudioContext; call from a user gesture.
   * @returns {void}
   */
  unlock() {
    if (!this.Ctor) {
      return;
    }
    if (!this.ctx) {
      this.ctx = new this.Ctor();
    }
    if (this.ctx.state === "suspended") {
      this.ctx.resume();
    }
  }

  /**
   * Plays the full dial-up handshake.
   * @returns {number} The handshake length in seconds (0 when audio is unavailable).
   */
  playHandshake() {
    if (!this.ensureMaster()) {
      return 0;
    }
    const events = handshakeSchedule(this.rng);
    for (const e of events) {
      this.tone(e);
    }
    const last = events[events.length - 1];
    return last.at + last.dur;
  }

  /**
   * Plays a short terminal beep.
   * @returns {void}
   */
  beep() {
    if (this.ensureMaster()) {
      this.tone({ at: 0, dur: 0.15, freqs: [880], gain: 0.1, type: "square" });
    }
  }

  /**
   * Fades out and stops everything currently playing.
   * @param {number} [fadeMs=150] Fade length.
   * @returns {void}
   */
  stop(fadeMs = 150) {
    if (!this.ctx || !this.master) {
      return;
    }
    const now = this.ctx.currentTime;
    this.master.gain.setValueAtTime(this.master.gain.value, now);
    this.master.gain.linearRampToValueAtTime(0, now + fadeMs / 1000);
    this.master = null;
  }

  /**
   * Creates a fresh master gain node when the context is ready.
   * @returns {boolean} True when audio can play.
   */
  ensureMaster() {
    if (!this.ctx) {
      return false;
    }
    if (!this.master) {
      this.master = this.ctx.createGain();
      this.master.gain.value = 1;
      this.master.connect(this.ctx.destination);
    }
    return true;
  }

  /**
   * Schedules one tone (one oscillator per frequency).
   * @param {{at: number, dur: number, freqs: number[], gain: number, type?: string}} e The tone.
   * @returns {void}
   */
  tone(e) {
    const start = this.ctx.currentTime + e.at;
    for (const f of e.freqs) {
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();
      osc.type = e.type ?? "sine";
      osc.frequency.value = f;
      gain.gain.value = e.gain;
      osc.connect(gain);
      gain.connect(this.master);
      osc.start(start);
      osc.stop(start + e.dur);
    }
  }
}
