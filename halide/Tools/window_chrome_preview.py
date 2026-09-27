"""Opens a project window with its drawn top bar and saves a PNG of it as it appears on screen.

Launches Tools/Scenes/Tools/WindowChromePreview.tscn, which reports the window's rect; this
script captures it with the OS's own screenshot tool (see context_menu_preview.py).

Usage:
  python Tools/window_chrome_preview.py [--out FILE.png] [--godot EXE] [--palette RES] [--hover close|maximize] [--window] [--extra ARGS]
"""
import argparse
import os
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from context_menu_preview import PROJECT, OS_NAME, find_godot, wait_for_rect, capture

SCENE = "res://Tools/Scenes/Tools/WindowChromePreview.tscn"


def capture_window(hwnd, out):
    """Asks DWM for the window's composed content and crops it to the visible frame."""
    script = (
        "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class W { "
        "[StructLayout(LayoutKind.Sequential)] public struct R { public int l, t, r, b; } "
        "[DllImport(\"user32.dll\")] public static extern bool SetProcessDPIAware(); "
        "[DllImport(\"user32.dll\")] public static extern bool GetWindowRect(IntPtr h, out R r); "
        "[DllImport(\"user32.dll\")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags); "
        "[DllImport(\"dwmapi.dll\")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out R r, int s); }'; "
        "[W]::SetProcessDPIAware() | Out-Null; Add-Type -AssemblyName System.Drawing; "
        f"$h = [IntPtr]{hwnd}; $w = New-Object W+R; $f = New-Object W+R; "
        "[W]::GetWindowRect($h, [ref]$w) | Out-Null; [W]::DwmGetWindowAttribute($h, 9, [ref]$f, 16) | Out-Null; "
        "$b = New-Object System.Drawing.Bitmap ($w.r - $w.l), ($w.b - $w.t); $g = [System.Drawing.Graphics]::FromImage($b); "
        "$dc = $g.GetHdc(); [W]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); "
        "$crop = New-Object System.Drawing.Rectangle ($f.l - $w.l), ($f.t - $w.t), ($f.r - $f.l), ($f.b - $f.t); "
        f"$b.Clone($crop, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb).Save('{out}')"
    )
    subprocess.run(["powershell", "-NoProfile", "-Command", script], check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out")
    parser.add_argument("--godot")
    parser.add_argument("--palette")
    parser.add_argument("--hover")
    parser.add_argument("--hold", type=int, default=3000)
    parser.add_argument("--extra", default="")
    parser.add_argument("--full", action="store_true", help="also capture the whole screen, beside FILE as FILE-screen.png")
    parser.add_argument("--selfshot", action="store_true", help="also save what the window drew itself, as FILE-self.png")
    parser.add_argument("--window", action="store_true", help="Windows: capture through PrintWindow, for swap chains a screen copy misses")
    args = parser.parse_args()

    out = os.path.abspath(args.out or os.path.join(PROJECT, "Tools", "Previews", f"window-chrome-{OS_NAME}.png"))
    os.makedirs(os.path.dirname(out), exist_ok=True)

    fd, rect_file = tempfile.mkstemp(suffix=".txt")
    os.close(fd)
    os.remove(rect_file)

    command = [find_godot(args.godot), "--path", PROJECT, *args.extra.split(), SCENE, "--", f"--hold={args.hold}", f"--out={rect_file}"]
    if args.palette:
        command.append(f"--palette={args.palette}")
    if args.hover:
        command.append(f"--hover={args.hover}")
    if args.selfshot:
        command.append(f"--selfshot={out[:-4]}-self.png")
    process = subprocess.Popen(command)

    try:
        report = wait_for_rect(rect_file, timeout=90)
        if report is None:
            sys.exit("the window never reported itself")
        values = [int(v) for v in report.split()]
        x, y, w, h = values[:4]
        print(f"window at {x},{y} size {w}x{h}", flush=True)
        time.sleep(0.3)
        if args.window and len(values) > 4:
            capture_window(values[4], out)
        else:
            capture((x, y, w, h), out)
        if args.full:
            capture(None, out[:-4] + "-screen.png")
        print(out, flush=True)
    finally:
        try:
            process.wait(timeout=args.hold / 1000 + 30)
        except subprocess.TimeoutExpired:
            process.kill()
        if os.path.exists(rect_file):
            os.remove(rect_file)


if __name__ == "__main__":
    main()
