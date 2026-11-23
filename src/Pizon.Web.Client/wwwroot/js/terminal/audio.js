/**
 * Synthesized modem sounds (Web Audio). The dial-up handshake is rendered sample by sample into one
 * audio buffer, following the real call: dial tone, DTMF dialing, ringback, the answer modem's 2100 Hz
 * tone (ANSam: 15 Hz amplitude modulation, phase reversal every 450 ms), V.21 FSK negotiation on the
 * caller's (980/1180 Hz) and answerer's (1650/1850 Hz) channels, V.32 AA/AC tones, a line-probing tone
 * comb, then scrambled QAM training (the hiss), all band-limited like a phone line. The Page-SysOp beep
 * is an oscillator. Nothing is played until a user gesture unlocks the AudioContext, and only when the
 * caller has turned sound on.
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

/** Sample rate (Hz) of the rendered handshake: plenty for a 300–3400 Hz phone line. */
export const SAMPLE_RATE = 22050;

/** V.21 FSK: [mark (1), space (0)] frequencies for the calling and answering channels, and the bit rate. */
const FSK = Object.freeze({ call: [980, 1180], answer: [1650, 1850], baud: 300 });

/**
 * Lists the handshake's segments in order, each starting where the previous one ends.
 * @returns {{kind: string, at: number, dur: number, digit?: string, fade?: number}[]} The segments
 *   (times in seconds; fade is a longer fade-out than the default 5 ms).
 */
export function handshakePlan() {
  const plan = [];
  let t = 0;
  const add = (kind, dur, extra) => {
    plan.push({ kind, at: t, dur, ...extra });
    t += dur;
  };
  add("dialTone", 0.9);
  add("silence", 0.1);
  for (const digit of PHONE_NUMBER) {
    add("dtmf", 0.08, { digit });
    add("silence", 0.05);
  }
  add("silence", 0.35);
  add("ringback", 1.0);
  add("silence", 0.35);
  add("answerTone", 1.5);
  add("fskCall", 0.5);
  add("fskAnswer", 0.5);
  add("fskCall", 0.25);
  add("silence", 0.1);
  add("v32Tones", 0.6);
  add("probe", 0.45);
  add("training", 1.4, { fade: 0.25 });
  return plan;
}

/**
 * Makes a sine sample function.
 * @param {number[]} freqs Frequencies (Hz).
 * @param {number} amp Amplitude of each sine.
 * @param {number} rate Sample rate.
 * @returns {(i: number) => number} Sample i of the segment.
 */
function sines(freqs, amp, rate) {
  return (i) => freqs.reduce((sum, f) => sum + amp * Math.sin((2 * Math.PI * f * i) / rate), 0);
}

/**
 * Makes a V.21 FSK sample function: random bits after a run of mark (1) bits, phase-continuous.
 * @param {number[]} channel The [mark, space] frequencies.
 * @param {number} rate Sample rate.
 * @param {() => number} rng Random source for the bits.
 * @returns {(i: number) => number} Sample i of the segment.
 */
function fsk(channel, rate, rng) {
  const perBit = rate / FSK.baud;
  let phase = 0;
  let bit = 0;
  let value = 1;
  return (i) => {
    const index = Math.floor(i / perBit);
    if (index !== bit) {
      bit = index;
      value = bit < 10 || rng() < 0.5 ? 1 : 0;
    }
    phase += (2 * Math.PI * channel[value === 1 ? 0 : 1]) / rate;
    return 0.16 * Math.sin(phase);
  };
}

/**
 * Makes a QAM sample function: random 16-point symbols at 2400 baud on an 1800 Hz carrier, smoothed
 * between symbols. This is what V.32 training and data sound like: band-limited hiss.
 * @param {number} rate Sample rate.
 * @param {() => number} rng Random source for the symbols.
 * @returns {(i: number) => number} Sample i of the segment.
 */
function qam(rate, rng) {
  const perSymbol = rate / 2400;
  const level = () => [-3, -1, 1, 3][Math.floor(rng() * 4)];
  let from = [level(), level()];
  let to = [level(), level()];
  let symbol = 0;
  return (i) => {
    const index = Math.floor(i / perSymbol);
    while (symbol < index) {
      from = to;
      to = [level(), level()];
      symbol++;
    }
    const k = i / perSymbol - index;
    const re = from[0] + (to[0] - from[0]) * k;
    const im = from[1] + (to[1] - from[1]) * k;
    const w = (2 * Math.PI * 1800 * i) / rate;
    return 0.075 * (re * Math.cos(w) - im * Math.sin(w));
  };
}

