using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Oheangbu.Presentation;

namespace Oheangbu.EditorTools
{
    /// <summary>Pure-math validation. No scene, model or gameplay state changes.</summary>
    public static class DosaArmRollValidation
    {
        [Serializable] private sealed class Bone { public string name = null; public float[] localToWorld = null; }
        [Serializable] private sealed class Row { public float[] rowMajor = null; }
        [Serializable] private sealed class Skin { public string name = null; public Bone[] bones = null; public Row[] bindposes = null; }
        [Serializable] private sealed class Snapshot { public Skin[] skins = null; }
        [Serializable] private sealed class Sweep
        {
            public int framesPerSecond, samples;
            public float maximumUpperIncrementDegrees, maximumForearmIncrementDegrees, maximumJointDegrees;
            public float maximumHandErrorDegrees, maximumAxisPositionErrorM;
            public bool continuousWithinConfiguredRate;
        }
        [Serializable] private sealed class Report
        {
            public string status, scope, sourceSnapshot;
            public bool disabledUnchanged;
            public PlayerArmRollSolver.Result actual153;
            public float actual153AxisErrorM, maximumRigidBasisErrorDegrees;
            public Sweep[] sweeps;
        }
        public static string Run()
        {
            string source = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../Screenshots/PlayerDosaV2/static-drawing-20260908_101710_685/153_circle_wrist_snapshot.json"));
            var snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(source));
            Skin skin = Array.Find(snapshot.skins, s => s.name == "DosaV2_ArmLining_Right");
            var map = new Dictionary<string, int>();
            for (int i = 0; i < skin.bones.Length; i++) map.Add(skin.bones[i].name, i);
            int pi = map["RightShoulder"], ui = map["RightArm"], fi = map["RightForeArm"], hi = map["RightHand"];
            Matrix4x4 p = Matrix(skin.bones[pi].localToWorld), u = Matrix(skin.bones[ui].localToWorld);
            Matrix4x4 f = Matrix(skin.bones[fi].localToWorld), h = Matrix(skin.bones[hi].localToWorld);
            var bind = new PlayerArmRollSolver.BindPose
            {
                UpperLocal = (Matrix(skin.bindposes[pi].rowMajor) * Matrix(skin.bindposes[ui].rowMajor).inverse).rotation,
                ForearmLocal = (Matrix(skin.bindposes[ui].rowMajor) * Matrix(skin.bindposes[fi].rowMajor).inverse).rotation,
                HandLocal = (Matrix(skin.bindposes[fi].rowMajor) * Matrix(skin.bindposes[hi].rowMajor).inverse).rotation
            };
            Vector3 ua = (Vector3)f.GetColumn(3) - (Vector3)u.GetColumn(3), fa = (Vector3)h.GetColumn(3) - (Vector3)f.GetColumn(3);
            PlayerArmRollSolver.State state = default;
            var settings = PlayerArmRollSolver.Settings.CandidateDefault;
            var disabled = PlayerArmRollSolver.Evaluate(p.rotation, u.rotation, f.rotation, h.rotation, ua, fa, bind, ref state, settings, 1f, 1f / 60f);
            var report = new Report
            {
                sourceSnapshot = source,
                scope = "Pure orientation math and synthetic continuous hand-roll sweep based on actual frame153. No actual skinned-mesh/native-cloth/all-pose visual PASS is implied.",
                disabledUnchanged = !disabled.Applied && Quaternion.Angle(disabled.Upper, u.rotation) < .001f
                    && Quaternion.Angle(disabled.Forearm, f.rotation) < .001f && Quaternion.Angle(disabled.Hand, h.rotation) < .001f
            };
            settings.Enabled = true;
            report.actual153 = PlayerArmRollSolver.Evaluate(p.rotation, u.rotation, f.rotation, h.rotation, ua, fa, bind, ref state, settings, 1f, 1f / 60f);
            report.actual153AxisErrorM = AxisError(u.rotation, f.rotation, ua, fa, report.actual153);
            var sweeps = new List<Sweep>();
            foreach (int fps in new[] { 30, 60, 120 })
            {
                state = default; var row = new Sweep { framesPerSecond = fps, continuousWithinConfiguredRate = true };
                PlayerArmRollSolver.Result previous = default;
                for (int frame = 0; frame <= 2 * fps; frame++)
                {
                    Quaternion target = Quaternion.AngleAxis(-180f + 360f * frame / (2 * fps), fa.normalized) * h.rotation;
                    var result = PlayerArmRollSolver.Evaluate(p.rotation, u.rotation, f.rotation, target, ua, fa, bind, ref state, settings, 1f, 1f / fps);
                    row.maximumHandErrorDegrees = Mathf.Max(row.maximumHandErrorDegrees, Quaternion.Angle(target, result.Hand));
                    row.maximumAxisPositionErrorM = Mathf.Max(row.maximumAxisPositionErrorM, AxisError(u.rotation, f.rotation, ua, fa, result));
                    row.maximumJointDegrees = Mathf.Max(row.maximumJointDegrees, Mathf.Max(result.AfterJointDegrees.x, Mathf.Max(result.AfterJointDegrees.y, result.AfterJointDegrees.z)));
                    if (frame > 0)
                    {
                        float du = Quaternion.Angle(previous.Upper, result.Upper), df = Quaternion.Angle(previous.Forearm, result.Forearm);
                        row.maximumUpperIncrementDegrees = Mathf.Max(row.maximumUpperIncrementDegrees, du);
                        row.maximumForearmIncrementDegrees = Mathf.Max(row.maximumForearmIncrementDegrees, df);
                        row.continuousWithinConfiguredRate &= du <= settings.MaximumRollDegreesPerSecond / fps + .05f
                            && df <= settings.MaximumRollDegreesPerSecond / fps + .05f;
                    }
                    previous = result; row.samples++;
                }
                sweeps.Add(row);
            }
            report.sweeps = sweeps.ToArray();
            for (int i = 0; i < 27; i++)
            {
                Quaternion basis = Quaternion.Euler(i * 13f, i * 29f, i * 7f); state = default;
                var result = PlayerArmRollSolver.Evaluate(basis * p.rotation, basis * u.rotation, basis * f.rotation, basis * h.rotation,
                    basis * ua, basis * fa, bind, ref state, settings, 1f, 1f / 60f);
                report.maximumRigidBasisErrorDegrees = Mathf.Max(report.maximumRigidBasisErrorDegrees,
                    Quaternion.Angle(Quaternion.Inverse(basis) * result.Upper, report.actual153.Upper),
                    Quaternion.Angle(Quaternion.Inverse(basis) * result.Forearm, report.actual153.Forearm));
            }
            bool pass = report.disabledUnchanged && report.actual153AxisErrorM < .00001f
                && report.actual153.AfterJointDegrees.z < 90f && report.actual153.AfterJointDegrees.y < 100f
                && report.maximumRigidBasisErrorDegrees < .25f;
            foreach (var row in report.sweeps) pass &= row.continuousWithinConfiguredRate && row.maximumHandErrorDegrees < .05f && row.maximumAxisPositionErrorM < .00001f;
            report.status = pass ? "PASS_MATH_ONLY" : "FAIL_MATH";
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/PlayerDosaV2/wrist-roll-math.json"));
            File.WriteAllText(output, JsonUtility.ToJson(report, true)); return output;
        }
        private static float AxisError(Quaternion u, Quaternion f, Vector3 ua, Vector3 fa, PlayerArmRollSolver.Result r) =>
            Mathf.Max((r.Upper * Quaternion.Inverse(u) * ua - ua).magnitude, (r.Forearm * Quaternion.Inverse(f) * fa - fa).magnitude);
        private static Matrix4x4 Matrix(float[] row)
        { Matrix4x4 m = new Matrix4x4(); for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) m[r, c] = row[r * 4 + c]; return m; }
    }
}
