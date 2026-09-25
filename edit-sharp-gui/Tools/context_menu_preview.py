"""Renders a ContextMenu through this OS's real native menu and saves a PNG of it.

Launches the game with Tools/Scenes/Tools/ContextMenuPreview.tscn, which opens the
menu through the platform handler and reports the popup's screen rect; this script
then captures that region with the OS's own screenshot tool and writes the image.
Meant to run on whatever machine or VM has the OS you want to see.

Needs only Python 3 and the OS capture tool:
  Windows  PowerShell (built in)
  macOS    screencapture (built in)
  Linux    one of grim, scrot, import (ImageMagick), gnome-screenshot

Usage:
  python Tools/context_menu_preview.py [--menu res://Path/To/Menu.tres] [--out FILE.png]
                                       [--godot EXE] [--hold MS] [--margin PX]

  --menu    a ContextMenu resource; without it a showcase of every item kind is shown
  --out     where to write the PNG; default Tools/Previews/context-menu-<os>.png (a .gdignore
            keeps Godot from importing that folder)
  --godot   the Godot executable; else $GODOT, else godot / godot4 / godot-mono on PATH
  --hold    how long the menu stays open, in ms (default 2000)
  --margin  pixels captured around the popup for its shadow (default 12)
  --hover   highlight the Nth selectable item, to see the hot look
  --palette a ThemePalette to swap in first, e.g. res://Themes/Light.tres
"""
import argparse
import os
import platform
import shutil
import subprocess
import sys
import tempfile
import time

PROJECT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SCENE = "res://Tools/Scenes/Tools/ContextMenuPreview.tscn"
OS_NAME = {"Windows": "windows", "Darwin": "macos", "Linux": "linux"}.get(platform.system(), platform.system().lower())


def find_godot(explicit):
    candidates = [explicit, os.environ.get("GODOT"), "godot", "godot4", "godot-mono", "Godot"]
    if platform.system() == "Darwin":
        candidates += ["/Applications/Godot_mono.app/Contents/MacOS/Godot", "/Applications/Godot.app/Contents/MacOS/Godot"]
    for c in candidates:
        if not c:
            continue
        path = c if os.path.isfile(c) else shutil.which(c)
        if path:
            return path
    sys.exit("godot not found: pass --godot or set $GODOT")


def wait_for_rect(path, timeout):
    deadline = time.time() + timeout
    while time.time() < deadline:
        if os.path.exists(path):
            with open(path) as f:
                text = f.read().strip()
            if text:
                return text
        time.sleep(0.05)
    return None


def capture(rect, out):
    system = platform.system()
    if system == "Windows":
        capture_windows(rect, out)
    elif system == "Darwin":
        capture_macos(rect, out)
    else:
        capture_linux(rect, out)


def capture_windows(rect, out):
    if rect:
        x, y, w, h = rect
        region = f"$x = {x}; $y = {y}; $w = {w}; $h = {h}"
    else:
        region = "$s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $x = 0; $y = 0; $w = $s.Width; $h = $s.Height"
    # a 24-bit target: the screen's layered popup leaves stray alpha in a 32-bit copy
    script = (
        "Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class Dpi { [DllImport(\"user32.dll\")] public static extern bool SetProcessDPIAware(); }'; "
        "[Dpi]::SetProcessDPIAware() | Out-Null; "
        "Add-Type -AssemblyName System.Drawing; Add-Type -AssemblyName System.Windows.Forms; "
        + region + "; "
        "$b = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb); "
        "$g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($x, $y, 0, 0, $b.Size); "
        f"$b.Save('{out}')"
    )
    subprocess.run(["powershell", "-NoProfile", "-Command", script], check=True)


def capture_macos(rect, out):
    args = ["screencapture", "-x"]
    if rect:
        x, y, w, h = rect
        args += ["-R", f"{x},{y},{w},{h}"]
    subprocess.run(args + [out], check=True)


def capture_linux(rect, out):
    tools = []
    if rect:
        x, y, w, h = rect
        tools = [
            ["grim", "-g", f"{x},{y} {w}x{h}", out],
            ["scrot", "-a", f"{x},{y},{w},{h}", out],
            ["import", "-window", "root", "-crop", f"{w}x{h}+{x}+{y}", "+repage", out],
        ]
    tools += [["grim", out], ["scrot", out], ["import", "-window", "root", out], ["gnome-screenshot", "-f", out]]
    for tool in tools:
        if shutil.which(tool[0]):
            subprocess.run(tool, check=True)
            return
    sys.exit("no screenshot tool found: install grim, scrot, imagemagick or gnome-screenshot")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--menu")
    parser.add_argument("--out")
    parser.add_argument("--godot")
    parser.add_argument("--hold", type=int, default=2000)
    parser.add_argument("--margin", type=int, default=12)
    parser.add_argument("--hover", type=int, default=0)
    parser.add_argument("--palette")
    args = parser.parse_args()

    out = args.out
    if not out:
        folder = os.path.join(PROJECT, "Tools", "Previews")
        os.makedirs(folder, exist_ok=True)
        ignore = os.path.join(folder, ".gdignore")
        if not os.path.exists(ignore):
            open(ignore, "w").close()
        out = os.path.join(folder, f"context-menu-{OS_NAME}.png")
    out = os.path.abspath(out)

    fd, rect_file = tempfile.mkstemp(suffix=".txt")
    os.close(fd)
    os.remove(rect_file)

    godot = find_godot(args.godot)
    command = [godot, "--path", PROJECT, SCENE, "--", f"--hold={args.hold}", f"--out={rect_file}"]
    if args.menu:
        command.append(f"--menu={args.menu}")
    if args.hover:
        command.append(f"--hover={args.hover}")
    if args.palette:
        command.append(f"--palette={args.palette}")
    process = subprocess.Popen(command)

    try:
        report = wait_for_rect(rect_file, timeout=60)
        if report is None:
            sys.exit("the menu never reported itself; is the scene able to open a window?")
        if report == "fail":
            sys.exit("the preview scene failed; see godot's output above")

        rect = None
        if report != "unknown":
            x, y, w, h = (int(v) for v in report.split())
            m = args.margin
            rect = (x - m, y - m, w + 2 * m, h + 2 * m)
            print(f"menu at {x},{y} size {w}x{h}")
        else:
            print("menu rect unknown on this platform; capturing the whole screen")

        time.sleep(0.3)  # let the popup finish its fade-in
        capture(rect, out)
        print(out)
    finally:
        try:
            process.wait(timeout=args.hold / 1000 + 15)
        except subprocess.TimeoutExpired:
            process.kill()
        if os.path.exists(rect_file):
            os.remove(rect_file)


if __name__ == "__main__":
    main()
