using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    public sealed class WorldLoadingScreen270 : MonoBehaviour
    {
        public static Vector3 RequestedPosition { get; set; }
        public WorldLoadingProfile270 Profile;
        public CanvasGroup Fade { get; private set; }
        public Camera ObservedCamera { get; set; }
        public int RenderedFrames { get; private set; }
        public float Value { get; private set; }
        public string Stage => status != null ? status.text : "";
        public RectTransform Spinner { get; private set; }
        public RawImage Illustration { get; private set; }
        Image fill;
        Text status;
        RectTransform actions;
        float angle;

        void Awake() { Build(); }
        void OnEnable() { RenderPipelineManager.endCameraRendering += CameraRendered; }
        void OnDisable() { RenderPipelineManager.endCameraRendering -= CameraRendered; }
        void CameraRendered(ScriptableRenderContext context, Camera camera) { if (camera == ObservedCamera) RenderedFrames++; }
        void Update() { AdvanceSpinner(Time.unscaledDeltaTime); }
        public void AdvanceSpinner(float unscaledDelta) { angle = (angle - Mathf.Max(0, unscaledDelta) * 72) % 360; if (Spinner != null) Spinner.localRotation = Quaternion.Euler(0, 0, angle); }

        public void Build()
        {
            if (Fade != null || Profile == null) return;
            var root = new GameObject("RegionLoadingCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            Fade = root.AddComponent<CanvasGroup>();
            var page = V.Stretch("LoadingPage", root.transform);
            V.Image(V.Stretch("Black", page), Color.black, null, true);
            Illustration = V.Raw(V.Stretch("RegionIllustration", page), Profile.Fallback, Color.white);
            var crop = Illustration.gameObject.AddComponent<AspectRatioFitter>(); crop.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            V.Image(V.Stretch("Shade", page), new Color(0, 0, 0, .13f));
            var footer = V.Rect("LoadingStatus", page, 96, 0, 0, 150);
            footer.anchorMin = new Vector2(0, 0); footer.anchorMax = new Vector2(1, 0); footer.pivot = Vector2.zero;
            footer.offsetMin = new Vector2(96, 44); footer.offsetMax = new Vector2(-96, 194);
            Color paper = new Color(.94f, .925f, .865f, 1);
            status = V.Text(footer, "CurrentOperation", "지역 불러오는 중", Profile.Font, 22, paper, 0, 46, 1300, 38);
            var shadow = status.gameObject.AddComponent<Outline>(); shadow.effectColor = new Color(0, 0, 0, .8f); shadow.effectDistance = new Vector2(1, -1);
            var track = V.Stretch("ProgressTrack", footer); track.anchorMin = new Vector2(0, 0); track.anchorMax = new Vector2(1, 0); track.pivot = Vector2.zero;
            track.offsetMin = new Vector2(0, 24); track.offsetMax = new Vector2(0, 27);
            V.Image(track, new Color(.94f, .925f, .865f, .22f));
            var progress = V.Stretch("Progress", track); fill = V.Image(progress, paper);
            Spinner = V.Rect("RotatingYinYang", footer, 0, 0, 48, 48); Spinner.anchorMin = Spinner.anchorMax = new Vector2(1, 0);
            Spinner.pivot = new Vector2(1, 0); Spinner.anchoredPosition = new Vector2(0, 50); Spinner.pivot = new Vector2(.5f, .5f); Spinner.anchoredPosition = new Vector2(-24, 74);
            Spinner.gameObject.AddComponent<LoadingYinYang270>().raycastTarget = false;
            actions = V.Rect("FailureActions", footer, 0, 0, 500, 40); actions.anchorMin = actions.anchorMax = new Vector2(0, 1); actions.anchoredPosition = new Vector2(0, 12);
            Select(RequestedPosition); SetProgress(0, "지역 불러오는 중");
        }

        public void Select(Vector3 world)
        {
            if (Illustration == null) return;
            Illustration.texture = Profile.Select(world);
            var texture = Illustration.texture;
            Illustration.GetComponent<AspectRatioFitter>().aspectRatio = texture != null ? texture.width / (float)texture.height : 16f / 9;
        }
        public void SetProgress(float value, string operation)
        {
            Value = Mathf.Max(Value, Mathf.Clamp01(value));
            status.text = operation;
            fill.rectTransform.anchorMax = new Vector2(Value, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        }
        public void Fail(string message, Action retry, Action lobby)
        {
            status.text = message;
            AddAction("다시 시도", 0, retry); AddAction("로비로", 180, lobby);
        }
        void AddAction(string label, float x, Action callback)
        {
            var rect = V.Rect(label, actions, x, 0, 156, 38);
            var image = V.Image(rect, new Color(.15f, .15f, .14f, .95f), null, true);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            V.Text(rect, "Label", label, Profile.Font, 20, Color.white, 0, 0, 156, 38, TextAnchor.MiddleCenter);
            button.onClick.AddListener(() => { button.interactable = false; callback?.Invoke(); });
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LoadingYinYang270 : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); float r = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .48f;
            Color light = new Color(.94f, .925f, .865f, 1), dark = new Color(.09f, .095f, .09f, 1);
            Disc(mesh, Vector2.zero, r, light, 0, 360);
            Disc(mesh, Vector2.zero, r, dark, -90, 180);
            Disc(mesh, Vector2.up * r * .5f, r * .5f, dark, 0, 360);
            Disc(mesh, Vector2.down * r * .5f, r * .5f, light, 0, 360);
            Disc(mesh, Vector2.up * r * .5f, r * .12f, light, 0, 360);
            Disc(mesh, Vector2.down * r * .5f, r * .12f, dark, 0, 360);
        }
        static void Disc(VertexHelper mesh, Vector2 centre, float radius, Color color, float start, float arc)
        {
            int index = mesh.currentVertCount, steps = 64;
            mesh.AddVert(centre, color, Vector2.zero);
            for (int i = 0; i <= steps; i++) { float a = (start + arc * i / steps) * Mathf.Deg2Rad; mesh.AddVert(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color, Vector2.zero); }
            for (int i = 0; i < steps; i++) mesh.AddTriangle(index, index + i + 1, index + i + 2);
        }
    }
}
