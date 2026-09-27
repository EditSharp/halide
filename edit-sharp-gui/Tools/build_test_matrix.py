"""Regenerates Tools/test_matrix.csv from the actual test fixtures, keeping the hand-classified
Windows/Linux/macOS real-input status columns for tests it already knows about, and flagging any
newly-found test as "NEW - classify me" so it doesn't get silently missed.

Run this whenever a fixture or test is added or renamed:
    python Tools/build_test_matrix.py
Then edit test_matrix.csv (a plain CSV, open it in anything) to fill in real classifications for
anything marked "NEW - classify me", and update status columns as tests get converted.
"""
import csv
import glob
import os
import re

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SUITE = os.path.join(PROJECT, "Scripts", "Tests", "Suite")
CSV_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "test_matrix.csv")

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
    found = discover()
    existing = load_existing()
    rows = []
    new_count = 0

    for fixture, test in found:
        key = (fixture, test)
        if key in existing:
            rows.append(existing[key])
            continue

        new_count += 1
        area, api_only, note = FIXTURES.get(fixture, ("NEW - classify me", False, "fixture not classified yet in FIXTURES dict"))
        is_api_only = (api_only or key in API_ONLY_TESTS) and key not in NOT_API_ONLY_TESTS
        status = "n/a" if is_api_only else "todo"
        mac_status = "n/a" if is_api_only else "blocked (no VM)"
        rows.append({
            "Area": area, "Fixture": fixture, "Test": test,
            "API-only by design": "yes" if is_api_only else "no",
            "Windows real input": status, "Linux real input": status, "macOS real input": mac_status,
            "Notes": note if fixture in FIXTURES else "NEW - classify me",
        })

    # tests that no longer exist in code are dropped silently (renamed/removed)
    rows.sort(key=lambda r: (r["Area"], r["Fixture"], r["Test"]))

    with open(CSV_PATH, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=["Area", "Fixture", "Test", "API-only by design", "Windows real input", "Linux real input", "macOS real input", "Notes"])
        w.writeheader()
        w.writerows(rows)

    print(f"{len(rows)} tests, {new_count} new (check for 'NEW - classify me' rows)")


if __name__ == "__main__":
    main()
