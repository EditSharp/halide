"""Runs Halide's Godot test fixtures, one fixture per process, and writes one JUnit report.

A native crash then only loses its own fixture, and is reported as a failure (or as a known issue when every test in
the fixture is marked [KnownIssue]).

Usage:
  python tests/godot/Tools/run_tests.py [--godot EXE] [--assembly DLL] [--windowed] [--filter TEXT] [--out DIR] [--extra "GODOT ARGS"]

  default     headless: every test that doesn't need windows
  --windowed  with windows: only the [Windowed] tests
  --out       where results.xml, per-fixture logs and failure screenshots go (default tests/godot/TestResults)
  --extra     engine arguments for every run, such as a rendering driver on a machine without a GPU
"""
import argparse
import os
import re
import subprocess
import sys
import time
import shlex
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
PROJECT = str(REPO / "halide")
TEST_ASSEMBLY = REPO / "tests" / "godot" / "bin" / "Debug" / "net10.0" / "Halide.GodotTests.dll"
sys.path.insert(0, str(Path(PROJECT) / "Tools"))
from context_menu_preview import find_godot

SCENE = "res://Tools/Scenes/Tests/TestRunner.tscn"
EXTRA = []
LINE = re.compile(r"^(PASS|FAIL|SKIP|KNOWN) (\w+)\.(\w+) \(([\d.]+)s\)(?: - (.*))?$")


def godot_command(godot, headless, *args):
    command = [godot]
    if headless:
        command.append("--headless")
    return command + EXTRA + ["--path", PROJECT, "--scene", SCENE, "--", f"--test-assembly={TEST_ASSEMBLY}", *args]


def list_tests(godot):
    # a hang here used to take the whole run down with a bare, uninformative traceback and nothing else
    # to go on -- Godot itself has been seen to hang on --list with zero output on a freshly-provisioned
    # CI runner. one retry (a truly stuck runner won't be helped by it, but a slow first-run JIT/shader
    # warmup will) plus surfacing whatever the process did print gives something to actually debug from
    for attempt in (1, 2):
        try:
            out = subprocess.run(godot_command(godot, True, "--list"), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=300).stdout
            break
        except subprocess.TimeoutExpired as e:
            text = lambda b: b.decode("utf-8", "replace") if isinstance(b, bytes) else (b or "")
            partial = (text(e.stdout) + text(e.stderr)).strip()
            print(f"--list timed out after 300s (attempt {attempt}/2){': ' + partial if partial else ' -- no output at all before the timeout'}", file=sys.stderr, flush=True)
            if attempt == 2:
                sys.exit("--list hung twice in a row: Godot itself isn't responding, not a test problem. Check the runner/GPU driver rather than the test suite.")

    tests = {}
    for line in out.splitlines():
        m = re.match(r"^TEST (\w+)\.(\w+)( \[known\])?$", line.strip())
        if m:
            tests.setdefault(m.group(1), []).append((m.group(2), bool(m.group(3))))
    return tests


def run_fixture(godot, fixture, tests, windowed, out):
    log_path = os.path.join(out, f"{fixture}.log")
    args = [f"--fixture={fixture}", f"--artifacts={os.path.join(out, 'screenshots')}"]
    if windowed:
        args.append("--windowed-only")

    started = time.time()
    try:
        # belt-and-suspenders alongside the machine-wide ForegroundLockTimeout=0 the runner agent sets:
        # a process launched automatically (not from a keystroke the user just made) doesn't get Windows'
        # permission to steal foreground focus by default; native menu tracking needs that or it misbehaves
        if windowed and sys.platform == "win32":
            import ctypes
            ctypes.windll.user32.AllowSetForegroundWindow(-1)  # ASFW_ANY

        proc = subprocess.run(godot_command(godot, not windowed, *args), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600)
        output, code = proc.stdout + proc.stderr, proc.returncode
    except subprocess.TimeoutExpired as e:
        text = lambda b: b.decode("utf-8", "replace") if isinstance(b, bytes) else (b or "")
        output, code = text(e.stdout) + text(e.stderr) + "\n[timed out]", -1
    with open(log_path, "w", encoding="utf-8") as f:
        f.write(output)

    results = {}
    for line in output.splitlines():
        m = LINE.match(line.strip())
        if m:
            results[m.group(3)] = (m.group(1), float(m.group(4)), m.group(5) or "")

    finished = "TEST RUN EXIT" in output or any(l.startswith("TESTS ") for l in output.splitlines())
    known = {name for name, k in tests if k}
    for name, _ in tests:
        if name in results:
            continue
        if windowed and name not in results:
            continue
        hung = next((l.strip() for l in output.splitlines() if l.startswith("HUNG ")), None)
        reason = hung or ("the process crashed before this test reported" if not finished else "didn't run")
        results[name] = ("KNOWN" if name in known else "FAIL", 0.0, f"{reason} (exit code {code}); see {os.path.basename(log_path)}")

    return results, time.time() - started


def main():
    global TEST_ASSEMBLY
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--godot")
    parser.add_argument("--assembly", default=str(TEST_ASSEMBLY))
    parser.add_argument("--windowed", action="store_true")
    parser.add_argument("--filter")
    parser.add_argument("--out", default=os.path.join(REPO, "tests", "godot", "TestResults"))
    parser.add_argument("--extra", default="")
    args = parser.parse_args()
    TEST_ASSEMBLY = Path(args.assembly).resolve()
    EXTRA.extend(shlex.split(args.extra))

    godot = find_godot(args.godot)
    if not TEST_ASSEMBLY.is_file():
        sys.exit(f"test assembly not found: {TEST_ASSEMBLY}\nBuild it with: dotnet build tests/godot/Halide.GodotTests.csproj")
    os.makedirs(args.out, exist_ok=True)
    fixtures = list_tests(godot)
    if not fixtures:
        sys.exit("no tests found: did the project build?")

    suites = ET.Element("testsuites")
    totals = {"PASS": 0, "FAIL": 0, "SKIP": 0, "KNOWN": 0}
    for fixture, tests in sorted(fixtures.items()):
        if args.filter and args.filter.lower() not in fixture.lower():
            continue
        results, seconds = run_fixture(godot, fixture, tests, args.windowed, args.out)
        suite = ET.SubElement(suites, "testsuite", name=fixture, tests=str(len(results)), time=f"{seconds:.3f}")
        for name, (outcome, took, message) in results.items():
            totals[outcome] += 1
            case = ET.SubElement(suite, "testcase", classname=fixture, name=name, time=f"{took:.3f}")
            if outcome == "FAIL":
                ET.SubElement(case, "failure", message=message.splitlines()[0] if message else "").text = message
            elif outcome in ("SKIP", "KNOWN"):
                ET.SubElement(case, "skipped", message=message)
            print(f"{outcome} {fixture}.{name}{' - ' + message.splitlines()[0] if message and outcome != 'PASS' else ''}", flush=True)
        suite.set("failures", str(sum(1 for o, _, _ in results.values() if o == "FAIL")))

    ET.ElementTree(suites).write(os.path.join(args.out, "results.xml"), encoding="utf-8", xml_declaration=True)
    print(f"TOTAL {totals['PASS']} passed, {totals['FAIL']} failed, {totals['SKIP']} skipped, {totals['KNOWN']} known issues", flush=True)
    sys.exit(1 if totals["FAIL"] else 0)


if __name__ == "__main__":
    main()
