using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 설정 page controller (DESIGN §5.5 / §7.8, options.png). Owns the value rows built by PlaytestUiRoot.BuildOptions:
    /// keeps each row's ‹ value › text, the "적용 전" marker (MetaBold20 cinnabar under the name) and the disabled state with its
    /// reason written in the value box ("수직동기화 사용 중") in sync after every step, wires explicit up / down navigation that
    /// skips disabled rows (left / right step the value through MenuOptionStepper304), makes the focused row's value bold
    /// (ValueBold24) and draws the 설명 칸 at x1346 for the focused row from UiStyle304SO.OptionHelp: every value with its meaning
    /// (current value in Serif800 한지), else the summary, then the warning with the cinnabar vertical dry stroke.</summary>
    [DisallowMultipleComponent]
    public sealed class MenuOptionsPanel304 : MonoBehaviour
    {
        public sealed class Row
        {
            public string Label;
            public FocusRow304 Focus;
            public TMP_Text Value, Prev, Next, Reason;
            public Func<string> Read;
            public Action<int> Step;
            public Func<bool> Pending;
            public Func<string> Blocked;
            public bool Focused;
        }

        UiStyle304SO style;
        RectTransform help;
        readonly List<Row> rows = new List<Row>();
        string shownKey;
        Row shownRow;

        [Tooltip("selectable above the first row (the current category tab)")] public Selectable Above;
        [Tooltip("selectable below the last row (the first action)")] public Selectable Below;

        public IReadOnlyList<Row> Rows => rows;
        UiStyle304SO S => style != null ? style : UiStyle304SO.Fallback;

        public void Init(UiStyle304SO s, RectTransform helpRoot) { style = s; help = helpRoot; }

        public Row Add(Row row)
        {
            if (row == null || row.Focus == null) return row;
            rows.Add(row);
            var stepper = row.Focus.Button.gameObject.AddComponent<MenuOptionStepper304>();
            stepper.Step = dir => StepRow(row, dir);
            return row;
        }

        /// <summary>Steps a row (click, Enter, ‹ ›, left / right) and refreshes every row (a step can enable or block another one).</summary>
        public void StepRow(Row row, int dir)
        {
            if (row == null || row.Step == null || !row.Focus.Button.IsInteractable()) return;
            row.Step(dir);
            Refresh();
        }

        /// <summary>Values, 적용 전 markers, disabled states and navigation.</summary>
        public void Refresh()
        {
            foreach (var r in rows)
            {
                if (r.Focus == null || r.Focus.Button == null) continue;
                string reason = r.Blocked != null ? r.Blocked() : null;
                bool blocked = !string.IsNullOrEmpty(reason);
                if (r.Focus.Visual != null && r.Focus.Visual.Interactable == blocked) r.Focus.Visual.SetInteractable(!blocked);
                else if (r.Focus.Visual == null) r.Focus.Button.interactable = !blocked;
                if (r.Value != null) { r.Value.gameObject.SetActive(!blocked); if (!blocked && r.Read != null) r.Value.text = r.Read(); }
                if (r.Prev != null) r.Prev.gameObject.SetActive(!blocked);
                if (r.Next != null) r.Next.gameObject.SetActive(!blocked);
                if (r.Reason != null) { r.Reason.gameObject.SetActive(blocked); if (blocked) r.Reason.text = reason; }
                if (r.Focus.Meta != null) r.Focus.Meta.gameObject.SetActive(!blocked && r.Pending != null && r.Pending());
            }
            Wire();
            shownKey = null;   // the focused row's value may have changed: redraw the 설명 칸
        }

        void Wire()
        {
            Button last = null;
            var live = new List<Button>();
            foreach (var r in rows) if (r.Focus != null && r.Focus.Button != null && r.Focus.Button.interactable) live.Add(r.Focus.Button);
            for (int i = 0; i < live.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnUp = i > 0 ? live[i - 1] : Above;
                nav.selectOnDown = i + 1 < live.Count ? live[i + 1] : Below;
                live[i].navigation = nav;   // left / right stay null: MenuOptionStepper304 steps the value
                last = live[i];
            }
            foreach (var r in rows)
                if (r.Focus != null && r.Focus.Button != null && !r.Focus.Button.interactable)
                    r.Focus.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            if (Below != null && live.Count > 0)
            {
                var nav = Below.navigation;
                if (nav.mode == Navigation.Mode.Explicit) { nav.selectOnUp = last; Below.navigation = nav; }
            }
        }

        void LateUpdate()
        {
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            Row focused = null;
            foreach (var r in rows)
            {
                bool f = sel != null && r.Focus != null && r.Focus.Button != null && sel == r.Focus.Button.gameObject && r.Focus.Button.interactable;
                if (f) focused = r;
                if (f != r.Focused && r.Value != null)
                {
                    r.Focused = f;
                    UiText304.ApplyRole(r.Value, S.Role(f ? UiType304.ValueBold24 : UiType304.Body24), S);
                }
            }
            var target = focused ?? shownRow ?? (rows.Count > 0 ? rows[0] : null);
            string key = target != null ? target.Label + "|" + (target.Read != null ? target.Read() : "") : "";
            if (key == shownKey) return;
            shownKey = key; shownRow = target;
            DrawHelp(target);
        }

        void DrawHelp(Row row)
        {
            if (help == null) return;
            V.Clear(help);
            if (row == null) return;
            var s = S;
            var info = s.Help(row.Label);
            string current = row.Read != null ? row.Read() : "";
            V.Label(s, help, "HelpTitle", row.Label, UiType304.Meta20, s.Mist, 0, 0);
            float y = 34f;
            const float width = 380f;
            if (info != null && !string.IsNullOrEmpty(info.Summary))
            {
                var summary = V.Label(s, help, "HelpSummary", info.Summary, UiType304.Body22, s.Mist, 0, y, width, 0, TextAlignmentOptions.TopLeft, true);
                y += summary.rectTransform.sizeDelta.y + 22f;
            }
            if (info != null && info.Values != null)
                for (int i = 0; i < info.Values.Count; i++)
                {
                    var v = info.Values[i]; if (v == null) continue;
                    bool now = v.Value == current;
                    var name = V.Label(s, help, "HelpValue_" + i, v.Value, now ? UiType304.Title24 : UiType304.Label24, now ? s.Paper : s.Mist, 0, y);
                    y += name.rectTransform.sizeDelta.y + 4f;
                    if (!string.IsNullOrEmpty(v.Meaning))
                    {
                        var meaning = V.Label(s, help, "HelpMeaning_" + i, v.Meaning, UiType304.Meta20, s.Mist, 0, y, width - 20f, 0, TextAlignmentOptions.TopLeft, true);
                        y += meaning.rectTransform.sizeDelta.y;
                    }
                    y += 20f;
                }
            if (info != null && !string.IsNullOrEmpty(info.Warning))
            {
                float wy = Mathf.Max(314f, y + 24f);
                var warn = V.Label(s, help, "HelpWarning", info.Warning, UiType304.Body22, s.Paper, 20, wy, 300f, 0, TextAlignmentOptions.TopLeft, true);
                float th = warn.rectTransform.sizeDelta.y, len = Mathf.Max(70f, th + 12f);
                MenuEdgeStroke304.Draw(s, help, "HelpWarningEdge", 1f, wy + th * .5f, len, 12f, .9f);   // stretched like options.html (110x12)
            }
        }
    }

    /// <summary>Left / right on a focused 설정 row step its value (the row's explicit navigation leaves left / right empty, so the
    /// Selectable does not move focus sideways).</summary>
    [DisallowMultipleComponent]
    public sealed class MenuOptionStepper304 : MonoBehaviour, IMoveHandler
    {
        public Action<int> Step;

        public void OnMove(AxisEventData eventData)
        {
            if (eventData == null || Step == null) return;
            if (eventData.moveDir == MoveDirection.Left) Step(-1);
            else if (eventData.moveDir == MoveDirection.Right) Step(1);
        }
    }
}
