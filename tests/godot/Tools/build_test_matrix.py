"""Regenerate the repository-level CSV inventory from the actual test fixtures.

New tests are always marked "NEW - classify me", even when added to an existing
fixture. Classify the test as API-only or real-input and set platform status before
committing. Use --check in CI to catch stale inventory or missing classifications.
"""
import argparse
import csv
import glob
import os
import re

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
PROJECT = os.path.join(REPO, "halide")
SUITE = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Suite")
CSV_PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "test_matrix.csv")

# fixture -> (area, api_only_by_design, note). Edit this as fixtures are added/reclassified.
FIXTURES = {
    "ChromeTests": ("Window chrome", True, "RegionAt() hit-testing classification on an isolated bar scene, not a real OS drag; the real end-to-end path (actual WM_NCHITTEST against a real window) is covered by WindowsChromeTests"),
    "CommandRegistryTests": ("Command registry", True, "pure data structure"),
    "DockDragTests": ("Dock dragging", False, ""),
    "NativeDockDragTests": ("Dock dragging", False, "real input"),
    "DockTreeTests": ("Dock tree model", True, "pure data structure"),
    "ExtensionTests": ("Extensions", True, "extension loading is code-level, not UI"),
    "KeyboardChainTests": ("Keyboard shortcuts", True, "command-routing logic (Keyboard.Capture/Commands.Run called directly, no UI involved); the click-driven focus-routing part is covered by NativeKeyboardChainTests"),
    "NativeKeyboardChainTests": ("Keyboard shortcuts", False, "real input"),
    "LayoutServiceTests": ("Layouts", True, "layout-service persistence/capture semantics (Open/Close/Capture/ApplyPreset called directly); the drag interaction that rearranges docks for real is covered separately by NativeDockDragTests"),
    "LayoutStoreTests": ("Layout persistence format", True, "JSON format, not interaction"),
    "MediaServiceTests": ("Media library", True, "calls Media.Import directly with fabricated paths -- importer-resolution logic (priority, dedup, undo), not a drag-drop/file-picker UI interaction; converting would mean new OS file-drop automation for no real coverage gain"),
    "MenuTests": ("Bar menus", True, "builds/inspects the ContextMenu data model directly (titles, ticks, greyed-out state) -- the real open/switch/click render path is covered separately by NativeBarMenuTests, so these are appropriately API-level per user decision 2026-09-27"),
    "NativeBarMenuTests": ("Bar menus", False, "real input"),
    "ProjectTests": ("Projects", True, "project lifecycle through the API directly (NewProjectAsync/Save/SaveAs/OpenAsync) -- no dialogs even involved; native dialog clicking is covered separately (see project memory: native dialogs, Windows+macOS CI-verified)"),
    "RemoteTests": ("Remote/JSON-RPC", True, "protocol framing"),
    "RenderingTests": ("Playback", True, "exercises the real renderer (waveform decode, playback clock) via direct API calls (Play/Pause) -- there's no meaningful UI gesture alternative since a play button click would just call the same method"),
    "ReopenLoopTests": ("Projects", True, "project reopen/persistence through the API directly, no dialogs"),
    "ReopenTests": ("Projects", True, "project reopen/persistence through the API directly, no dialogs"),
    "SceneTests": ("Scene smoke tests", True, "infra, not a feature"),
    "SettingsTests": ("Settings window", True, "settings persistence and page-listing checks called/read directly; not a real click into the settings UI"),
    "ShortcutTests": ("Keyboard shortcuts", True, "KeyCombo/ShortcutMap parsing, defaults, bind/unbind and JSON save-load -- plain C# unit tests, no UI or real input involved at all"),
    "ThemeTests": ("Themes", True, "resource/asset validation"),
    "TimelineServiceTests": ("Timeline editing", False, ""),
    "NativeTimelineTests": ("Timeline editing", False, "real input"),
    "WindowStateTests": ("Window state", False, "drives window position/mode through WindowChrome.Place and Window.Mode, but exercises the real OS window (real WM_MOVE/WM_SIZE, real RestoredRect tracking) the same way WindowsChromeTests' SendMessageW calls count as real -- not a literal mouse drag-resize, but not an in-process stand-in either"),
    "WindowsChromeTests": ("Window chrome", False, "real OS hit-test messages, close to real input already"),
}

