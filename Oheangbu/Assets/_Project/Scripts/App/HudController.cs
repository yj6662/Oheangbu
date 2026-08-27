using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App
{
    // 프로토 임시 HUD [TEST] — 헌법 화이트리스트 4종 중 3종만: 락온 레티클(그로기 겸) · HP · 먹 미터.
    // 상호작용 프롬프트는 이번 스코프에 대상이 없어 미표시. 추가 UI는 없다 — 이 파일이 전부다.
    // uGUI를 코드로 생성한다(그레이박스 수준) — 정식 HUD(먹병 liquid 등)는 ART-UI 트랙.
    public sealed class HudController : MonoBehaviour
    {
        private RectTransform _hpFill;
        private RectTransform _inkFill;
        private RectTransform _reticle;
        private RectTransform _groggyFill;
        private Canvas _canvas;

        private const float BarWidth = 260f;

        private void Awake()
        {
            var canvasGo = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // HP — 좌하단 붉은기 없는 먹빛 바(색 어휘: 서사=무채)
            _hpFill = CreateBar("HP", new Vector2(40f, 70f), new Color(0.22f, 0.20f, 0.19f, 0.9f));
            // 먹 미터 — HP 아래, 짙은 먹색
            _inkFill = CreateBar("Ink", new Vector2(40f, 40f), new Color(0.10f, 0.09f, 0.09f, 0.9f));

            // 락온 레티클 — 화면상 대상 위치에 뜨는 고리(그로기가 안에서 차오른다 — COMBAT-GROGGY 표시 규칙)
            var reticleGo = new GameObject("Reticle", typeof(Image));
            reticleGo.transform.SetParent(canvasGo.transform, false);
            var ring = reticleGo.GetComponent<Image>();
            ring.color = new Color(0.95f, 0.93f, 0.88f, 0.8f);
            _reticle = reticleGo.GetComponent<RectTransform>();
            _reticle.sizeDelta = new Vector2(46f, 46f);

            var groggyGo = new GameObject("GroggyFill", typeof(Image));
            groggyGo.transform.SetParent(reticleGo.transform, false);
            groggyGo.GetComponent<Image>().color = new Color(0.13f, 0.12f, 0.11f, 0.95f); // 먹이 차오른다
            _groggyFill = groggyGo.GetComponent<RectTransform>();
            _groggyFill.sizeDelta = new Vector2(38f, 38f);
            _groggyFill.localScale = Vector3.zero;

            _reticle.gameObject.SetActive(false);
        }

        private RectTransform CreateBar(string name, Vector2 position, Color color)
        {
            var back = new GameObject(name + "_Back", typeof(Image));
            back.transform.SetParent(_canvas.transform, false);
            back.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);
            var backRect = back.GetComponent<RectTransform>();
            backRect.anchorMin = backRect.anchorMax = Vector2.zero;
            backRect.pivot = new Vector2(0f, 0.5f);
            backRect.anchoredPosition = position;
            backRect.sizeDelta = new Vector2(BarWidth, 16f);

            var fill = new GameObject(name + "_Fill", typeof(Image));
            fill.transform.SetParent(back.transform, false);
            fill.GetComponent<Image>().color = color;
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(BarWidth, 0f);
            fillRect.localScale = Vector3.one;
            return fillRect;
        }

        public void SetHp01(float value)
        {
            if (_hpFill != null) _hpFill.localScale = new Vector3(Mathf.Clamp01(value), 1f, 1f);
        }

        public void SetInk01(float value)
        {
            if (_inkFill != null) _inkFill.localScale = new Vector3(Mathf.Clamp01(value), 1f, 1f);
        }

        public void SetGroggy01(float value)
        {
            if (_groggyFill != null) _groggyFill.localScale = Vector3.one * Mathf.Clamp01(value);
        }

        // 락온 대상 위에 레티클을 고정한다 — 비락온이면 숨김(그로기도 함께 안 보인다: 락온=결투 계약)
        public void UpdateReticle(Transform target, Camera cam)
        {
            bool visible = target != null && cam != null;
            if (_reticle == null) return;
            _reticle.gameObject.SetActive(visible);
            if (!visible) return;

            Vector3 screen = cam.WorldToScreenPoint(target.position + Vector3.up * 1.1f);
            if (screen.z < 0f) { _reticle.gameObject.SetActive(false); return; }
            _reticle.position = screen;
        }
    }
}
