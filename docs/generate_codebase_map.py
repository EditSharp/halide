"""Generate the repository's codebase map. Run with --check in CI."""

from __future__ import annotations

import argparse
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "halide"
OUTPUT = ROOT / "docs" / "codebase-map.md"

AREAS = {
    "Api": "Public app services and extension-facing registrations.",
    "App": "Application lifecycle, projects, commands, settings, layouts, and native window integration.",
    "Input": "Shared mouse state and keyboard shortcut routing. See the input-system guide.",
    "Tests": "Godot smoke/probe scripts and the thin host for the separate test assembly.",
    "UI": "Godot controls, views, docking, menus, dialogs, inspectors, and timeline interactions.",
}


def source_summary() -> str:
    scripts = APP / "Scripts"
    grouped: dict[str, list[Path]] = {}
    for path in sorted(scripts.rglob("*.cs")):
        if ".godot" in path.parts or path.name.endswith(".uid.cs"):
            continue
        relative = path.relative_to(scripts)
        area = relative.parts[0] if len(relative.parts) > 1 else "Root"
        grouped.setdefault(area, []).append(path)

    lines = []
    for area, paths in sorted(grouped.items()):
        description = AREAS.get(area, "Supporting runtime code and feature-specific models.")
        lines.append(f"### `halide/Scripts/{area}/` — {len(paths)} C# files")
        lines.append("")
        lines.append(description)
        lines.append("")
        folders = Counter("/".join(p.relative_to(scripts).parts[:-1]) or area for p in paths)
        for folder, count in sorted(folders.items()):
            lines.append(f"- `{folder}/` ({count} files)")
        lines.append("")
    return "\n".join(lines)


def large_files() -> str:
    candidates = []
    for path in (APP / "Scripts").rglob("*.cs"):
        try:
            count = sum(1 for _ in path.open(encoding="utf-8"))
        except UnicodeDecodeError:
            continue
        if count >= 500:
            candidates.append((count, path.relative_to(ROOT).as_posix()))
    candidates.sort(reverse=True)
    if not candidates:
        return "No C# source files are at least 500 lines long."
    return "\n".join(f"- `{path}` — {count} lines" for count, path in candidates)


def render() -> str:
    return f"""# Halide codebase map

This file is generated from the repository on each update. Regenerate with
`python docs/generate_codebase_map.py`; CI checks that the committed map matches
the current tree. Edit the generator's area descriptions when responsibilities
change; do not edit this generated file by hand.

## Repository layout

- `halide/` — Godot application project: C# runtime, scenes, themes, assets, native platform code.
- `tests/` — repository-level .NET tests and the separate Godot test project, tooling, and inventory.
- `extensions/` — sample extension built against the app API.
- `docs/` — generated map and focused developer guides.
- `.github/workflows/` — build, test, and platform probe automation.

## Runtime code at a glance

{source_summary()}
## Finding the main flows

- App startup and global nodes: `halide/Scenes/Host.tscn`, `halide/Scripts/App/Host.cs`, `halide/project.godot`.
- Project and editor lifecycle: `halide/Scripts/App/ProjectWindow.cs`, `ProjectSession.cs`, `halide/Scripts/ProjectManager.cs`.
- User-visible commands: `halide/Scripts/App/Commands/`, `halide/Scripts/Api/CommandsService.cs`.
- Extension boundary: `halide/Scripts/Api/Extensions/` and `extensions/Sample/`.
- Timeline editing: `halide/Scripts/UI/Views/UITimeline.cs`, `halide/Scripts/UI/Components/UIClipsView.cs`, `halide/Scripts/UI/Components/UIClip.cs`.
- Media browsing: `halide/Scripts/UI/Views/MediaViewer.cs` and `halide/Scripts/MediaLibrary.cs`.
- Input flow and extension points: [`docs/input-system.md`](input-system.md).
- Godot test project and platform matrix: [`tests/godot/README.md`](../tests/godot/README.md), [`tests/godot/Halide.GodotTests.csproj`](../tests/godot/Halide.GodotTests.csproj), [`tests/godot/test_matrix.csv`](../tests/godot/test_matrix.csv).

## Large runtime files to review

These counts are generated to make growing responsibilities visible. A large
file is a review prompt, not an instruction to split mechanically; group new
behavior behind cohesive types with one clear reason to change.

{large_files()}

## Tests and change workflow

- `tests/` contains repository-level .NET tests and `tests/godot/Halide.GodotTests.csproj`, which owns Godot fixtures and runner logic.
- `halide/Scripts/Tests/Runner/TestRunner.cs` is the small scene host that loads the separate test assembly. Godot probe scripts and scenes remain under `halide/` because the app project owns their `res://` resources.
- Add or rename a `[Test]` in `tests/godot/Suite/`, then run `python tests/godot/Tools/build_test_matrix.py` and classify any new row. CI fails if the inventory is stale or a new fixture lacks an explicit classification.
- Update this map by running `python docs/generate_codebase_map.py`. CI fails when generated output is stale.
- Keep source comments focused on non-obvious behavior. CI rejects a single uninterrupted source comment longer than 40 lines.
- Before a structural refactor, run the relevant .NET test or Godot fixture; for UI behavior, prefer the real-input fixture and platform listed in the CSV.
"""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="fail if the generated map is stale")
    args = parser.parse_args()
    expected = render()
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != expected:
            print("docs/codebase-map.md is stale; run python docs/generate_codebase_map.py")
            return 1
        print("codebase map is current")
        return 0
    OUTPUT.write_text(expected, encoding="utf-8", newline="\n")
    print(f"wrote {OUTPUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
