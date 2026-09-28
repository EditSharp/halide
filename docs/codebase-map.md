# Halide codebase map

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

### `halide/Scripts/Api/` — 33 C# files

Public app services and extension-facing registrations.

- `Api/` (17 files)
- `Api/Extensions/` (7 files)
- `Api/Remote/` (9 files)

### `halide/Scripts/App/` — 32 C# files

Application lifecycle, projects, commands, settings, layouts, and native window integration.

- `App/` (11 files)
- `App/Chrome/` (5 files)
- `App/Chrome/Platform/` (2 files)
- `App/Commands/` (4 files)
- `App/Layouts/` (4 files)
- `App/Platform/` (1 files)
- `App/Settings/` (5 files)

### `halide/Scripts/Input/` — 6 C# files

Shared mouse state and keyboard shortcut routing. See the input-system guide.

- `Input/` (6 files)

### `halide/Scripts/Root/` — 4 C# files

Supporting runtime code and feature-specific models.

- `Root/` (4 files)

### `halide/Scripts/Tests/` — 22 C# files

Godot smoke/probe scripts and the thin host for the separate test assembly.

- `Tests/` (21 files)
- `Tests/Runner/` (1 files)

### `halide/Scripts/UI/` — 135 C# files

Godot controls, views, docking, menus, dialogs, inspectors, and timeline interactions.

- `UI/` (6 files)
- `UI/Components/` (12 files)
- `UI/ContextMenu/` (4 files)
- `UI/ContextMenu/Elements/` (8 files)
- `UI/ContextMenu/Platform/` (3 files)
- `UI/ContextMenu/Platform/GodotDrawn/` (2 files)
- `UI/ContextMenu/Platform/MacOS/` (3 files)
- `UI/ContextMenu/Platform/Windows/` (4 files)
- `UI/Dialog/` (13 files)
- `UI/Dialog/Platform/` (5 files)
- `UI/Dialog/Platform/MacOS/` (1 files)
- `UI/Dialog/Platform/Windows/` (3 files)
- `UI/Docking/` (17 files)
- `UI/DragDrop/` (1 files)
- `UI/DragDrop/Platform/Windows/` (1 files)
- `UI/Inspector/` (10 files)
- `UI/Inspector/Editors/` (13 files)
- `UI/Settings/` (6 files)
- `UI/Theme/` (9 files)
- `UI/Thumbnails/` (6 files)
- `UI/Views/` (8 files)

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

- `halide/Scripts/UI/Components/UIClipsView.cs` — 1974 lines
- `halide/Scripts/UI/Views/UITimeline.cs` — 1146 lines
- `halide/Scripts/UI/Views/MediaViewer.cs` — 1105 lines
- `halide/Scripts/Tests/SmokeTest.cs` — 1069 lines
- `halide/Scripts/UI/Dialog/Platform/Windows/NativeDialog.cs` — 912 lines
- `halide/Scripts/UI/ContextMenu/Platform/Windows/MenuThread.cs` — 773 lines
- `halide/Scripts/UI/Views/Inspector.cs` — 679 lines
- `halide/Scripts/UI/Components/UIClip.cs` — 648 lines
- `halide/Scripts/ProjectManager.cs` — 598 lines
- `halide/Scripts/UI/Views/Editor.cs` — 563 lines
- `halide/Scripts/UI/Views/UIPlayback.cs` — 530 lines

## Tests and change workflow

- `tests/` contains repository-level .NET tests and `tests/godot/Halide.GodotTests.csproj`, which owns Godot fixtures and runner logic.
- `halide/Scripts/Tests/Runner/TestRunner.cs` is the small scene host that loads the separate test assembly. Godot probe scripts and scenes remain under `halide/` because the app project owns their `res://` resources.
- Add or rename a `[Test]` in `tests/godot/Suite/`, then run `python tests/godot/Tools/build_test_matrix.py` and classify any new row. CI fails if the inventory is stale or a new fixture lacks an explicit classification.
- Update this map by running `python docs/generate_codebase_map.py`. CI fails when generated output is stale.
- Keep source comments focused on non-obvious behavior. CI rejects a single uninterrupted source comment longer than 40 lines.
- Before a structural refactor, run the relevant .NET test or Godot fixture; for UI behavior, prefer the real-input fixture and platform listed in the CSV.
