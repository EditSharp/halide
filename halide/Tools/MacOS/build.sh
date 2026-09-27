#!/bin/sh
# builds the macOS natives next to this script: the context menu host (ship it as
# Contents/MacOS/editsharp-menu) and the dialog library (Contents/Frameworks/
# libeditsharp-dialogs.dylib). a dev run finds both here
set -e
cd "$(dirname "$0")"
clang -fobjc-arc -framework Cocoa -O2 -o editsharp-menu menuhost.m
echo "built $(pwd)/editsharp-menu"
clang -fobjc-arc -framework Cocoa -O2 -dynamiclib -fvisibility=hidden -o libeditsharp-dialogs.dylib dialogs.m
echo "built $(pwd)/libeditsharp-dialogs.dylib"
