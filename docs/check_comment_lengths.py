"""Reject oversized consecutive source comments while allowing normal explanation."""

from __future__ import annotations

import argparse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COMMENT_LINE_LIMIT = 40
SOURCES = [ROOT / "halide" / "Scripts", ROOT / "tests" / "godot"]


def violations() -> list[str]:
    found = []
    for source in SOURCES:
        for path in source.rglob("*"):
            if path.suffix.lower() not in {".cs", ".py", ".ps1"}:
                continue
            text = path.read_text(encoding="utf-8")
            lines = text.splitlines()
            run_start = None
            run_length = 0
            in_block = False
            block_start = 0
            block_length = 0

            def report(start: int, length: int) -> None:
                if length > COMMENT_LINE_LIMIT:
                    found.append(f"{path.relative_to(ROOT).as_posix()}:{start}: {length} consecutive comment lines (limit {COMMENT_LINE_LIMIT})")

            for number, line in enumerate(lines, 1):
                stripped = line.lstrip()
                is_line_comment = stripped.startswith("//") if path.suffix.lower() == ".cs" else stripped.startswith("#")
                if is_line_comment:
                    if run_start is None:
                        run_start = number
                    run_length += 1
                else:
                    if run_start is not None:
                        report(run_start, run_length)
                    run_start = None
                    run_length = 0

                if path.suffix.lower() == ".cs":
                    if not in_block and "/*" in line:
                        in_block = True
                        block_start = number
                        block_length = 1
                    elif in_block:
                        block_length += 1
                    if in_block and "*/" in line:
                        report(block_start, block_length)
                        in_block = False
                        block_length = 0

            if run_start is not None:
                report(run_start, run_length)
            if in_block:
                report(block_start, block_length)
    return found


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="reserved for consistent CI command style")
    parser.parse_args()
    found = violations()
    if found:
        print("Break up oversized source comments or replace them with focused documentation:")
        print("\n".join(f"  {item}" for item in found))
        return 1
    print(f"source comments are within the {COMMENT_LINE_LIMIT}-line limit")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
