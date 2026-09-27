"""Read #296 receipts into a compact final-evidence summary.

No Unity calls, captures, asset hashing, scene reads or output writes. JSON goes
to stdout; --compact prints one short status line per check and performance run.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

import build_architecture296_review as review


PERF_KINDS = ("shore", "crossing", "gorge", "gate", "cheolong", "hwanggyeong")
RUNTIME_METRICS = ("PlayStarts", "playStarts", "MoveSamples", "Phase", "phase", "terrainSteps",
                   "terrainAcceptedSite", "terrainMaxLateral", "terrainMaxDeckError", "terrainSupportedSamples",
                   "terrainHazardDeathCount", "surfaceTriangles", "surfaceSubmeshes", "surfaceFormationSamples")


def inspect_checks(folder):
    rows = review.checks_data(folder)
    for row in rows:
        relative = row["expectedPath"]
        path = folder / relative
        if path.suffix == ".txt":
            text = review.read_text(path)
            row["passLines"] = len(re.findall(r"^PASS(?:\s|$)", text, re.M))
            row["failLines"] = len(re.findall(r"^FAIL(?:\s|$)", text, re.M))
            row["failures"] = [line for line in text.splitlines() if line.startswith("FAIL ")]
            if relative == "reproduction-check.txt":
                result = re.search(r"entries=(\d+) changed/missing/added=(\d+)", text)
                if result:
                    row["entries"], row["changedMissingAdded"] = map(int, result.groups())
        else:
            value = review.read_json(path)
            if not isinstance(value, dict):
                continue
            checks = value.get("checks", value.get("Checks"))
            row["recordedChecks"] = len(checks) if isinstance(checks, list) else None
            row["scope"] = value.get("scope", value.get("Scope"))
            if relative.startswith(("Runtime/", "Mum/", "Vehicle/")):
                row["active"] = value.get("Active", value.get("active"))
                row["finished"] = value.get("Finished", value.get("finished"))
                row["restored"] = value.get("Restored", value.get("restored"))
                row["failureCount"] = len(value.get("Failures", value.get("failures", [])))
                row["metrics"] = {key: value[key] for key in RUNTIME_METRICS if key in value}
                if relative == "Vehicle/checks.json":
                    row["vehicle"] = review.vehicle_data(value)
            elif relative == "protection-check.json":
                row["files"] = value.get("files")
    return rows


def inspect_captures(folder):
    # PNG contents can be large and are irrelevant to this status extraction.
    views = (review.read_json(folder / "cameras.json") or {}).get("Views", [])
    latest = max(((folder / name).stat().st_mtime for name in review.GENERATION if (folder / name).is_file()), default=0)
    result = []
    for index, view in enumerate(views):
        row = {"id": view["Id"]}
        metadata = {}
        for stage in ("before", "after"):
            path = folder / "Captures" / stage / f"{index:02d}-{view['Id']}.png"
            row[stage] = path.relative_to(folder).as_posix() if path.is_file() else None
            metadata[stage] = review.read_json(path.with_suffix(".png.json")) or {}
            if stage == "after":
                row["afterStale"] = path.is_file() and path.stat().st_mtime + 2 < latest
        row["samePose"] = bool(row["before"] and row["after"] and
                               review.same_view(metadata["before"].get("View"), metadata["after"].get("View")) and
                               review.same_view(metadata["after"].get("View"), view))
        row["sameTerrainRecorded"] = bool(metadata["before"].get("HeightSHA256") and
                                          metadata["before"].get("HeightSHA256") == metadata["after"].get("HeightSHA256") and
                                          metadata["before"].get("WaterSHA256") == metadata["after"].get("WaterSHA256"))
        result.append(row)
    return {"expected": len(views), "paired": sum(bool(row["before"] and row["after"]) for row in result),
            "samePose": sum(row["samePose"] for row in result), "staleAfter": sum(row["afterStale"] for row in result), "views": result}


def inspect_performance(folder):
    measured = review.performance_data(folder)
    output = []
    for kind in PERF_KINDS:
        rows = [row for row in measured if row["scenario"] == kind]
        if not rows:
            output.append({"kind": kind, "status": "pending", "reason": "No current #296 frame-times receipt"})
            continue
        for row in rows:
            metrics = {key: row.get(key) for key in ("cpuSamples", "cpuMedianMs", "cpuP95Ms", "gpuSamples", "gpuMedianMs", "gpuP95Ms", "frameSamples", "frameMedianMs", "frameP95Ms")}
            provenance = row.get("provenance") or {}
            output.append({"kind": kind, "status": "pending" if row.get("stale") else row["walkStatus"],
                           "walkSummary": row["walkSummary"], "source": row["source"], "environment": row["environment"],
                           "device": row["device"], "modifiedUtc": row["modifiedUtc"], "metrics": metrics,
                           "cpuMeasured": bool(row.get("cpuSamples", 0)), "gpuMeasured": bool(row.get("gpuSamples", 0)),
                           "newerInputs": row.get("newerInputs", []),
                           "provenance": {key: provenance.get(key) for key in
                               ("Scene", "Kind", "Route", "SourcePath", "SourceId", "SourceSha256", "Anchor", "CrossingId", "Start", "End", "Length", "PathSha256", "NoArt")}})
    auxiliaries = [{key: value for key, value in row.items() if key != "raw"} for row in measured if row["scenario"] not in PERF_KINDS]
    return {"expectedKinds": list(PERF_KINDS), "runs": output, "auxiliaryRuns": auxiliaries,
            "scope": "Unity Editor Play at 1920x1080; warm-up, automated CharacterController movement and editor overhead. CPU/GPU unavailability stays null; no standalone 120fps approval."}


def collect(folder):
    rows = inspect_checks(folder)
    landscape = review.read_json(folder / "Landscape/clearance.json") or {}
    surface = review.read_json(folder / "Surface/surface-materials.json") or {}
    legacy = review.read_json(folder / "Legacy/replacements.json") or {}
    sheet = review.read_json(folder / "architecture.json") or {}
    return {"revision": "architecture-296", "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "sourceFolder": str(folder), "scope": "Read-only #296 receipts. Missing/stale evidence stays pending; no raw save payloads, PNG bytes or Unity scene reads.",
            "checkStates": dict(Counter(row["status"] for row in rows)), "checks": rows,
            "counts": {"landscape": {key: landscape.get(key) for key in ("Removed", "RemainingConflicts", "ArenaRemovals", "BridgeRemovals", "GateRemovals", "ReviewedTreeRemovals", "CompoundTreeRemovals")},
                       "surface": {key: surface.get(key) for key in ("GroundMaterials", "NaturalHighlandMaterials", "PreservedHighlandConstructionMaterials")},
                       "legacy": {key: legacy.get(key) for key in ("FoundationStones", "HouseholdTimbers", "RetainedLanternCaps")}},
            "provenance": review.provenance_data({"architecture.json": sheet}),
            "captures": inspect_captures(folder), "performance": inspect_performance(folder),
            "boundedInteriorVisualReview": "Analysis/interior-visual-review.json" if (folder / "Analysis/interior-visual-review.json").is_file() else None}


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compact", action="store_true")
    args = parser.parse_args()
    result = collect(review.DEFAULT)
    if args.compact:
        for row in result["checks"]:
            print(row["status"].upper(), row["expectedPath"], row["summary"])
        for row in result["performance"]["runs"]:
            print("PERF", row["kind"], row["status"].upper(), json.dumps(row.get("metrics"), ensure_ascii=False))
        print("CAPTURES", json.dumps({key: value for key, value in result["captures"].items() if key != "views"}))
    else:
        print(json.dumps(result, ensure_ascii=False, indent=2))
