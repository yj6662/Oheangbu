"""Run the 15 existing XML/input casts in three ordinary C2 Play sessions."""
import json
import time
from datetime import datetime, timezone
from pathlib import Path
from capture_ktp_rework import call, OUT


def wait_mode(playing):
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        state = call("Probe")
        if f"playing={str(playing)}" in state:
            return state
        time.sleep(2)
    raise TimeoutError("Unity Play mode transition did not complete")


def main():
    state = call("Probe")
    if "playing=False" not in state or "C2_CodexWorld.unity" not in state:
        raise RuntimeError("Start from stopped saved C2; no scene is opened by this runner")
    rows = []
    report = {
        "startedUtc": datetime.now(timezone.utc).isoformat(),
        "scope": "Three normal Play entries; existing XML strokes, real input/recognition/DI and VFX. No ink injection. No real-target damage, free handwriting, performance or art claim.",
        "batches": rows,
    }
    output = OUT / "player_input_batches16.json"
    for batch in (1, 2, 3):
        try:
            call("Play")
            wait_mode(True)
            time.sleep(2)
            result = json.loads(call(f"PlayerInputBatch{batch}"))
            deadline = time.monotonic() + 105
            while result.get("phase") != "FINISHED":
                if time.monotonic() >= deadline:
                    call("PlayerInputCancel")
                    raise TimeoutError(f"Input batch {batch} did not finish")
                time.sleep(5)
                result = json.loads(call("PlayerInputPoll"))
                print(json.dumps({k: result.get(k) for k in ("batchId", "phase", "completed", "failed", "errors")}), flush=True)
            rows.append({k: result.get(k) for k in ("batchId", "status", "output", "requestedGlyphs", "completed", "failed", "errors", "cleanupStatus", "configuredTargets", "inkBefore", "inkAfter")})
            output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
            if result.get("status") != "PASS_OBSERVED_SELECTED_BATCH_INPUT_CASTS":
                raise RuntimeError(f"Batch {batch} requires inspection: {result.get('output')}")
        finally:
            call("PlayerInputCancel")
            time.sleep(1)
            call("Stop")
            wait_mode(False)
    report["status"] = "PASS_OBSERVED_15_REGISTERED_INPUT_CASTS_ONLY"
    report["finishedUtc"] = datetime.now(timezone.utc).isoformat()
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
