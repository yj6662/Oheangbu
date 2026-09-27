"""Run one serialized #296 verification phase. Root owns invocation of this CLI.

Usage: python Tools/Unity/verify_architecture296.py --phase edit|play|performance
Stops on failure/timeout; never aborts, retries or starts the next Unity command.
Raw service responses stay in Art/PlaytestPolish/Unity; stdout is summary only.
"""
import argparse, contextlib, io, json, re, sys, time
from concurrent.futures import ThreadPoolExecutor, TimeoutError as FutureTimeout
from datetime import datetime, timezone
from pathlib import Path
from playtest_polish import call, OUT as SERVICE

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Art/World/Compact/Rebuild/Architecture296"
TYPE = "Oheangbu.EditorTools.WorldMacro.CompactArchitecture296"
PHASES = {
    "edit": [("check", "checks.txt", "text"), ("gate-mobility", "gate-mobility.txt", "text"),
             ("nav", "navigation.txt", "text"), ("nav-diagnose", "nav-diagnostics.txt", "traversal"),
             ("walk", "walk-first-visit.txt", "traversal"), ("culling", "culling.txt", "text")],
    "play": [("runtime:start", "Runtime/checks.json", "runtime"),
             ("mum296:start-hyeongang", "Mum/hyeongang-play-checks.json", "runtime"),
             ("vehicle:start", "Vehicle/checks.json", "runtime")],
    "performance": [("perf:" + kind, "Perf/" + kind + "/walk-play.txt", "perf")
                    for kind in ("shore", "crossing", "gorge", "gate", "cheolong", "hwanggyeong")],
}


def utc(): return datetime.now(timezone.utc).isoformat()
def signature(path): return (path.stat().st_mtime_ns, path.stat().st_size) if path.is_file() else None
def read_json(path):
    try: return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError): return None
def fresh(path, before, started):
    current = signature(path)
    return bool(current and current != before and current[0] >= started)
def failed(text): return bool(re.search(r"\b(?:FAIL|TIMEOUT|ERROR)\b", text))
def short_failure(text): return next((line[:500] for line in text.splitlines() if failed(line)), text[:200])
def flags(data): return {k.lower(): v for k, v in data.items()}


class Runner:
    def __init__(self, folder=OUT, caller=call):
        self.folder, self.caller, self.console = folder, caller, sys.stdout
        self.path = folder / "Analysis/final-verification-progress.json"
        self.ledger = read_json(self.path) or {"version": 1, "runs": []}
        self.run, self.row, self.last_print = {}, {}, 0

    def save(self):
        self.ledger["updatedUtc"] = utc()
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temp = self.path.with_suffix(".pending")
        temp.write_text(json.dumps(self.ledger, ensure_ascii=False, indent=2), encoding="utf-8")
        temp.replace(self.path)

    def progress(self, message, force=False):
        if force or time.monotonic() - self.last_print >= 60:
            self.last_print = time.monotonic()
            self.row["progress"] = message
            self.save()
            print(message, file=self.console, flush=True)

    def invoke(self, command, deadline):
        def captured():
            with contextlib.redirect_stdout(io.StringIO()):
                return self.caller(TYPE, "Run", command, max(.1, deadline - time.monotonic()))
        with ThreadPoolExecutor(max_workers=1) as pool:
            future = pool.submit(captured)
            while True:
                left = deadline - time.monotonic()
                if left <= 0: raise TimeoutError(command + ": service deadline; inspect Unity before any next command")
                try: reply = future.result(timeout=min(2, left)); break
                except FutureTimeout: self.progress(command + ": waiting for Unity response")
        if reply.get("id"):
            self.row.setdefault("responses", []).append(str(SERVICE / ("response_" + reply["id"] + ".json")))
        text = str(reply.get("result", ""))
        if reply.get("status") != "COMPLETE" or reply.get("error") or failed(text):
            raise RuntimeError(command + ": " + short_failure(str(reply.get("error") or text)))
        return text

    def wait_result(self, command, path, kind, before, started, deadline):
        state_path = self.folder / "traversal-status.json"
        while time.monotonic() < deadline:
            if fresh(path, before, started):
                if kind in ("runtime", "traversal"):
                    source = state_path if kind == "traversal" else path
                    data = read_json(source) if signature(source) and source.stat().st_mtime_ns >= started else None
                    state = flags(data) if isinstance(data, dict) else {}
                    if kind == "traversal" and state.get("command") != command: state = {}
                    if state.get("finished") is True and state.get("restored") is True and state.get("active") is False:
                        checks = state.get("checks", [])
                        if state.get("status") != "PASS" or state.get("error") or state.get("failures"):
                            raise RuntimeError(command + ": completed/restored with failure " + str(state.get("status")))
                        if kind == "runtime" and not checks: raise RuntimeError(command + ": no completed checks")
                        if kind == "runtime": return "PASS; finished/restored; checks=" + str(len(checks))
                        text = path.read_text(encoding="utf-8-sig")
                    else:
                        self.progress(command + ": " + str(state.get("status", "waiting for fresh completion")))
                        time.sleep(2); continue
                else: text = path.read_text(encoding="utf-8-sig")
                if failed(text) or not re.search(r"^PASS\b", text, re.M):
                    raise RuntimeError(command + ": " + short_failure(text))
                if kind == "perf" and not re.search(r"\bplay=False\b", self.invoke("status", deadline)):
                    self.progress(command + ": result complete; waiting for Edit restoration")
                    time.sleep(2); continue
                return "PASS; " + str(len(re.findall(r"^PASS\b", text, re.M))) + " result lines"
            self.progress(command + ": waiting for fresh result and restoration")
            time.sleep(2)
        raise TimeoutError(command + ": unfinished at deadline; no next command; inspect/restore Unity manually")

    def execute(self, phase):
        self.run = {"phase": phase, "startedUtc": utc(), "status": "RUNNING", "commands": []}
        self.ledger["runs"].append(self.run)
        try:
            for command, relative, kind in PHASES[phase]:
                path = self.folder / relative
                started = time.time_ns(); before = signature(path)
                deadline = time.monotonic() + (1800 if phase == "play" else 300 if phase == "performance" else 900)
                self.row = {"command": command, "startedUtc": utc(), "startedNs": started,
                            "resultPath": relative, "previousSignature": before, "status": "RUNNING"}
                self.run["commands"].append(self.row)
                self.progress(command + ": start", True)
                status = self.invoke("status", deadline)
                if "play=False" not in status or "Architecture296/" not in status:
                    raise RuntimeError(command + ": candidate Edit scene required; " + status[:240])
                self.invoke(command, deadline)
                summary = self.wait_result(command, path, kind, before, started, deadline)
                self.row.update(status="PASS", finishedUtc=utc(), resultSignature=signature(path), summary=summary)
                self.progress(command + ": " + summary, True)
            self.run.update(status="PASS", finishedUtc=utc()); self.save()
            return True
        except Exception as error:
            self.row.update(status="STOPPED", error=str(error), stoppedUtc=utc())
            self.run.update(status="STOPPED", error=str(error), stoppedUtc=utc())
            self.progress(self.row.get("command", phase) + ": STOPPED; " + str(error), True)
            return False


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--phase", choices=PHASES, required=True)
    args = parser.parse_args()
    raise SystemExit(0 if Runner().execute(args.phase) else 1)
