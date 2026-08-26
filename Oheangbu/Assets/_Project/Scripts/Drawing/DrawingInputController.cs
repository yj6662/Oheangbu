using System;
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.Drawing
{
    // 작도 입력의 유일한 수집 지점(SPEC-DRAWING-INPUT §6) — Q 홀드 상태머신 + Raw 획 기록.
    // 흐름: 작도 키 다운=모드 진입 → 획 긋기(Raw 기록) → 키 업=커밋(분할 인식 → 성공: 글자 방송 / 실패: 불발).
    // 인식 불가침: 여기 기록되는 Raw가 인식·표현 양쪽의 유일한 원천이다 — 표현(붓 스무딩 등)은
    // 이벤트를 받아 제 계층에서 하고, Raw에는 어떤 보정도 섞지 않는다(레거시 §11 교훈 승계).
    public sealed class DrawingInputController : MonoBehaviour
    {
        // 액션 이름은 식별자(코드), 키 바인딩은 데이터(.inputactions) — 하드코딩 금지 규칙의 분계선
        private const string ActionMapName = "Drawing";
        private const string DrawModeActionName = "DrawMode";
        private const string StrokeActionName = "Stroke";
        private const string PointActionName = "Point";

        [Header("입력 (바인딩=InputSystem_Actions 데이터)")]
        [SerializeField] private InputActionAsset _actions;

        [Header("인식")]
        [SerializeField] private JamoTemplateLibrarySO _templates;
        [Tooltip("인식용 점 샘플 최소 화면 간격(px) — S2·레거시 규약 [TEST]")]
        [SerializeField, Min(0f)] private float _minSamplePixelDistance = 2f;
        [Tooltip("이 점 수 미만으로 그린 것은 그리다 만 것 — 조용히 취소(불발 아님) [TEST]")]
        [SerializeField, Min(0)] private int _minCommitPointCount = 8;

        [Header("방송 채널")]
        [SerializeField] private DrawnLetterEventChannelSO _letterDrawn;   // 커밋 성공
        [SerializeField] private VoidEventChannelSO _misfired;             // 불발(먹 소모 담당자가 구독 예정)
        [SerializeField] private BoolEventChannelSO _modeChanged;          // 작도 모드 on/off

        [Header("감속 [TEST — 전역 상수는 예준 플레이테스트로 박제, 임시로 인스펙터]")]
        [SerializeField, Range(0.05f, 1f)] private float _drawTimeScale = 1f; // 1=감속 없음

        // 표현(어댑터)용 저수준 이벤트 — 같은 프레임의 Raw 사실만 흘린다
        public event Action ModeEntered;
        public event Action ModeExited;
        public event Action StrokeStarted;
        public event Action<Vector2> StrokePointAdded; // 화면 좌표 그대로
        public event Action StrokeEnded;
        public event Action<bool> Committed;            // true=성공(글자 방송됨) / false=불발
        public event Action LetterInterrupted;          // 피격 등으로 글자만 소멸

        private readonly List<StrokeData> _strokes = new List<StrokeData>();
        private readonly RecognitionPipeline _pipeline = new RecognitionPipeline();

        private InputAction _drawMode;
        private InputAction _stroke;
        private InputAction _point;
        private StrokeData _currentStroke;
        private Vector2 _lastSample;
        private float _modeEnterTime;
        private float _cachedTimeScale = 1f;

        public bool InDrawMode { get; private set; }
        public bool IsStroking => _currentStroke != null;
        public int StrokeCount => _strokes.Count;

        public int TotalPointCount
        {
            get
            {
                int count = 0;
                foreach (var s in _strokes) count += s.Points.Count;
                return count;
            }
        }

        private void Awake()
        {
            if (_actions == null)
            {
                Debug.LogError("[Drawing] InputActionAsset 미할당 — 작도 입력 비활성");
                enabled = false;
                return;
            }
            var map = _actions.FindActionMap(ActionMapName, throwIfNotFound: true);
            _drawMode = map.FindAction(DrawModeActionName, throwIfNotFound: true);
            _stroke = map.FindAction(StrokeActionName, throwIfNotFound: true);
            _point = map.FindAction(PointActionName, throwIfNotFound: true);

            if (_templates != null) _pipeline.Initialize(_templates);
            if (!_pipeline.IsReady)
            {
                Debug.LogWarning("[Drawing] 자모 템플릿이 비어 있음 — 커밋은 전부 불발 처리된다");
            }
        }

        private void OnEnable()
        {
            _actions?.FindActionMap(ActionMapName)?.Enable();
        }

        private void OnDisable()
        {
            if (InDrawMode) ExitMode(committed: false);
            _actions?.FindActionMap(ActionMapName)?.Disable();
        }

        private void Update()
        {
            if (_drawMode == null) return;

            if (_drawMode.WasPressedThisFrame() && !InDrawMode) EnterMode();
            if (!InDrawMode) return;

            HandleStroke();

            if (_drawMode.WasReleasedThisFrame()) Commit();
        }

        // 피격=작도 중단(COMBAT-ATTACK LOCKED)의 적용 지점 — 글자만 소멸, 모드는 유지(§3-4).
        // 넘(부동심)의 판정은 호출자(전투 층) 몫: 넘이 켜져 있으면 이 메서드를 부르지 않으면 된다.
        public void InterruptLetter()
        {
            if (!InDrawMode) return;
            EndCurrentStroke();
            _strokes.Clear();
            LetterInterrupted?.Invoke();
        }

        private void EnterMode()
        {
            InDrawMode = true;
            _modeEnterTime = Time.unscaledTime; // 감쇠 구간(§3-2)의 시작점
            if (_drawTimeScale < 1f)
            {
                _cachedTimeScale = Time.timeScale;
                Time.timeScale = _drawTimeScale;
            }
            _modeChanged?.Raise(true);
            ModeEntered?.Invoke();
        }

        private void HandleStroke()
        {
            // 「누른 채 진입」 방어(레거시 실측 교훈): Down 프레임을 못 받아도 눌린 상태면 획 시작으로 취급
            bool pressed = _stroke.IsPressed();
            if (pressed && _currentStroke == null)
            {
                _currentStroke = new StrokeData();
                _strokes.Add(_currentStroke);
                _lastSample = new Vector2(float.MinValue, 0f);
                StrokeStarted?.Invoke();
            }

            if (_currentStroke != null && pressed)
            {
                Vector2 screen = _point.ReadValue<Vector2>();
                if (Vector2.Distance(screen, _lastSample) >= _minSamplePixelDistance)
                {
                    _lastSample = screen;
                    // 세(勢) 측정의 원천이 되는 시각 — 실시간(unscaled)이라 작도 감속과 무관(§3-1)
                    _currentStroke.Add(new StrokePoint(screen, Time.unscaledTime));
                    StrokePointAdded?.Invoke(screen);
                }
            }

            if (_currentStroke != null && _stroke.WasReleasedThisFrame()) EndCurrentStroke();
        }

        private void EndCurrentStroke()
        {
            if (_currentStroke == null) return;
            _currentStroke = null;
            StrokeEnded?.Invoke();
        }

        private void Commit()
        {
            EndCurrentStroke();

            int totalPoints = TotalPointCount;
            if (totalPoints < _minCommitPointCount)
            {
                // 그리다 만 것 — 획이 사실상 없으므로 불발(먹 소모)조차 아니고 조용한 취소
                ExitMode(committed: false, silent: true);
                return;
            }

            var result = _pipeline.Recognize(_strokes, Screen.height);
            char letter = default;
            Jamo initial = default;
            Jamo medial = default;
            Jamo? final = null;
            bool success = result.Success && HangulComposer.TryCompose(
                result.InitialName, result.MedialName, result.FinalName,
                out letter, out initial, out medial, out final);

            if (success)
            {
                float strokeDuration = MeasureStrokeDuration();
                float holdDuration = Time.unscaledTime - _modeEnterTime;
                _letterDrawn?.Raise(new DrawnLetter(letter, initial, medial, final,
                    result.WorstDistance, result.AverageDistance,
                    strokeDuration, holdDuration, _strokes.Count));
            }
            else
            {
                _misfired?.Raise(); // 불발 — 먹만 소모(§3-3). 먹 차감은 구독자(경제 층) 몫
            }

            ExitMode(committed: success);
        }

        // 세(勢) 구간 = 첫 획의 첫 점 → 마지막 획의 마지막 점(§3-1) — 릴리즈 여유는 감쇠가 담당
        private float MeasureStrokeDuration()
        {
            if (_strokes.Count == 0) return 0f;
            return _strokes[_strokes.Count - 1].EndTime - _strokes[0].StartTime;
        }

        private void ExitMode(bool committed, bool silent = false)
        {
            EndCurrentStroke();
            _strokes.Clear();
            InDrawMode = false;
            if (_drawTimeScale < 1f) Time.timeScale = _cachedTimeScale;
            _modeChanged?.Raise(false);
            if (!silent) Committed?.Invoke(committed);
            ModeExited?.Invoke();
        }
    }
}
