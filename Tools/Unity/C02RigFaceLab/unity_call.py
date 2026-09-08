"""Call the configured Unity MCP bridge without persisting credential material."""
import argparse
import base64
import json
from pathlib import Path
import subprocess
import sys


def call(name, arguments):
    bridge = Path.home() / "AppData/Local/Temp/oheangbu_mcp.py"
    request = base64.b64encode(json.dumps({"name": name, "arguments": arguments}).encode()).decode()
    result = subprocess.run([sys.executable, str(bridge), "--base64", request], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        raise RuntimeError(result.stderr[-1200:])
    return json.loads(result.stdout)


def reflect(method, value=None):
    parameters = [] if value is None else [{"typeName": "System.String", "name": "request", "value": value}]
    return call("reflection-method-call", {
        "filter": {"namespace": "Oheangbu.C02RigFaceLab.Editor", "typeName": "LabEditor", "methodName": method,
                   "inputParameters": [{"typeName": p["typeName"], "name": p["name"]} for p in parameters]},
        "knownNamespace": True, "typeNameMatchLevel": 6, "methodNameMatchLevel": 6,
        "parametersMatchLevel": 2, "executeInMainThread": True, "inputParameters": parameters})


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("method")
    parser.add_argument("request", nargs="?")
    parser.add_argument("--tool", action="store_true")
    args = parser.parse_args()
    value = args.request
    if value and value.startswith("@"):
        value = Path(value[1:]).read_text(encoding="utf-8-sig")
    result = call(args.method, json.loads(value or "{}")) if args.tool else reflect(args.method, value)
    print(json.dumps(result, ensure_ascii=False))
