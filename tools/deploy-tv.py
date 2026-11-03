#!/usr/bin/env python3
"""Pause capture, rebuild, sign and install the TPK on the TV, then resume.

Required settings (env vars or flags, never committed):
  AMBILIGHT_TV_IP          TV address for Apps2Samsung install/launch
  AMBILIGHT_SIGNING_DIR    Apps2Samsung --data-dir with the saved Partner profile
  AMBILIGHT_CLI            path to apps2samsung-cli (default: on PATH)
  AMBILIGHT_CONTROL_FILE   control-server file (default: /tmp/amb-control)
  AMBILIGHT_TELEMETRY_DIR  telemetry dir containing latest.json

Steps: pause -> wait for paused telemetry -> build -> install -> launch -> run.
The script stops before installing if capture is not confirmed idle.
"""
import argparse
import json
import os
import subprocess
import sys
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP_ID = "dev.ritwickverma.TizenAmbilight"
TPK = os.path.join(REPO, "TizenAmbilight", "bin", "Release", "tizen90",
                   "dev.ritwickverma.TizenAmbilight-1.0.0.tpk")
PAUSED_STATUS = "paused by control channel (no capture calls)"


def run(cmd, **kw):
    print("+ " + " ".join(cmd), flush=True)
    subprocess.run(cmd, check=True, **kw)


def write_control(path, token):
    with open(path, "w", encoding="utf-8") as f:
        f.write(token)


def read_telemetry(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def wait_for_paused(telemetry, timeout):
    deadline = time.time() + timeout
    while time.time() < deadline:
        d = read_telemetry(telemetry)
        if d.get("last_status") == PAUSED_STATUS and d.get("ppi", {}).get("sampler_running") is False:
            return True
        time.sleep(2)
    return False


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--tv", default=os.environ.get("AMBILIGHT_TV_IP"))
    p.add_argument("--signing-dir", default=os.environ.get("AMBILIGHT_SIGNING_DIR"))
    p.add_argument("--cli", default=os.environ.get("AMBILIGHT_CLI", "apps2samsung-cli"))
    p.add_argument("--control-file", default=os.environ.get("AMBILIGHT_CONTROL_FILE", "/tmp/amb-control"))
    p.add_argument("--telemetry-dir", default=os.environ.get("AMBILIGHT_TELEMETRY_DIR"))
    p.add_argument("--skip-build", action="store_true", help="install the existing TPK without rebuilding")
    p.add_argument("--timeout", type=int, default=60, help="seconds to wait for paused telemetry")
    args = p.parse_args()

    missing = [n for n, v in [("--tv / AMBILIGHT_TV_IP", args.tv),
                              ("--signing-dir / AMBILIGHT_SIGNING_DIR", args.signing_dir),
                              ("--telemetry-dir / AMBILIGHT_TELEMETRY_DIR", args.telemetry_dir)] if not v]
    if missing:
        sys.exit("missing settings: " + ", ".join(missing))

    latest = os.path.join(args.telemetry_dir, "latest.json")

    print("Pausing capture...", flush=True)
    write_control(args.control_file, "pause")
    if not wait_for_paused(latest, args.timeout):
        write_control(args.control_file, "run")
        sys.exit("capture did not report paused telemetry; install aborted (control reset to run)")
    print("Capture confirmed idle.", flush=True)

    # On failure the script exits with capture still paused, so the TV stays idle for inspection.
    if not args.skip_build:
        run(["dotnet", "msbuild", os.path.join(REPO, "TizenAmbilight", "TizenAmbilight.csproj"),
             "-restore", "-t:Rebuild", "-p:Configuration=Release", "-v:m"], cwd=REPO)
    if not os.path.isfile(TPK):
        sys.exit("TPK not found: " + TPK)

    run([args.cli, "install", TPK, "--tv", args.tv, "--partner",
         "--data-dir", args.signing_dir, "--keep", "--no-browser"])
    run([args.cli, "launch", APP_ID, "--tv", args.tv, "--data-dir", args.signing_dir])

    print("Resuming capture...", flush=True)
    write_control(args.control_file, "run")
    time.sleep(25)
    d = read_telemetry(latest)
    running = d.get("ppi", {}).get("sampler_running")
    print(f"telemetry: version={d.get('version')} status={d.get('last_status')!r} sampler_running={running}")


if __name__ == "__main__":
    main()
