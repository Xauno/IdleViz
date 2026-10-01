import { copyFileSync, mkdtempSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { isPreset } from "../IdleViz/web/visualizer-state.js";

// The vendored converter has to work with no network: its WebAssembly is inside the file.
// This package is an ES module package, so the browser bundle is loaded from a .cjs copy.
const root = resolve(import.meta.dirname, "..");
const copy = join(mkdtempSync(join(tmpdir(), "idleviz-converter-")), "converter.cjs");
copyFileSync(resolve(root, "IdleViz", "web", "vendor", "milkdrop-preset-converter.min.js"), copy);
const converter = createRequire(import.meta.url)(copy);
const sample = readFileSync(resolve(root, "tests", "fixtures", "sample.milk"), "utf8");

describe("the vendored Milkdrop converter", () => {
  it("converts a preset, equations and shaders included", async () => {
    const preset = await converter.convertPreset(sample);
    expect(isPreset(preset)).toBe(true);
    expect(preset.baseVals).toMatchObject({ decay: 0.94, zoom: 1.01, wave_mode: 2 });
    expect(preset.frame_eqs_str).toContain("a['zoom']=(a['zoom']+(0.03*a['bass_att']));");
    expect(preset.pixel_eqs_str).toContain("a['rad']");
    expect(preset.warp).toContain("shader_body");
    expect(preset.warp).toContain("texture(sampler_main, uv)");
    expect(preset.comp).not.toContain("parsing failed");
    // What Swift caches is this object as JSON.
    expect(isPreset(JSON.parse(JSON.stringify(preset)))).toBe(true);
  });

  it("marks a shader it can't translate, which Swift treats as a failure", async () => {
    const broken = sample.replace("ret = tex2D( sampler_main, uv ).xyz;", "ret = no_such_function(uv);");
    const preset = await converter.convertPreset(broken);
    expect(preset.warp).toContain("parsing failed");
  });
});
