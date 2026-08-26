// S2 스파이크: 초성 5자(ㄱㄴㅁㅅㅇ) 템플릿 등록 + 마우스 입력 인식률 측정.
// 스파이크 전용 — 본 게임 모듈(_Project)과 무관하며 검증이 끝나면 폴더째 제거한다.
// PDollar(Assembly-CSharp)를 참조해야 하므로 asmdef 없는 이 폴더에 둔다.
// [이식 수정 2026-08-20] 입력 읽기만 UnityEngine.Input → Input System(Mouse/Keyboard.current)으로 교체 —
// 새 프로젝트가 Input System 전용(Active Input Handling)이라 구 API는 예외를 던진다. 인식·판정 로직은 무수정.
//
// 조작:
//   마우스 왼쪽 드래그 = 획 긋기(여러 획 가능)   C 또는 마우스 오른쪽 = 지우기
//   T = 모드 전환(템플릿 등록 <-> 테스트)
//   [템플릿 모드] 1~5 = 현재 그림을 해당 자모(ㄱㄴㅁㅅㅇ) 템플릿으로 저장
//   [테스트 모드] 1~5 = 지금부터 그릴 자모 선언 -> 그리기 -> Space = 판정·집계
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using PDollarGestureRecognizer;
using UnityEngine;
using UnityEngine.InputSystem;

public class S2_JamoRecognitionTest : MonoBehaviour
{
    private static readonly string[] Classes = { "ㄱ", "ㄴ", "ㅁ", "ㅅ", "ㅇ" };
    private static readonly string[] FileTags = { "giyeok", "nieun", "mieum", "siot", "ieung" };

    private readonly List<Point> _points = new List<Point>();
    private readonly List<LineRenderer> _lines = new List<LineRenderer>();
    private readonly List<Gesture> _templates = new List<Gesture>();

    private int _strokeId = -1;
    private bool _templateMode = true;
    private int _expected = -1;
    private string _lastResult = "";
    private double _lastMs;

    private readonly int[] _ok = new int[Classes.Length];
    private readonly int[] _tried = new int[Classes.Length];

    private Material _lineMaterial;
    private Vector3 _lastSample;

    private string TemplateDir => Path.Combine(Application.dataPath, "Spike", "Templates");

