using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.BrushRender;
using Oheangbu.Core.Domain;
using Oheangbu.Drawing;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // SPEC-PLAYER-DOSA §4: 실제 Unity 카메라/리본을 사용하는 표현 경계 회귀.
    // 입력 디바이스·인식 재생 검사는 별도다. 이 검사는 입력 이벤트 경계부터 검사한다.
    public static class DrawingProjectionRegression
    {
        [Serializable]
        private sealed class Report
        {
            public string status;
            public int passed;
            public int failed;
            public float maxScreenErrorPixels;
            public List<string> checks = new List<string>();
        }

        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        public static string Run()
        {
            var report = new Report();
            if (!Application.isPlaying)
            {
                report.status = "NOT_RUN: Play Mode required";
                return JsonUtility.ToJson(report);
            }

            var randomState = UnityEngine.Random.state;
            try
            {
                Check(report, "final camera / stationary endpoint / rendered mesh", () => CheckProjection(report));
                Check(report, "same-frame commits / failure context / world detach", () => CheckCommits(report));
                Check(report, "captured ink and clocks / interruption and re-entry", CheckInkAndInterruption);
            }
            finally { UnityEngine.Random.state = randomState; }
            report.status = report.failed == 0 ? "PASS" : "FAIL";
            return JsonUtility.ToJson(report);
        }

        private static void Check(Report report, string label, Action body)
        {
            try
            {
                body();
                report.passed++;
                report.checks.Add("PASS: " + label);
            }
            catch (Exception exception)
            {
                report.failed++;
                report.checks.Add("FAIL: " + label + " — " + exception.GetBaseException().Message);
            }
        }

        private static void CheckProjection(Report report)
        {
            using (var f = new Fixture())
            {
                var points = new[] { new Vector2(80f, 90f), new Vector2(960f, 540f), new Vector2(1820f, 970f) };
                Emit(f.Adapter, "OnStrokeStarted");
                foreach (var point in points) Emit(f.Adapter, "OnStrokePointAdded", point);
                Require(f.Strokes.Count == 0, "Input callback projected before final camera pose");

                // 입력 수집 뒤 카메라 블렌드·FOV 스냅이 일어나는 프레임을 재현한다.
                f.Camera.transform.SetPositionAndRotation(new Vector3(1.2f, 2.1f, -2.5f), Quaternion.Euler(22f, 31f, 0f));
                f.Camera.fieldOfView = 42f;
                Emit(f.Adapter, "LateUpdate");
                Require(f.Strokes.Count == 1 && f.Strokes[0].Data.Points.Count == points.Length, "Missing live stroke samples");
                var stroke = f.Strokes[0];
                Require(stroke.VertexCount > 0, "First-frame mesh was not flushed");
                CheckPoints(report, f.Camera, stroke, points);
                CheckRenderedEndpoint(f.Camera, stroke, points[2]);

                Set(f.Input, "<InDrawMode>k__BackingField", true);
                Set(f.Input, "<HasPointer>k__BackingField", true);
                Set(f.Input, "_currentStroke", new StrokeData());
                Set(f.Input, "<PointerScreenPosition>k__BackingField", points[2] + new Vector2(1.2f, 0.5f));

                // 새 Raw 점이 없는 프레임에도 이미 그린 획과 실제 끝점을 함께 검사한다.
                var rotations = new[] { new Vector3(-80f, 0f, 0f), new Vector3(80f, 160f, 0f), new Vector3(13f, -53f, 0f) };
                foreach (var rotation in rotations)
                {
                    f.Camera.transform.SetPositionAndRotation(new Vector3(-2.4f, 1.6f, -0.2f), Quaternion.Euler(rotation));
                    f.Camera.fieldOfView = 65f;
                    Emit(f.Adapter, "LateUpdate");
                    CheckPoints(report, f.Camera, stroke, points);
                    Require(f.Adapter.TryGetVisualPointer(out Camera camera, out Vector2 screen, out Vector3 world), "Missing visual pointer");
                    Require(camera == f.Camera && screen == points[2], "Active tip did not use the rendered sample endpoint");
                    ScreenError(report, camera, world, points[2]);
                    CheckRenderedEndpoint(camera, stroke, points[2]);
                    var last = stroke.Data.Points[stroke.Data.Points.Count - 1].Position;
                    Require(Vector3.Distance(world, stroke.transform.TransformPoint(last)) < 0.001f, "Tip and live ribbon mapping differ");
                }

                // 수필(EndStroke의 표현 꼬리)은 그대로 두고 획 사이 붓은 현재 포인터를 따른다.
                Set(f.Input, "_currentStroke", null);
                Require(f.Adapter.TryGetVisualPointer(out _, out Vector2 liftedScreen, out _), "Missing lifted pointer");
                Require(liftedScreen == f.Input.PointerScreenPosition, "Lifted brush used stale sampled endpoint");
            }
        }

        private static void CheckCommits(Report report)
        {
            using (var f = new Fixture())
            {
                var first = new[] { new Vector2(260f, 730f), new Vector2(620f, 240f) };
                var second = new[] { new Vector2(1150f, 710f), new Vector2(1580f, 340f) };
                QueueStroke(f.Adapter, first);
                f.Adapter.NotifyCastFailed();
                Emit(f.Adapter, "OnLetterDrawn", Letter('가'));
                Emit(f.Adapter, "OnCommitted", true);
                Emit(f.Adapter, "EvaporateRemaining");
                QueueStroke(f.Adapter, second);
                Emit(f.Adapter, "OnLetterDrawn", Letter('나'));
                Emit(f.Adapter, "OnCommitted", true);
                Emit(f.Adapter, "EvaporateRemaining");
                float commitTime = Time.time;

                f.Camera.transform.SetPositionAndRotation(new Vector3(0.5f, 2f, -3f), Quaternion.Euler(10f, 43f, 0f));
                f.Camera.fieldOfView = 48f;
                Emit(f.Adapter, "LateUpdate");
                var groups = (IList)Get(f.Adapter, "_fading");
                Require(groups.Count == 2 && f.Strokes.Count == 0, "Quick commits merged, disappeared, or stayed live");
                Require(!(bool)GetPublic(groups[0], "IsFlash") && (bool)GetPublic(groups[1], "IsFlash"), "Cast failure leaked across commits");
                var firstStroke = ((List<BrushStrokeRenderer>)GetPublic(groups[0], "Strokes"))[0];
                var secondStroke = ((List<BrushStrokeRenderer>)GetPublic(groups[1], "Strokes"))[0];
                Require(firstStroke.transform.parent == null && secondStroke.transform.parent == null, "Committed stroke was not detached");
                Require(firstStroke.VertexCount > 0 && secondStroke.VertexCount > 0, "Quick-commit mesh waits for another frame");
                Require((float)GetPublic(groups[0], "StartTime") == commitTime, "Commit fade clock changed");
                Require(firstStroke.Data.Points.Count == first.Length + f.Style.TaperSteps, "End/taper event lost or repeated");
                Require(secondStroke.Data.Points.Count == second.Length + f.Style.TaperSteps, "Second end/taper event lost or repeated");
                CheckPoints(report, f.Camera, firstStroke, first);
                CheckPoints(report, f.Camera, secondStroke, second);
                Vector3 detached = secondStroke.transform.TransformPoint(secondStroke.Data.Points[0].Position);
                f.Camera.transform.SetPositionAndRotation(new Vector3(12f, 4f, 7f), Quaternion.Euler(-30f, -100f, 0f));
                Emit(f.Adapter, "LateUpdate");
                Require(Vector3.Distance(detached, secondStroke.transform.TransformPoint(secondStroke.Data.Points[0].Position)) < 0.000001f,
                    "Committed stroke followed the next camera pose");
            }
        }

        private static void CheckInkAndInterruption()
        {
            using (var f = new Fixture())
            {
                float birth = Time.time;
                f.Adapter.InkNormalized = 0.05f;
                Emit(f.Adapter, "OnStrokeStarted");
                Emit(f.Adapter, "OnStrokePointAdded", new Vector2(200f, 400f));
                Emit(f.Adapter, "OnStrokeEnded");
                Emit(f.Adapter, "EvaporateRemaining"); // 피격: 모드를 닫지 않고 다음 획을 시작
                f.Adapter.InkNormalized = 0.09f;
                Emit(f.Adapter, "OnStrokeStarted");
                Emit(f.Adapter, "OnStrokePointAdded", new Vector2(800f, 600f));
                f.Adapter.InkNormalized = 1f; // LateUpdate 직전 잔량 변화가 과거 점에 섞이면 실패
                Emit(f.Adapter, "LateUpdate");

                var groups = (IList)Get(f.Adapter, "_fading");
                Require(groups.Count == 1 && f.Strokes.Count == 1, "Interruption erased or joined the new stroke");
                var interrupted = ((List<BrushStrokeRenderer>)GetPublic(groups[0], "Strokes"))[0].Data.Points[0];
                var active = f.Strokes[0].Data.Points[0];
                Require(Mathf.Abs(interrupted.Ink - f.Style.MaxDensity * f.Style.InkChargeToDensity(0.05f)) < 0.00001f,
                    "Interrupted point used later ink value");
                Require(Mathf.Abs(active.Ink - f.Style.MaxDensity * f.Style.InkChargeToDensity(0.09f)) < 0.00001f,
                    "Live point used later ink value");
                Require(interrupted.Time == birth && active.Time == birth, "Ink birth clock differs from captured frame");
            }
        }

        private static void QueueStroke(BrushStrokeFeedAdapter adapter, Vector2[] points)
        {
            Emit(adapter, "OnStrokeStarted");
            foreach (var point in points) Emit(adapter, "OnStrokePointAdded", point);
            Emit(adapter, "OnStrokeEnded");
        }

        private static DrawnLetter Letter(char letter) => new DrawnLetter(letter, Jamo.Giyeok, Jamo.A, null, 0f, 0f, 0.6f, 0.7f, 2);

        private static void CheckPoints(Report report, Camera camera, BrushStrokeRenderer stroke, Vector2[] points)
        {
            for (int i = 0; i < points.Length; i++)
                ScreenError(report, camera, stroke.transform.TransformPoint(stroke.Data.Points[i].Position), points[i]);
        }

        private static void ScreenError(Report report, Camera camera, Vector3 world, Vector2 expected)
        {
            Vector3 screen = camera.WorldToScreenPoint(world);
            float error = Vector2.Distance(new Vector2(screen.x, screen.y), expected);
            report.maxScreenErrorPixels = Mathf.Max(report.maxScreenErrorPixels, error);
            Require(screen.z > 0f && error <= 0.25f, "Screen projection error " + error + " px at " + expected);
        }

        private static void CheckRenderedEndpoint(Camera camera, BrushStrokeRenderer stroke, Vector2 expected)
        {
            // 기본 리본의 회봉 캡 중심은 실제 마지막 점에 있다. 데이터만 갱신하고 메시를
            // 이전 프레임에 남겨 둔 구현도 검출하도록 GPU 입력 메시 정점을 확인한다.
            float closest = float.MaxValue;
            foreach (var vertex in stroke.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                Vector3 screen = camera.WorldToScreenPoint(stroke.transform.TransformPoint(vertex));
                closest = Mathf.Min(closest, Vector2.Distance(new Vector2(screen.x, screen.y), expected));
            }
            Require(closest <= 0.25f, "Rendered mesh endpoint is stale by " + closest + " px");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static object Get(object target, string field) => target.GetType().GetField(field, PrivateInstance).GetValue(target);
        private static object GetPublic(object target, string field) => target.GetType().GetField(field).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, PrivateInstance).SetValue(target, value);
        private static void Emit(object target, string method, params object[] args) => target.GetType().GetMethod(method, PrivateInstance).Invoke(target, args);

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject _root;
            private readonly RenderTexture _target;
            public readonly Camera Camera;
            public readonly DrawingInputController Input;
            public readonly BrushStrokeFeedAdapter Adapter;
            public readonly BrushStyleSO Style;
            public List<BrushStrokeRenderer> Strokes => (List<BrushStrokeRenderer>)Get(Adapter, "_strokes");

            public Fixture()
            {
                _root = new GameObject("DrawingProjectionRegression") { hideFlags = HideFlags.DontSave };
                _root.SetActive(false);
                var cameraObject = new GameObject("ProjectionCamera");
                cameraObject.transform.SetParent(_root.transform);
                Camera = cameraObject.AddComponent<Camera>();
                Camera.enabled = false;
                _target = new RenderTexture(1920, 1080, 0);
                Camera.targetTexture = _target;
                Camera.fieldOfView = 60f;
                Camera.aspect = 1920f / 1080f;
                Camera.transform.position = new Vector3(0f, 1.6f, -2f);
                var inputObject = new GameObject("ReadOnlyInputState");
                inputObject.transform.SetParent(_root.transform);
                inputObject.SetActive(false); // 입력 장치를 건드리지 않는 상태 스냅샷 대역
                Input = inputObject.AddComponent<DrawingInputController>();
                Style = ScriptableObject.CreateInstance<BrushStyleSO>();
                Adapter = _root.AddComponent<BrushStrokeFeedAdapter>();
                Set(Adapter, "_projectionCamera", Camera);
                Set(Adapter, "_style", Style);
                Set(Adapter, "_input", Input);
                Set(Adapter, "_baseFov", 60f);
                var eye = new GameObject("EyeAnchor");
                eye.transform.SetParent(_root.transform);
                eye.transform.position = new Vector3(0f, 1.6f, 0f);
                Set(Adapter, "_eyeAnchor", eye.transform);
                _root.SetActive(true);
            }

            public void Dispose()
            {
                // 실제 플레이 입력/씬/카메라는 건드리지 않는다. 분리된 획은 어댑터가 정리한다.
                Adapter.enabled = false;
                var fallback = (Material)Get(Adapter, "_fallbackMaterial");
                if (fallback != null) Object.Destroy(fallback);
                Object.Destroy(_root);
                Object.Destroy(Style);
                Object.Destroy(_target);
            }
        }
    }
}
