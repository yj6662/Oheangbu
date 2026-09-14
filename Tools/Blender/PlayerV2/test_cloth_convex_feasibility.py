"""Known closed-form bounds and rigid-frame invariance for the SOCP audit."""
import json
import subprocess
import sys
import tempfile
from pathlib import Path
import numpy as np

out = Path(tempfile.gettempdir()) / "DosaClothConvexContract"
out.mkdir(exist_ok=True)
tool = Path(__file__).with_name("audit_cloth_convex_feasibility.py")
rest = np.array([[0., 0., 0.], [1., 0., 0.], [2., 0., 0.]])
edges = np.array([[0, 1], [1, 2]])
# Two pinned endpoints and one free point with an independent motion ball.
poses = np.array([rest, [[0, 0, 0], [1.5, 0, 0], [3, 0, 0]],
                  [[0, 0, 0], [.5, 0, 0], [2, 0, 0]]])
names = ["rest", "stretched_endpoints", "free_ball_bottleneck"]
expected = [1., 1.5, 1.4]
mobility = np.array([0., .1, 0.])
rotation = np.array([[0., -1., 0.], [0., 0., -1.], [1., 0., 0.]])
results = []
for label, r, p in [("identity", rest, poses), ("rigid_frame", rest @ rotation.T + [30, 12, -7], poses @ rotation.T + [30, 12, -7])]:
    dataset = out / (label + ".npz")
    np.savez(dataset, rest=r, edges=edges, mobility=mobility, posePoints=p, poseNames=names)
    report = out / (label + ".json")
    subprocess.run([sys.executable, str(tool), str(dataset), "--poses", *names, "--out", str(report)], check=True, capture_output=True, text=True)
    cases = json.loads(report.read_text())["cases"]
    for row, wanted in zip(cases, expected):
        assert row["solverStatus"] == "optimal"
        assert abs(row["minimizedMaximumStretch"] - wanted) < 1e-6, row
        assert row["maximumPinErrorMeters"] < 1e-7, row
        assert row["maximumMotionBallExcessMeters"] < 1e-7, row
        assert row["withinInitialStretchLimitAndResiduals"] == (wanted <= 1.35), row
        results.append({"frame": label, "case": row["pose"], "expected": wanted, "measured": row["minimizedMaximumStretch"]})
summary = {"status": "PASS_CLOSED_FORM_CONTRACT_ONLY", "cases": results, "assertions": 30}
(out / "contract-report.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps(summary, indent=2))