# individual tests that are API-only by design even in a fixture that's otherwise convertible
API_ONLY_TESTS = {
    ("TimelineServiceTests", "ABatchIsOneEntry"),
    ("TimelineServiceTests", "BatchesNestIntoTheOuterEntry"),
    ("TimelineServiceTests", "ACancelledBatchChangesNothing"),
    ("TimelineServiceTests", "ABatchThatThrowsRollsBack"),
    ("TimelineServiceTests", "ChangedFiresOnEdits"),
    ("TimelineServiceTests", "FindingClipsById"),
}

# the reverse: tests worth converting to real input despite their fixture defaulting to API-only
# (a genuine click-to-effect chain, not just a data-shape check)
NOT_API_ONLY_TESTS = {
    ("MenuTests", "RunningAViewItemTogglesIt"),
    ("MenuTests", "PickingALayoutFromTheMenuAppliesItAndUnticksTheRest"),
}

TEST_RE = re.compile(r"\[Test[^\]]*\][^\n]*\n\s*public (?:void|async Task) (\w+)\(")
FIXTURE_RE = re.compile(r"public (?:sealed )?class (\w+)")


def discover():
    found = []
    for path in sorted(glob.glob(os.path.join(SUITE, "*.cs"))):
        text = open(path, encoding="utf-8").read()
        m = FIXTURE_RE.search(text)
        if not m:
            continue
        fixture = m.group(1)
        for test in TEST_RE.findall(text):
            found.append((fixture, test))
    return found


def load_existing():
    existing = {}
    if os.path.exists(CSV_PATH):
        with open(CSV_PATH, encoding="utf-8") as f:
            for row in csv.DictReader(f):
                existing[(row["Fixture"], row["Test"])] = row
    return existing


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail if the CSV is stale or contains unclassified tests")
    args = parser.parse_args()
    found = discover()
    existing = load_existing()
    rows = []
    new_count = 0
    new_tests = []

    for fixture, test in found:
        key = (fixture, test)
        if key in existing:
            rows.append(existing[key])
            continue

        new_count += 1
        area = FIXTURES.get(fixture, ("NEW - classify me", False, ""))[0]
        new_tests.append(f"{fixture}.{test}")
        rows.append({
            "Area": area, "Fixture": fixture, "Test": test,
            "API-only by design": "NEW - classify me",
            "Windows real input": "classify",
            "Linux real input": "classify",
            "macOS real input": "classify",
            "Notes": "NEW - classify me: state why this is API-only, or describe the real user input and platform status.",
        })

    rows.sort(key=lambda r: (r["Area"], r["Fixture"], r["Test"]))

    import io
    output = io.StringIO(newline="")
    w = csv.DictWriter(output, fieldnames=["Area", "Fixture", "Test", "API-only by design", "Windows real input", "Linux real input", "macOS real input", "Notes"], lineterminator="\n")
    w.writeheader()
    w.writerows(rows)
    generated = output.getvalue()

    if args.check:
        stale = not os.path.exists(CSV_PATH) or open(CSV_PATH, encoding="utf-8", newline="").read() != generated
        unclassified = []
        for row in rows:
            api_only = row["API-only by design"]
            statuses = [row[column] for column in ("Windows real input", "Linux real input", "macOS real input")]
            invalid = api_only not in {"yes", "no"} or row["Area"] == "NEW - classify me"
            invalid |= api_only == "yes" and statuses != ["n/a"] * 3
            invalid |= api_only == "no" and any(status not in {"done", "todo"} and not status.startswith("blocked") for status in statuses)
            if invalid:
                unclassified.append(f"{row['Fixture']}.{row['Test']}")
        if stale or unclassified:
            if stale:
                print("test_matrix.csv is stale; run python tests/godot/Tools/build_test_matrix.py")
            if unclassified:
                print("Classify each newly discovered test in test_matrix.csv:")
                print("\n".join(f"  {test}" for test in unclassified))
            return 1
        print(f"test inventory is current ({len(rows)} tests)")
        return 0

    with open(CSV_PATH, "w", newline="", encoding="utf-8") as f:
        f.write(generated)
    print(f"{len(rows)} tests, {new_count} new")
    for test in new_tests:
        print(f"  classify {test}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
