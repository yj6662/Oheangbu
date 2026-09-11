"""Capture curated KTP phases from C2 using the existing single Unity editor queue."""
import argparse
import json
import time
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/SpellVFX120"


def call(method, request=None):
    command = OUT / "command.json"
    if command.exists():
        raise RuntimeError("An unconsumed Unity command exists")
    token = uuid.uuid4().hex
    pending = OUT / (token + ".pending")
    pending.write_text(json.dumps({"id": token, "method": method,
                                   "request": json.dumps(request) if request else None}), encoding="utf-8")
    pending.replace(command)
    response = OUT / ("response_" + token + ".json")
    print(json.dumps({"method": method, "response": str(response)}), flush=True)
    deadline = time.monotonic() + 120
    while not response.exists():
        if time.monotonic() >= deadline:
            raise TimeoutError(str(response))
        time.sleep(1)
    result = json.loads(response.read_text(encoding="utf-8-sig"))
    if result["status"] != "COMPLETE":
        raise RuntimeError(result)
    return result["result"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", default="03")
    parser.add_argument("--external", action="store_true")
    parser.add_argument("--clip", action="store_true")
    parser.add_argument("--indices", default="0,15,18,24,39,42,48,63,66,72,87,90,96,111,114")
    args = parser.parse_args()
    for index in [int(value) for value in args.indices.split(",")]:
        folder = "KTP" + ("External" if args.external else "Integrated") + ("Frames" if args.clip else "Stills") + args.version
        request = dict(start=index, count=1, folder=folder, width=1280, height=720,
                       frames=5, clip=args.clip, fps=24, duration=0, demonstrationCues=True)
        if args.external:
            shield = index in (18, 42, 66, 90, 114)
            request.update(externalCamera=True, hideTemporaryTargets=True,
                           cameraPosition=dict(x=3.8, y=2.8, z=4 if shield else 8),
                           cameraTarget=dict(x=0, y=1, z=0 if shield else 4))
        print(call("Capture", request), flush=True)
        deadline = time.monotonic() + 180
        while True:
            time.sleep(2)
            result = json.loads(call("CapturePoll"))
            if result["status"] == "COMPLETE":
                break
            if result["status"] != "RUNNING" or time.monotonic() >= deadline:
                raise RuntimeError(result)
        print(json.dumps({"index": index, "folder": folder, "complete": True}), flush=True)


if __name__ == "__main__":
    main()
