/**
 * Decoding for ANSI art files: CP437 (classic .ans) or UTF-8, with the SAUCE record removed.
 * @module cp437
 */

/** Unicode characters for CP437 bytes 0x80-0xFF. */
const HIGH =
  "ÇüéâäàåçêëèïîìÄÅÉæÆôöòûùÿÖÜ¢£¥₧ƒáíóúñÑªº¿⌐¬½¼¡«»" +
  "░▒▓│┤╡╢╖╕╣║╗╝╜╛┐└┴┬├─┼╞╟╚╔╩╦╠═╬╧╨╤╥╙╘╒╓╫╪┘┌█▄▌▐▀" +
  "αßΓπΣσµτΦΘΩδ∞φε∩≡±≥≤⌠⌡÷≈°∙·√ⁿ²■ ";

/**
 * Decodes CP437 bytes. Bytes below 0x80 stay ASCII so escape sequences and line breaks survive.
 * @param {Uint8Array} bytes The raw bytes.
 * @returns {string} The decoded text.
 */
export function decodeCp437(bytes) {
  let out = "";
  for (const b of bytes) {
    out += b < 0x80 ? String.fromCharCode(b) : HIGH[b - 0x80];
  }
  return out;
}

/**
 * Removes a trailing SAUCE metadata record (everything from the first 0x1A EOF marker).
 * @param {Uint8Array} bytes The raw file bytes.
 * @returns {Uint8Array} The art bytes without metadata.
 */
export function stripSauce(bytes) {
  const eof = bytes.indexOf(0x1a);
  return eof < 0 ? bytes : bytes.subarray(0, eof);
}

/**
 * Decodes an art file: UTF-8 when the bytes are valid UTF-8, otherwise CP437.
 * @param {Uint8Array} bytes The raw file bytes.
 * @returns {string} The art text.
 */
export function decodeArt(bytes) {
  const art = stripSauce(bytes);
  try {
    return new TextDecoder("utf-8", { fatal: true }).decode(art);
  } catch {
    return decodeCp437(art);
  }
}
