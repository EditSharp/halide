#!/bin/sh
# builds the macOS context menu host next to this script. ship the binary as
# Contents/MacOS/editsharp-menu in the app bundle; a dev run finds it here
set -e
cd "$(dirname "$0")"
clang -fobjc-arc -framework Cocoa -O2 -o editsharp-menu menuhost.m
echo "built $(pwd)/editsharp-menu"
