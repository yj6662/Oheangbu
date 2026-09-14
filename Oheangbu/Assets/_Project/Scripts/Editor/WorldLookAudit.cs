using System.IO;
using Oheangbu.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // [SPEC-SPIKE-WORLD-LOOKDEV §5-10] 월드 룩 계측 — reflection-method-call 진입점(전부 public static string, 1호출 = 1프레임).
    // 캡처는 리그 카메라가 아니라 임시 카메라를 고정 포즈에 세워 RT로 찍는다: 플레이 여부·플레이어 위치와 무관하게 결정적이고,
    // PNG는 sRGB RT → ReadPixels라서 화면과 같은 색공간(§6 「색공간 = PNG sRGB」). 파일은 프로젝트 루트 Screenshots/Lookdev(.gitignore).
    // 픽셀 통계(Histogram·LuminanceProbe 등)는 PNG를 파이썬(PIL)으로 읽어 계산한다 — 에디터 안에 통계 코드를 두지 않는다.
    public static class WorldLookAudit
    {
        public const string CaptureFolder = "Screenshots/Lookdev";
        private const float EyeHeight = 1.8f;   // 리그 CameraPivot(0.7) + 캡슐 중심(1.1) — 지면 y 0 기준

        // Spec §5-11 고정 컷 — x, z, yaw, pitch(양수 = 아래)
        private static readonly float[,] Cuts =
        {
            { 0f, -30f, 0f, 0f },     // 1 막장 — 먹 + 광맥 역번짐
            { 0f, -3f, 0f, 0f },      // 2 문턱 — 갱목 실루엣 역광 + 능선 + 소지 하늘(승인 컷 대비)
            { 6f, 14f, -25f, -3f },   // 3 아침 산수 — 원근·먹선 감쇠·하늘 가드·인력 면제
        };

        public static string CaptureCut(int cut, bool postProcessing)
        {
            if (cut < 1 || cut > Cuts.GetLength(0)) return "FAIL: cut 1..3";
            int i = cut - 1;
            return Capture(Cuts[i, 0], EyeHeight, Cuts[i, 1], Cuts[i, 2], Cuts[i, 3], $"cut{cut}_{(postProcessing ? "post" : "raw")}", postProcessing);
        }

        // 임의 포즈 캡처 — 반환 = 절대 경로. 1920x1080 · FOV 60(리그 카메라 동일) · SolidColor 소지(드라이버 팔레트)
        public static string Capture(float x, float y, float z, float yaw, float pitch, string name, bool postProcessing)
        {
            const int width = 1920;
            const int height = 1080;
            var driver = Object.FindFirstObjectByType<WorldLookDriver>();
            if (driver != null) driver.Apply();   // 전역 색·카메라 배경을 이 프레임에 보증(에디터 모드 포함)

            var go = new GameObject("~LookdevCaptureCamera");
            go.hideFlags = HideFlags.HideAndDontSave;
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = driver != null && driver.Palette != null ? driver.Palette.PaperColor : Color.black;
            cam.allowHDR = true;
            go.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(pitch, yaw, 0f));
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = postProcessing;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            string fullPath;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory(CaptureFolder);
                fullPath = Path.GetFullPath(Path.Combine(CaptureFolder, name + ".png"));
                File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(go);
            }
            return fullPath;
        }
    }
}
