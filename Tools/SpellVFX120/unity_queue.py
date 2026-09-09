"""Submit a named VFX120 Editor operation; credentials are not used by this local queue."""
import argparse
import json
from pathlib import Path
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/SpellVFX120"


def submit(method, request=None, wait=20):
    OUT.mkdir(parents=True, exist_ok=True)
    command = OUT / "command.json"
    if command.exists():
        raise RuntimeError("An unconsumed command already exists; poll its response instead")
    token = uuid.uuid4().hex
    temp = OUT / ("command_" + token + ".pending")
    temp.write_text(json.dumps({"id": token, "method": method, "request": request}), encoding="utf-8")
    temp.replace(command)
    response = OUT / ("response_" + token + ".json")
    end = time.monotonic() + wait
    while time.monotonic() < end:
        if response.exists():
            raw = response.read_text(encoding="utf-8")
            if len(raw) < 4000:
                print(raw)
            else:
                payload = json.loads(raw)
                result = payload.get("result", "")
                try:
                    inner = json.loads(result)
                    payload["result"] = {k: v for k, v in inner.items()
                        if not isinstance(v, (dict, list))}
                except (TypeError, ValueError):
                    payload["result"] = str(result)[:1500]
                payload["fullResponseFile"] = str(response)
                print(json.dumps(payload, ensure_ascii=False))
            return
        time.sleep(.25)
    print(json.dumps({"status": "PENDING", "response": str(response)}))


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("method")
    p.add_argument("request", nargs="?")
    p.add_argument("--wait", type=float, default=20)
    a = p.parse_args()
    request = a.request
    if request and request.startswith("@"):
        request = Path(request[1:]).read_text(encoding="utf-8-sig")
    submit(a.method, request, a.wait)
