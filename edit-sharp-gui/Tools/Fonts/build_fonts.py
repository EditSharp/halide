"""Builds the app's fonts from variable sources.

For each source it writes into Fonts/:
  <Name>Variable.ttf      the variable font, every axis kept, with the glyph
                          overlap flags set so a rasteriser knows the
                          contours overlap (a variable font cannot have its
                          overlaps removed: the boolean operation breaks
                          the point compatibility its masters need)
  <Name>-<Weight>.ttf     static instances at the named weights with the
                          overlaps genuinely removed

Needs fonttools and skia-pathops:  python -m pip install --user fonttools skia-pathops

Usage:
  python Tools/Fonts/build_fonts.py <InterVariable.ttf> <JetBrainsMono[wght].ttf>
"""
import os
import sys

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

FONTS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Fonts")

# the weights the palette knows, per family; the mono font only needs the one
WEIGHTS = {
    "Inter": {"Light": 300, "Regular": 400, "Medium": 500, "Bold": 700},
    "JetBrainsMono": {"Regular": 400, "Bold": 700},
}


def family_of(font: TTFont) -> str:
    name = font["name"].getDebugName(16) or font["name"].getDebugName(1) or "Font"
    name = name.replace(" ", "")
    # "Inter Variable" is the family Inter
    return name[:-len("Variable")] if name.endswith("Variable") else name


def build(source: str) -> None:
    font = TTFont(source)
    family = family_of(font)
    axes = {a.axisTag: (a.minValue, a.defaultValue, a.maxValue) for a in font["fvar"].axes}
    print(f"{family}: axes {axes}")

    # the variable font, overlaps flagged rather than removed
    flagged = instancer.instantiateVariableFont(
        TTFont(source), {}, inplace=False, overlap=instancer.OverlapMode.KEEP_AND_SET_FLAGS)
    out = os.path.join(FONTS_DIR, f"{family}Variable.ttf")
    flagged.save(out)
    print(f"  wrote {os.path.relpath(out)} (variable, overlap flags set)")

    # static instances with the overlaps removed
    for weight_name, weight in WEIGHTS.get(family, {"Regular": 400}).items():
        limits = {"wght": weight}
        # text sizes use the smallest optical size when the font has that axis
        if "opsz" in axes:
            limits["opsz"] = axes["opsz"][0]
        static = instancer.instantiateVariableFont(
            TTFont(source), limits, inplace=False, overlap=instancer.OverlapMode.REMOVE,
            updateFontNames=True)
        out = os.path.join(FONTS_DIR, f"{family}-{weight_name}.ttf")
        static.save(out)
        print(f"  wrote {os.path.relpath(out)} (static {weight}, overlaps removed)")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    os.makedirs(FONTS_DIR, exist_ok=True)
    for path in sys.argv[1:]:
        build(path)
