# Halide Godot test suite

The fixtures, reusable test framework, test inventory, report builder, and
interactive queue live in the separate `Halide.GodotTests.csproj` project here.
The app contains one small Godot scene host
(`halide/Scripts/Tests/Runner/TestRunner.cs`) that loads the test assembly. Probe
scripts and `res://` test scenes stay under `halide/` because the existing app
project owns those resources.

## Run tests

Build the separate test project, import the app's Godot project, and make a Godot
executable available on `PATH` (or pass `--godot`):

```powershell
dotnet build tests/godot/Halide.GodotTests.csproj
python tests/godot/Tools/run_tests.py --godot C:\path\to\Godot.exe
python tests/godot/Tools/run_tests.py --godot C:\path\to\Godot.exe --windowed
```

If you build Release or use a custom output directory, pass the test DLL with
`--assembly PATH`.

The default run covers non-windowed fixtures. `--windowed` runs real desktop
input tests and must run in an interactive session. Use `--filter NAME` to run
fixtures matching a name. Each fixture runs in its own Godot process; result XML,
logs, and failure recordings go to `--out` (default: `tests/godot/TestResults`).

On the dedicated Windows test desktop, `es_runner_agent.ps1` can watch its queue
and run real-input jobs submitted through `enqueue_test.py`.

## Add a test

1. Add a `[Test]` to a fixture under `tests/godot/Suite/`. Use real
   window/keyboard/mouse input for user behavior. Mark a case API-only only
   when it intentionally tests a data contract, persistence format, protocol,
   or other API behavior without a UI interaction.
2. Run `python tests/godot/Tools/build_test_matrix.py`. Every new case is marked
   `NEW - classify me`, including cases added to a known fixture.
3. In `test_matrix.csv`, set `API-only by design` to `yes` or `no`; record why
   in Notes. For API-only rows, use `n/a` for platform input columns. For UI
   tests, record the real-input status separately for Windows, Linux, and
   macOS (`done`, `todo`, or a specific blocker).
4. Run `python tests/godot/Tools/build_test_matrix.py --check`. CI runs this
   check so deleted, renamed, or newly added tests cannot leave the inventory
   stale or unclassified.

Do not copy a test's status to a platform until it has actually run there. A
passing headless API test does not count as real-input coverage.
