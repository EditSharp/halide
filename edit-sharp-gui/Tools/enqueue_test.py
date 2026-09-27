"""Queues a test run for Tools/es_runner_agent.ps1 (which must already be running in an interactive
session) and, by default, waits for it and prints the outcome.

Usage: python Tools/enqueue_test.py [run_tests.py args...] [--wait/--no-wait] [--timeout SECONDS]
Example: python Tools/enqueue_test.py --filter NativeBarMenuTests --windowed
"""
import argparse
import io
import json
import os
import sys
import time
import uuid

# Windows consoles default to cp1252, which chokes on stray unicode in test/tool output
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

QUEUE = r"C:\tmp\es-queue"
RESULTS = r"C:\tmp\es-results"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--wait", dest="wait", action="store_true", default=True)
    parser.add_argument("--no-wait", dest="wait", action="store_false")
    parser.add_argument("--timeout", type=float, default=300)
    args, run_tests_args = parser.parse_known_args()

    os.makedirs(QUEUE, exist_ok=True)
    os.makedirs(RESULTS, exist_ok=True)

    job_id = time.strftime("%Y%m%d-%H%M%S-") + uuid.uuid4().hex[:6]
    job = {"id": job_id, "args": run_tests_args}
    job_path = os.path.join(QUEUE, job_id + ".json")
    with open(job_path, "w", encoding="utf-8") as f:
        json.dump(job, f)
    print(f"queued {job_id}: {' '.join(run_tests_args)}")

    if not args.wait:
        return

    done_path = os.path.join(RESULTS, job_id + ".done")
    deadline = time.time() + args.timeout
    while not os.path.exists(done_path):
        if time.time() > deadline:
            print(f"still running after {args.timeout:.0f}s (is es_runner_agent.ps1 running? check {job_path} and {RESULTS}\\{job_id}.log)")
            return
        time.sleep(1)

    with open(done_path, encoding="utf-8") as f:
        exit_code = f.read().strip()
    log_path = os.path.join(RESULTS, job_id + ".log")
    report_path = os.path.join(RESULTS, job_id, "report.html")
    print(f"finished, exit={exit_code}")
    print(f"log:    {log_path}")
    print(f"report: {report_path}")
    if os.path.exists(log_path):
        with open(log_path, encoding="utf-8", errors="replace") as f:
            lines = f.readlines()
        print("".join(lines[-30:]))


if __name__ == "__main__":
    main()
