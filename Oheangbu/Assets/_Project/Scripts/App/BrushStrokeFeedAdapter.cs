using System.Collections.Generic;
using Oheangbu.BrushRender;
using Oheangbu.Drawing;
using UnityEngine;

namespace Oheangbu.App
{
    // 단일 Raw 입력 → 표현(BrushRender) 어댑터(SPEC-DRAWING-INPUT §6의 Render Adapter).
    // DrawingInputController의 저수준 이벤트만 구독해 획 렌더러에 점을 공급한다 —
    // 인식 파이프라인과는 어떤 데이터도 공유하지 않으므로(이벤트=같은 원천의 복사),
    // 이 컴포넌트를 통째로 꺼도 인식은 그대로다(인식 불가침의 어댑터 층 증명).
    public sealed class BrushStrokeFeedAdapter : MonoBehaviour
    {
        [SerializeField] private DrawingInputController _input;
        [SerializeField] private Material _strokeMaterial;                // 비우면 Sprites/Default 폴백(스파이크)
        [SerializeField, Min(0.01f)] private float _surfaceDistance = 1f; // 카메라 앞 작도면 거리 [TEST]
        [SerializeField, Min(0f)] private float _baseWidth = 0.01f;       // 획 기본 폭(미감 [TEST])

        private readonly List<BrushStrokeRenderer> _strokes = new List<BrushStrokeRenderer>();
        private BrushStrokeRenderer _current;
        private Material _fallbackMaterial;

        private void OnEnable()
        {
            if (_input == null) return;
            _input.StrokeStarted += OnStrokeStarted;
            _input.StrokePointAdded += OnStrokePointAdded;
            _input.StrokeEnded += OnStrokeEnded;
            _input.ModeExited += ClearAll;
            _input.LetterInterrupted += ClearAll;
        }

        private void OnDisable()
        {
            if (_input == null) return;
            _input.StrokeStarted -= OnStrokeStarted;
            _input.StrokePointAdded -= OnStrokePointAdded;
            _input.StrokeEnded -= OnStrokeEnded;
            _input.ModeExited -= ClearAll;
            _input.LetterInterrupted -= ClearAll;
            ClearAll();
        }

        private void OnStrokeStarted()
        {
            var go = new GameObject($"BrushStroke_{_strokes.Count}");
            go.transform.SetParent(transform, false);
            _current = go.AddComponent<BrushStrokeRenderer>();
            _current.Configure(GetStrokeMaterial());
            _strokes.Add(_current);
        }

        private void OnStrokePointAdded(Vector2 screen)
        {
            if (_current == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, _surfaceDistance));
            _current.AddPoint(transform.InverseTransformPoint(world), _baseWidth, 1f); // 농도 변화는 미감 스파이크 몫
        }

        private void OnStrokeEnded()
        {
            if (_current == null) return;
            _current.EndStroke();
            _current = null;
        }

        // 커밋·불발·피격 어느 경로로 끝나도 화면의 획은 정리한다(소멸 연출은 미감 스파이크에서)
        private void ClearAll()
        {
            foreach (var stroke in _strokes)
            {
                if (stroke != null) Destroy(stroke.gameObject);
            }
            _strokes.Clear();
            _current = null;
        }

        private Material GetStrokeMaterial()
        {
            if (_strokeMaterial != null) return _strokeMaterial;
            if (_fallbackMaterial == null)
            {
                // 스파이크 폴백 — 정식 셰이더(Noise Cutout)는 SPEC-SPIKE-INK-LOOKDEV에서
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
                _fallbackMaterial = new Material(shader);
            }
            return _fallbackMaterial;
        }
    }
}
