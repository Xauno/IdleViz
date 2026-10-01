# Vendored libraries

These files are copied unchanged from npm so the app builds without a Node step. `tests/vendor.test.js` checks their SHA-256 hashes against `checksums.json`.

| File | Package | Version | License |
| --- | --- | --- | --- |
| `butterchurn.min.js` | [butterchurn](https://www.npmjs.com/package/butterchurn) | 2.6.7 | MIT (`LICENSE-butterchurn.txt`) |
| `butterchurnPresets.min.js` | [butterchurn-presets](https://www.npmjs.com/package/butterchurn-presets) | 2.4.7 | MIT (`LICENSE-butterchurn-presets.txt`) |
| `butterchurnPresetsExtra.min.js` | butterchurn-presets | 2.4.7 | MIT |
| `butterchurnPresetsExtra2.min.js` | butterchurn-presets | 2.4.7 | MIT |
| `butterchurnPresetsMD1.min.js` | butterchurn-presets | 2.4.7 | MIT |
| `milkdrop-preset-converter.min.js` | [milkdrop-preset-converter](https://www.npmjs.com/package/milkdrop-preset-converter) | 0.1.2 | MIT (`LICENSE-milkdrop-preset-converter.txt`) |

The four packs hold 455 presets, 395 after dropping names that appear in more than one pack.

The converter turns original Milkdrop `.milk` presets into Butterchurn's format. It is one file with its WebAssembly built in, so it runs offline. Only `converter.html` loads it, not the visualizer page. If its version changes, change `MilkConversion.converterVersion` in `Sources/IdleVizCore/CustomPresets.swift` too, so cached conversions are redone.

## Updating

```bash
npm pack butterchurn@<version> butterchurn-presets@<version> milkdrop-preset-converter@<version>
```

Unpack the tarballs, copy the files above from `package/lib/` (`package/dist/` for the converter), and update `checksums.json` (`shasum -a 256 *.js`) and the versions in this table.
