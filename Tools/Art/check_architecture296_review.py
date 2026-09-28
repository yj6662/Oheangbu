"""Offline report-contract checks. Does not launch Unity or generate the review page."""
from __future__ import annotations

import copy
import json
import os
import re
import subprocess
import tempfile
from datetime import datetime, timezone
from html.parser import HTMLParser
from pathlib import Path

import build_architecture296_review as review


def run():
    checks = []

    def check(name, passed, evidence=None):
        checks.append({"name": name, "passed": bool(passed), "evidence": evidence})

    check("missing check is pending", review.status_json(None)[0] == "pending")
    complete = {"Status": "PASS", "Finished": True, "Restored": True, "Active": False, "Failures": [], "Checks": ["observed"]}
    check("runtime requires completed restored nonempty evidence", review.status_json(complete, True)[0] == "pass" and
          all(review.status_json({**complete, **change}, True)[0] != "pass" for change in
              [{"Finished": False}, {"Restored": False}, {"Active": True}, {"Checks": []}, {"Failures": ["failed"]}, {"Status": "FAIL"}, {"Status": ""}]))
    check("explicit failure dominates text successes", review.status_text("PASS first\nFAIL second")[0] == "fail")
    check("missing credits are unknown", review.provenance_data({})["meshyRecordedCredits"] is None and
          review.provenance_data({"architecture.json": {"Sources": [{"MeshyCredits": 0}, {}]}})["meshyRecordedCredits"] is None)
    check("recorded zero credits remain zero", review.provenance_data({"architecture.json": {"Sources": [{"MeshyCredits": 0}]}})["meshyRecordedCredits"] == 0)
    vehicle = review.vehicle_data({"Scope": "fixture", "Drives": [{"Id": "closed", "Pass": True, "WheelSamples": 30, "BridgeWheelSamples": 0}, {"Id": "bridge", "Pass": False, "WheelSamples": 20, "BridgeWheelSamples": 4}]})
    check("vehicle receipt retains real per-drive outcomes and wheel support", vehicle["driveCount"] == 2 and
          vehicle["passedDrives"] == 1 and vehicle["failedDrives"] == 1 and vehicle["drives"][1]["BridgeWheelSamples"] == 4)
    data = review.collect(review.DEFAULT)
    document = review.render(data)
    complex_data = review.read_json(review.DEFAULT / "Complex/validation.json")
    check("current source-preserving native schema supported", review.status_complex(complex_data)[0] == "pass",
          review.status_complex(complex_data)[1])
    damaged = copy.deepcopy(complex_data)
    if isinstance(damaged, dict) and damaged.get("assets"):
        damaged["assets"][0]["reducedLevels"][0]["invalidIndices"] = 1
    check("native index failure rejected", review.status_complex(damaged)[0] == "fail")
    check("current provenance explicitly shows license and budget", bool(data["provenance"]["sources"]) and
          "Meshy 원장 사용량" in document and "100 크레딧" in document and "CC0" in document and "EULA" in document,
          {"sources": data["provenance"]["sourceCount"], "recordedCredits": data["provenance"]["meshyRecordedCredits"]})
    check("manual approval is independent", "수동 미술 검토 대기" in document or
          bool((data["documents"]["art-review.json"] or {}).get("reviewer")))

    with tempfile.TemporaryDirectory(prefix="architecture296-review-") as temp:
        folder = Path(temp)
        (folder / "checks.txt").write_text("PASS prior result\n", encoding="utf-8")
        (folder / "crossings.json").write_text("{}", encoding="utf-8")
        os.utime(folder / "checks.txt", (100, 100))
        os.utime(folder / "crossings.json", (200, 200))
        stale = next(row for row in review.checks_data(folder) if row["expectedPath"] == "checks.txt")
        check("new generated input invalidates old PASS", stale["status"] == "pending" and bool(stale["newerInputs"]))
        perf = folder / "Perf/shore"
        history = perf / "History/old"
        history.mkdir(parents=True)
        text = "CPU FrameTiming: samples=0 median_ms=0 p95_ms=0\nGPU FrameTiming: samples=0 median_ms=0 p95_ms=0\n"
        (perf / "frame-times.txt").write_text(text, encoding="utf-8")
        (history / "frame-times.txt").write_text(text, encoding="utf-8")
        rows = review.performance_data(folder)
        check("old performance runs excluded and zero samples unknown", len(rows) == 1 and
              rows[0]["cpuMedianMs"] is None and rows[0]["gpuMedianMs"] is None)
        js = re.findall(r"<script>(.*?)</script>", document, re.S)
        script = folder / "review.js"
        script.write_text("\n".join(js), encoding="utf-8")
        result = subprocess.run(["node", "--check", str(script)], capture_output=True, text=True, encoding="utf-8")
        check("inline browser script parses", result.returncode == 0, result.stderr.strip())

    class Links(HTMLParser):
        def __init__(self):
            super().__init__()
            self.paths = []

        def handle_starttag(self, tag, attrs):
            for name, value in attrs:
                if name in ("href", "src") and value and not value.startswith(("http:", "https:", "#", "data:")):
                    self.paths.append(value)

    links = Links()
    links.feed(document)
    outputs = {"review-data.json", "Perf/review-summary.csv"}
    missing = sorted(set(p for p in links.paths if p not in outputs and not (review.DEFAULT / p).is_file()))
    check("evidence and source links resolve locally", not missing, missing)
    check("current pair metadata is exposed without inferred approval", all("samePose" in row and "afterStale" in row for row in data["captures"]),
          {"views": len(data["captures"]), "paired": sum(bool(r["before"] and r["after"]) for r in data["captures"]),
           "samePose": sum(r["samePose"] for r in data["captures"]), "staleAfter": sum(r["afterStale"] for r in data["captures"])})
    return {"passed": all(c["passed"] for c in checks), "utc": datetime.now(timezone.utc).isoformat(),
            "scope": "Offline report parsing, freshness, missing evidence, provenance, local links and JavaScript syntax. No visual/browser/Unity approval.",
            "generatorSha256": review.sha(Path(review.__file__)), "checks": checks,
            "currentStatuses": [{"path": row["expectedPath"], "status": row["status"], "summary": row["summary"]} for row in data["checks"]]}


if __name__ == "__main__":
    result = run()
    output = review.DEFAULT / "Analysis/review-generator-checks.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{sum(c['passed'] for c in result['checks'])}/{len(result['checks'])} PASS; {output}")
    for row in result["checks"]:
        if not row["passed"]:
            print("FAIL", row["name"], row["evidence"])
    raise SystemExit(0 if result["passed"] else 1)
