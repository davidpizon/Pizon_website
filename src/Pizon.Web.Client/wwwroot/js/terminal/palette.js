/**
 * The PIZON BBS 256-color palette. Indices 0-15 follow the Armageddon BBS chart (VGA values),
 * 16-231 are the 6x6x6 cube and 232-255 the grayscale ramp. Mirrors Palette256.cs.
 * @module palette
 */

/** VGA-style values for the 16 system colors. */
const SYSTEM = [
  0x000000, 0x800000, 0x008000, 0x808000, 0x000080, 0x800080, 0x008080, 0xc0c0c0,
  0x808080, 0xff0000, 0x00ff00, 0xffff00, 0x0000ff, 0xff00ff, 0x00ffff, 0xffffff,
];

/** Channel levels used by the 6x6x6 color cube. */
const CUBE = [0x00, 0x5f, 0x87, 0xaf, 0xd7, 0xff];

/**
 * Builds the packed RGB table for all 256 indices.
 * @returns {number[]} 256 packed 0xRRGGBB values.
 */
export function buildPalette() {
  const table = SYSTEM.slice();
  for (const r of CUBE) {
    for (const g of CUBE) {
      for (const b of CUBE) {
        table.push((r << 16) | (g << 8) | b);
      }
    }
  }
  for (let i = 0; i < 24; i++) {
    const level = 8 + i * 10;
    table.push((level << 16) | (level << 8) | level);
  }
  return table;
}

/** Packed RGB values for every palette index. */
export const PALETTE = Object.freeze(buildPalette());

/**
 * Gets the CSS hex color for a palette index.
 * @param {number} index Palette index 0-255.
 * @returns {string} A color such as "#ff00ff".
 */
export function hex(index) {
  return "#" + PALETTE[index].toString(16).padStart(6, "0");
}

/**
 * Finds the nearest palette index to an RGB color (used to snap truecolor ANSI to the palette).
 * @param {number} r Red 0-255.
 * @param {number} g Green 0-255.
 * @param {number} b Blue 0-255.
 * @returns {number} The closest palette index.
 */
export function nearestIndex(r, g, b) {
  let best = 0;
  let bestDistance = Infinity;
  for (let i = 0; i < PALETTE.length; i++) {
    const c = PALETTE[i];
    const dr = ((c >> 16) & 0xff) - r;
    const dg = ((c >> 8) & 0xff) - g;
    const db = (c & 0xff) - b;
    const d = dr * dr + dg * dg + db * db;
    if (d < bestDistance) {
      bestDistance = d;
      best = i;
    }
  }
  return best;
}
