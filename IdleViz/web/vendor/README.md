# Vendored libraries

These files are copied unchanged from npm so the app builds without a Node step. `tests/vendor.test.js` checks their SHA-256 hashes against `checksums.json`.

| File | Package | Version | License |
| --- | --- | --- | --- |
| `butterchurn.min.js` | [butterchurn](https://www.npmjs.com/package/butterchurn) | 2.6.7 | MIT (`LICENSE-butterchurn.txt`) |
| `butterchurnPresets.min.js` | [butterchurn-presets](https://www.npmjs.com/package/butterchurn-presets) | 2.4.7 | MIT (`LICENSE-butterchurn-presets.txt`) |
| `butterchurnPresetsExtra.min.js` | butterchurn-presets | 2.4.7 | MIT |
| `butterchurnPresetsExtra2.min.js` | butterchurn-presets | 2.4.7 | MIT |
| `butterchurnPresetsMD1.min.js` | butterchurn-presets | 2.4.7 | MIT |

The four packs hold 455 presets, 395 after dropping names that appear in more than one pack.

## Updating

```bash
npm pack butterchurn@<version> butterchurn-presets@<version>
```

Unpack the tarballs, copy the files above from `package/lib/`, and update `checksums.json` (`shasum -a 256 *.js`) and the versions in this table.