/**
 * Makes the sample function for one segment.
 * @param {{kind: string, dur: number, digit?: string}} seg The segment.
 * @param {number} rate Sample rate.
 * @param {() => number} rng Random source.
 * @returns {(i: number) => number} Sample i of the segment.
 */
function voice(seg, rate, rng) {
  switch (seg.kind) {
    case "dialTone":
      return sines([350, 440], 0.12, rate);
    case "dtmf":
      return sines(DTMF[seg.digit], 0.12, rate);
    case "ringback":
      return sines([440, 480], 0.08, rate);
    case "answerTone":
      return (i) => {
        const t = i / rate;
        const flip = Math.floor(t / 0.45) % 2 === 1 ? Math.PI : 0;
        return 0.16 * (1 + 0.2 * Math.sin(2 * Math.PI * 15 * t)) * Math.sin(2 * Math.PI * 2100 * t + flip);
      };
    case "fskCall":
      return fsk(FSK.call, rate, rng);
    case "fskAnswer":
      return fsk(FSK.answer, rate, rng);
    case "v32Tones": {
      const aa = sines([1800], 0.16, rate);
      const ac = sines([600, 3000], 0.11, rate);
      const half = (seg.dur * rate) / 2;
      return (i) => (i < half ? aa(i) : ac(i));
    }
    case "probe": {
      const phases = Array.from({ length: 25 }, () => rng() * 2 * Math.PI);
      return (i) => phases.reduce((sum, p, n) => sum + 0.035 * Math.sin((2 * Math.PI * 150 * (n + 1) * i) / rate + p), 0);
    }
    case "training":
      return qam(rate, rng);
    default:
      return () => 0;
  }
}

/**
 * Renders the whole handshake: every segment with 5 ms fades (no clicks; longer fade-outs where the
 * plan asks), then a phone-line band
 * limit (one-pole high-pass near 300 Hz and low-pass near 3400 Hz) and a faint line hiss.
 * @param {() => number} rng Random source (bits, symbols, probe phases, hiss).
 * @param {number} [rate=SAMPLE_RATE] Sample rate.
 * @returns {Float32Array} The samples, each within [-1, 1].
 */
export function renderHandshake(rng, rate = SAMPLE_RATE) {
  const plan = handshakePlan();
  const last = plan[plan.length - 1];
  const out = new Float32Array(Math.round((last.at + last.dur) * rate));
  const ramp = 0.005 * rate;
  for (const seg of plan) {
    const start = Math.round(seg.at * rate);
    const count = Math.min(Math.round(seg.dur * rate), out.length - start);
    const sample = voice(seg, rate, rng);
    const fadeOut = (seg.fade ?? 0.005) * rate;
    for (let i = 0; i < count; i++) {
      out[start + i] = sample(i) * Math.min(1, i / ramp, (count - i) / fadeOut);
    }
  }
  const hp = Math.exp((-2 * Math.PI * 300) / rate);
  const lp = 1 - Math.exp((-2 * Math.PI * 3400) / rate);
  let prevIn = 0;
  let high = 0;
  let low = 0;
  for (let i = 0; i < out.length; i++) {
    const x = out[i] + 0.004 * (rng() - 0.5);
    high = hp * (high + x - prevIn);
    prevIn = x;
    low += lp * (high - low);
    out[i] = Math.max(-1, Math.min(1, low));
  }
  return out;
}

/**
 * Plays modem audio through an AudioContext.
 */
export class ModemAudio {
  /**
   * Creates the player. The AudioContext is created lazily on the first gesture.
   * @param {(new () => AudioContext)|undefined} AudioContextCtor The AudioContext constructor, if supported.
   * @param {() => number} rng Random source for the handshake.
   */
  constructor(AudioContextCtor, rng) {
    this.Ctor = AudioContextCtor;
    this.rng = rng;
    this.ctx = null;
    this.master = null;
    this.source = null;
    this.handshake = null;
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
   * Plays the full dial-up handshake (rendered once, then reused).
   * @returns {number} The handshake length in seconds (0 when audio is unavailable).
   */
  playHandshake() {
    if (!this.ensureMaster()) {
      return 0;
    }
    if (!this.handshake) {
      const samples = renderHandshake(this.rng);
      this.handshake = this.ctx.createBuffer(1, samples.length, SAMPLE_RATE);
      this.handshake.getChannelData(0).set(samples);
    }
    const source = this.ctx.createBufferSource();
    source.buffer = this.handshake;
    source.connect(this.master);
    source.start(this.ctx.currentTime);
    this.source = source;
    return this.handshake.duration;
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
    const end = now + fadeMs / 1000;
    this.master.gain.setValueAtTime(this.master.gain.value, now);
    this.master.gain.linearRampToValueAtTime(0, end);
    this.master = null;
    this.source?.stop(end);
    this.source = null;
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
