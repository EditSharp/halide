"""Drags a borderless project window's resize grips on X11 and checks the window follows.

Needs xdotool and a running window manager (the compositor does the resize).

Usage:
  python3 Tools/linux_resize_check.py [--godot EXE] [--extra ARGS] [--out FILE.png]
"""
import argparse
import os
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from context_menu_preview import PROJECT, find_godot, wait_for_rect, capture

SCENE = "res://Tools/Scenes/Tools/WindowChromePreview.tscn"


def xdotool(*args):
    return subprocess.run(["xdotool", *map(str, args)], check=True, capture_output=True, text=True).stdout


def geometry():
    """The preview window's x, y, width, height as X sees it."""
    window = xdotool("search", "--name", "Chrome Preview").split()[0]
    values = dict(line.split("=") for line in xdotool("getwindowgeometry", "--shell", window).split())
    return int(values["X"]), int(values["Y"]), int(values["WIDTH"]), int(values["HEIGHT"])


def drag(start, by):
    x, y = start
    xdotool("mousemove", x, y)
    time.sleep(0.3)
    xdotool("mousedown", 1)
    # the resize starts from wherever the pointer is once the app has handled the press
    time.sleep(0.5)
    for step in range(1, 11):
        xdotool("mousemove", x + by[0] * step // 10, y + by[1] * step // 10)
        time.sleep(0.05)
    time.sleep(0.3)
    xdotool("mouseup", 1)
    time.sleep(0.8)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--godot")
    parser.add_argument("--extra", default="")
    parser.add_argument("--out")
    args = parser.parse_args()

    fd, rect_file = tempfile.mkstemp(suffix=".txt")
    os.close(fd)
    os.remove(rect_file)

    command = [find_godot(args.godot), "--path", PROJECT, *args.extra.split(), SCENE, "--", "--hold=12000", f"--out={rect_file}"]
    process = subprocess.Popen(command)
    failures = []
    try:
        if wait_for_rect(rect_file, timeout=90) is None:
            sys.exit("the window never reported itself")
        time.sleep(0.5)

        x, y, w, h = geometry()
        print(f"before: {x},{y} {w}x{h}", flush=True)

        # the bottom right corner, 3px in from the edge
        drag((x + w - 3, y + h - 3), (-120, -90))
        x2, y2, w2, h2 = geometry()
        print(f"after corner: {x2},{y2} {w2}x{h2}", flush=True)
        if (w2, h2) != (w - 120, h - 90) or (x2, y2) != (x, y):
            failures.append(f"corner drag gave {w2}x{h2} at {x2},{y2}, expected {w - 120}x{h - 90} at {x},{y}")

        # the left edge, halfway down
        drag((x2 + 2, y2 + h2 // 2), (60, 0))
        x3, y3, w3, h3 = geometry()
        print(f"after left edge: {x3},{y3} {w3}x{h3}", flush=True)
        if (x3, w3, h3) != (x2 + 60, w2 - 60, h2):
            failures.append(f"left drag gave {w3}x{h3} at {x3},{y3}, expected {w2 - 60}x{h2} at {x2 + 60},{y2}")

        if args.out:
            capture((x3, y3, w3, h3), os.path.abspath(args.out))
    finally:
        process.kill()
        if os.path.exists(rect_file):
            os.remove(rect_file)

    for failure in failures:
        print(f"FAIL {failure}", flush=True)
    if failures:
        sys.exit(1)
    print("resize grips OK", flush=True)


if __name__ == "__main__":
    main()
