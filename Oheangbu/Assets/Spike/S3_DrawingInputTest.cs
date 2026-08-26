// S3 스파이크 하네스(SPEC-DRAWING-INPUT §10) — Q 홀드 작도 흐름의 통합 검증 씬용. 검증 후 폴더째 제거.
// 검증 항목: Q 다운→작도 모드 / 합성 글자(초성+중성[+종성]) 분할 인식 / Q 업=커밋 / 미인식=불발 /
//           X=피격 시뮬(글자만 소멸·모드 유지) / 렌더 어댑터를 꺼도 인식 불변(어댑터 GO 비활성으로 확인).
// 결과는 SO 채널(EC_DrawnLetter·EC_DrawMisfire) 구독으로 표시 — 방송 경로까지 통째로 검증한다.
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Drawing;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class S3_DrawingInputTest : MonoBehaviour
{
    [SerializeField] private DrawingInputController _controller;
    [SerializeField] private DrawnLetterEventChannelSO _letterDrawn;
    [SerializeField] private VoidEventChannelSO _misfired;

    private readonly List<string> _log = new List<string>();
    private int _letterCount;
    private int _misfireCount;

    private void OnEnable()
    {
        if (_letterDrawn != null) _letterDrawn.Subscribe(OnLetterDrawn);
        if (_misfired != null) _misfired.Subscribe(OnMisfired);
    }

    private void OnDisable()
    {
        if (_letterDrawn != null) _letterDrawn.Unsubscribe(OnLetterDrawn);
        if (_misfired != null) _misfired.Unsubscribe(OnMisfired);
    }

    private void OnLetterDrawn(DrawnLetter letter)
    {
        _letterCount++;
        AddLog($"「{letter.Letter}」 형(최악) {letter.WorstJamoDistance:F2} · 구조(평균) {letter.AverageDistance:F2} · " +
            $"세 {letter.StrokeDuration:F2}s · 홀드 {letter.HoldDuration:F2}s · {letter.StrokeCount}획");
    }

    private void OnMisfired()
    {
        _misfireCount++;
        AddLog("불발 — 인식 실패(먹만 소모)");
    }

    private void AddLog(string entry)
    {
        _log.Add(entry);
        const int keep = 5; // 화면 표시용 최근 기록 수
        if (_log.Count > keep) _log.RemoveAt(0);
    }

    private void Update()
    {
        // X = 피격 시뮬레이션 — 글자만 소멸·모드 유지(§3-4) 확인용. 정식 연동은 전투 층 몫.
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.xKey.wasPressedThisFrame && _controller != null)
        {
            _controller.InterruptLetter();
            AddLog("피격 시뮬 — 글자 소멸(모드 유지 확인)");
        }
    }

    private void OnGUI()
    {
        string mode = _controller != null && _controller.InDrawMode
            ? $"작도 모드 (획 {_controller.StrokeCount} · 점 {_controller.TotalPointCount})"
            : "대기 — Q를 누르고 있는 동안 작도";
        GUI.Label(new Rect(10, 10, 1200, 24),
            $"[S3 작도 입력] {mode} | 성공 {_letterCount} · 불발 {_misfireCount} | Q 홀드=작도, 떼면 발동 · X=피격 시뮬 · 어댑터 GO 끄면 렌더만 꺼져야 함");
        for (int i = 0; i < _log.Count; i++)
        {
            GUI.Label(new Rect(10, 36 + i * 22, 1200, 22), _log[_log.Count - 1 - i]);
        }
    }
}
