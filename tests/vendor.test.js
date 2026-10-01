import { createHash } from "node:crypto";
import { readFileSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";
import { describe, expect, it } from "vitest";

// The vendored libraries must stay byte-for-byte what npm published (see IdleViz/web/vendor/README.md).
const vendorDir = resolve(import.meta.dirname, "..", "IdleViz", "web", "vendor");
const checksums = JSON.parse(readFileSync(join(vendorDir, "checksums.json"), "utf8"));

describe("vendored libraries", () => {
  it("lists every vendored script", () => {
    const scripts = readdirSync(vendorDir).filter((f) => f.endsWith(".js"));
    expect(scripts.sort()).toEqual(Object.keys(checksums).sort());
  });

  it.each(Object.entries(checksums))("%s matches its checksum", (file, expected) => {
    const actual = createHash("sha256")
      .update(readFileSync(join(vendorDir, file)))
      .digest("hex");
    expect(actual).toBe(expected);
  });

  it("is loaded by the pages that need it", () => {
    const page = readFileSync(join(vendorDir, "..", "index.html"), "utf8");
    const converter = readFileSync(join(vendorDir, "..", "converter.html"), "utf8");
    for (const file of Object.keys(checksums)) {
      // The Milkdrop converter only runs in its own hidden page, away from the visualizer.
      const [html, other] = file.startsWith("milkdrop") ? [converter, page] : [page, converter];
      expect(html).toContain(`src="vendor/${file}"`);
      expect(other).not.toContain(file);
    }
  });
});
