// Converts one original Milkdrop preset to Butterchurn's JSON, for Swift to check and cache.

/**
 * @param {string} source  The text of a .milk file.
 * @returns {Promise<string>} The converted preset as JSON.
 */
async function convertMilk(source) {
  const converter = /** @type {any} */ (window).milkdropPresetConverter;
  if (typeof converter?.convertPreset !== "function") throw new Error("The converter didn't load");
  return JSON.stringify(await converter.convertPreset(String(source)));
}

Object.assign(window, { convertMilk });