    private void Start()
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
        _lineMaterial = new Material(shader);
        LoadTemplates();
    }

    private void LoadTemplates()
    {
        _templates.Clear();
        if (!Directory.Exists(TemplateDir)) return;
        foreach (string file in Directory.GetFiles(TemplateDir, "*.xml"))
            _templates.Add(GestureIO.ReadGestureFromFile(file));
    }

    private void Update()
    {
        HandleDrawing();
        HandleKeys();
    }

    private void HandleDrawing()
    {
        var mouseDevice = Mouse.current;
        if (mouseDevice == null) return; // 마우스 미연결 환경(원격 등) 보호
        if (mouseDevice.leftButton.wasPressedThisFrame)
        {
            _strokeId++;
            var lr = NewStroke();
            _lines.Add(lr);
            _lastSample = new Vector3(float.MinValue, 0f, 0f);
        }
        if (mouseDevice.leftButton.isPressed)
        {
            Vector3 mouse = mouseDevice.position.ReadValue();
            if (Vector3.Distance(mouse, _lastSample) < 2f) return; // 2px 미만 이동은 무시
            _lastSample = mouse;
            // 인식용 점은 화면 좌표 그대로 사용 (Y만 뒤집어 필기 좌표계와 맞춤)
            _points.Add(new Point(mouse.x, Screen.height - mouse.y, _strokeId));
            var lr = _lines[_lines.Count - 1];
            Vector3 world = Camera.main.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, 1f));
            lr.positionCount++;
            lr.SetPosition(lr.positionCount - 1, world);
        }
        if (mouseDevice.rightButton.wasPressedThisFrame) ClearDrawing();
    }

    private LineRenderer NewStroke()
    {
        var go = new GameObject($"Stroke_{_strokeId}");
        go.transform.SetParent(transform);
        var lr = go.AddComponent<LineRenderer>();
        lr.material = _lineMaterial;
        lr.startColor = lr.endColor = Color.black;
        lr.startWidth = lr.endWidth = 0.005f;
        lr.positionCount = 0;
        lr.useWorldSpace = true;
        return lr;
    }

    private void HandleKeys()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return; // 키보드 미연결 환경 보호
        if (keyboard.cKey.wasPressedThisFrame) ClearDrawing();
        if (keyboard.tKey.wasPressedThisFrame) { _templateMode = !_templateMode; ClearDrawing(); }

        for (int i = 0; i < Classes.Length; i++)
        {
            // 구 KeyCode.Alpha1+i 열거를 Input System의 Key.Digit1+i 인덱서로 그대로 옮김
            if (!keyboard[Key.Digit1 + i].wasPressedThisFrame) continue;
            if (_templateMode) SaveTemplate(i);
            else _expected = i;
        }

        if (!_templateMode && keyboard.spaceKey.wasPressedThisFrame) Classify();
    }

    private void SaveTemplate(int classIndex)
    {
        if (_points.Count < 4) return;
        Directory.CreateDirectory(TemplateDir);
        int n = Directory.GetFiles(TemplateDir, FileTags[classIndex] + "_*.xml").Length + 1;
        string file = Path.Combine(TemplateDir, $"{FileTags[classIndex]}_{n:D2}.xml");
        GestureIO.WriteGesture(_points.ToArray(), Classes[classIndex], file);
        _templates.Add(new Gesture(_points.ToArray(), Classes[classIndex]));
        _lastResult = $"템플릿 저장: {Classes[classIndex]} (총 {_templates.Count}개)";
        ClearDrawing();
    }

    private void Classify()
    {
        if (_points.Count < 4 || _templates.Count == 0) return;
        var sw = Stopwatch.StartNew();
        Result r = PointCloudRecognizer.Classify(new Gesture(_points.ToArray()), _templates.ToArray());
        sw.Stop();
        _lastMs = sw.Elapsed.TotalMilliseconds;

        if (_expected >= 0)
        {
            _tried[_expected]++;
            if (r.GestureClass == Classes[_expected]) _ok[_expected]++;
            _lastResult = $"기대 {Classes[_expected]} -> 인식 {r.GestureClass} (score {r.Score:F2}, {_lastMs:F2}ms)";
        }
        else
        {
            _lastResult = $"인식 {r.GestureClass} (score {r.Score:F2}, {_lastMs:F2}ms) — 1~5로 기대 자모를 먼저 선언하세요";
        }
        ClearDrawing();
    }

    private void ClearDrawing()
    {
        _points.Clear();
        _strokeId = -1;
        foreach (var lr in _lines) if (lr != null) Destroy(lr.gameObject);
        _lines.Clear();
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 900, 30),
            _templateMode
                ? "[템플릿 모드] 드래그로 그리기, 1~5 = ㄱㄴㅁㅅㅇ 템플릿 저장, T = 테스트 모드로"
                : "[테스트 모드] 1~5 = 기대 자모 선언, 드래그로 그리기, Space = 판정, T = 템플릿 모드로");
        GUI.Label(new Rect(10, 35, 900, 30), $"템플릿 {_templates.Count}개 | {_lastResult}");
        if (!_templateMode)
        {
            string exp = _expected >= 0 ? Classes[_expected] : "-";
            int totalOk = _ok.Sum(), totalTried = _tried.Sum();
            string stats = string.Join("  ", Enumerable.Range(0, Classes.Length)
                .Select(i => $"{Classes[i]} {_ok[i]}/{_tried[i]}"));
            GUI.Label(new Rect(10, 60, 900, 30), $"기대: {exp} | {stats} | 전체 {totalOk}/{totalTried}" +
                (totalTried > 0 ? $" ({100.0 * totalOk / totalTried:F0}%)" : ""));
        }
    }
}
