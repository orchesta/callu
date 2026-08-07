import { describe, it, expect } from "vitest";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";

/** The base DialogContent caps itself with `sm:max-w-lg`. A caller that answers with a plain
 * `max-w-4xl` sets a different breakpoint key, so tailwind-merge keeps both and the media query
 * wins — the dialog silently stays 512px wide and its content scrolls sideways. */

const SRC = join(import.meta.dirname, "..", "..", "..");

function tsxFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) return entry === "node_modules" ? [] : tsxFiles(full);
    return full.endsWith(".tsx") ? [full] : [];
  });
}

/** Every `<DialogContent className="...">` in the tree, with the file it came from. */
function dialogWidthClasses() {
  const found: Array<{ file: string; classes: string }> = [];
  for (const file of tsxFiles(SRC)) {
    const source = readFileSync(file, "utf8");
    for (const match of source.matchAll(/<DialogContent[^>]*?className="([^"]*)"/g)) {
      found.push({ file: relative(SRC, file).replace(/\\/g, "/"), classes: match[1] });
    }
  }
  return found;
}

describe("dialog widths", () => {
  it("finds the dialogs, so an empty scan cannot pass as a clean one", () => {
    expect(dialogWidthClasses().length).toBeGreaterThan(20);
  });

  it("never sets a width the base component's sm: cap silently overrides", () => {
    const offenders = dialogWidthClasses()
      .filter(({ classes }) => /(?:^|\s)max-w-(?:\w+|\[[^\]]+\])/.test(classes))
      .map(({ file, classes }) => `${file}: ${classes}`);

    expect(offenders, "prefix these with sm: — a bare max-w- is ignored above 640px").toEqual([]);
  });
});
