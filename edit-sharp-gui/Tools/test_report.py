"""Builds a self-contained HTML report from a run_tests.py results folder (results.xml + screenshots/).

Usage: python Tools/test_report.py <results-dir> [--out FILE.html] [--open]
  --open  opens the report in the default browser once it's written (Windows/macOS/Linux)
"""
import argparse
import glob
import html
import os
import re
import sys
import webbrowser
import xml.etree.ElementTree as ET


def read_results(results_dir):
    path = os.path.join(results_dir, "results.xml")
    if not os.path.exists(path):
        sys.exit(f"no results.xml in {results_dir}: did the run finish?")
    root = ET.parse(path).getroot()
    suites = []
    for suite in root:
        cases = []
        for case in suite:
            outcome = "pass"
            message = ""
            if case.find("failure") is not None:
                outcome = "fail"
                message = case.find("failure").get("message") or case.find("failure").text or ""
            elif case.find("skipped") is not None:
                outcome = "skip"
                message = case.find("skipped").get("message") or ""
            cases.append({"name": case.get("name"), "time": case.get("time"), "outcome": outcome, "message": message})
        suites.append({"name": suite.get("name"), "cases": cases})
    return suites


def find_screenshots(results_dir, fixture, test):
    shots_dir = os.path.join(results_dir, "screenshots")
    if not os.path.isdir(shots_dir):
        return []
    prefix = f"{fixture}.{test}-"
    return sorted(glob.glob(os.path.join(shots_dir, prefix + "*.png")))


def build(results_dir, out_path):
    suites = read_results(results_dir)
    total = {"pass": 0, "fail": 0, "skip": 0}
    rows = []
    for suite in suites:
        for case in suite["cases"]:
            total[case["outcome"]] += 1
            shots = find_screenshots(results_dir, suite["name"], case["name"])
            shot_tags = "".join(
                f'<a href="screenshots/{html.escape(os.path.basename(s))}" target="_blank">'
                f'<img class="shot" src="screenshots/{html.escape(os.path.basename(s))}"></a>'
                for s in shots
            )
            rows.append(f"""
            <tr class="{case['outcome']}">
              <td>{html.escape(suite['name'])}</td>
              <td>{html.escape(case['name'])}</td>
              <td class="badge">{case['outcome']}</td>
              <td>{case['time']}s</td>
              <td>{html.escape(case['message'])}</td>
              <td>{shot_tags}</td>
            </tr>""")

    page = f"""<!doctype html><html><head><meta charset="utf-8">
<title>EditSharp test results</title>
<style>
body {{ font: 14px/1.5 -apple-system, "Segoe UI", sans-serif; background: #14171d; color: #e5e8ee; margin: 0; padding: 24px; }}
h1 {{ font-size: 20px; }}
.summary {{ display: flex; gap: 16px; margin-bottom: 16px; }}
.summary div {{ padding: 8px 16px; border-radius: 6px; background: #1b1f27; }}
.summary .pass {{ color: #63c08a; }} .summary .fail {{ color: #e06c6c; }} .summary .skip {{ color: #9aa3b2; }}
table {{ border-collapse: collapse; width: 100%; }}
th, td {{ text-align: left; padding: 6px 10px; border-bottom: 1px solid #2b313c; vertical-align: top; }}
tr.fail {{ background: #2a1a1a; }}
.badge {{ text-transform: uppercase; font-size: 11px; font-weight: 600; }}
tr.pass .badge {{ color: #63c08a; }} tr.fail .badge {{ color: #e06c6c; }} tr.skip .badge {{ color: #9aa3b2; }}
.shot {{ height: 60px; border: 1px solid #2b313c; border-radius: 3px; margin-right: 4px; }}
input {{ margin-bottom: 12px; padding: 6px 10px; width: 300px; background: #1b1f27; color: #e5e8ee; border: 1px solid #2b313c; border-radius: 4px; }}
</style></head><body>
<h1>EditSharp test results</h1>
<div class="summary">
  <div class="pass">{total['pass']} passed</div>
  <div class="fail">{total['fail']} failed</div>
  <div class="skip">{total['skip']} skipped</div>
</div>
<input id="filter" placeholder="filter by name..." oninput="filterRows()">
<table id="table"><thead><tr><th>Fixture</th><th>Test</th><th>Outcome</th><th>Time</th><th>Message</th><th>Screenshots</th></tr></thead>
<tbody>{"".join(rows)}</tbody></table>
<script>
function filterRows() {{
  const q = document.getElementById('filter').value.toLowerCase();
  for (const row of document.querySelectorAll('#table tbody tr'))
    row.style.display = row.textContent.toLowerCase().includes(q) ? '' : 'none';
}}
</script>
</body></html>"""

    with open(out_path, "w", encoding="utf-8") as f:
        f.write(page)
    return total


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("results_dir")
    parser.add_argument("--out")
    parser.add_argument("--open", action="store_true")
    args = parser.parse_args()

    out_path = args.out or os.path.join(args.results_dir, "report.html")
    total = build(args.results_dir, out_path)
    print(f"wrote {out_path}: {total['pass']} passed, {total['fail']} failed, {total['skip']} skipped")

    if args.open:
        webbrowser.open("file://" + os.path.abspath(out_path))


if __name__ == "__main__":
    main()
