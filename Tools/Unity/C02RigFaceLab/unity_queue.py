"""Submit one scoped Editor request; survive Unity MCP domain-reload reconnect delays."""
import argparse
import json
from pathlib import Path
import time
import uuid

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Art/PlayerPhase1/C02_RigFaceLab/Unity"


def submit(method, request=None, wait_seconds=40):
    OUT.mkdir(parents=True, exist_ok=True)
    command = OUT / "command.json"
    if command.exists():
        raise RuntimeError("An unconsumed Unity experiment command already exists")
    token = uuid.uuid4().hex
    temporary = OUT / ("command_" + token + ".pending")
    temporary.write_text(json.dumps({"id": token, "method": method, "request": request}), encoding="utf-8")
    temporary.replace(command)
    response = OUT / ("response_" + token + ".json")
    deadline = time.monotonic() + wait_seconds
    while time.monotonic() < deadline:
        if response.exists():
            try:
                result = json.loads(response.read_text(encoding="utf-8"))
                print(json.dumps(result, ensure_ascii=False))
                return result
            except (OSError, json.JSONDecodeError):
                pass
        time.sleep(0.2)
    print(json.dumps({"status": "PENDING", "id": token, "response": str(response)}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("method", choices=["RootHeightAudit", "Probe", "Import", "BuildReview", "OpenReview", "TestFiveWeights", "StartCapture", "CalibrateReview", "TestCandidateSkinQuality", "Play", "Stop"])
    parser.add_argument("request", nargs="?")
    parser.add_argument("--wait", type=float, default=40)
    args = parser.parse_args()
    request = args.request
    if request and request.startswith("@"):
        request = Path(request[1:]).read_text(encoding="utf-8-sig")
    submit(args.method, request, args.wait)
