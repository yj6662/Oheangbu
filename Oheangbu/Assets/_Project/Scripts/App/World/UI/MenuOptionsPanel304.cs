using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 설정 page controller (DESIGN §5.5 / §7.8, options.png). Owns the value rows built by PlaytestUiRoot.BuildOptions:
    /// keeps each row's ‹ value › text, the "적용 전" marker (MetaBold20 cinnabar under the name) and the disabled state with its
    /// reason written in the value box ("수직동기화 사용 중") in sync after every step, wires explicit up / down navigation that
    /// skips disabled rows (left / right step the value through MenuOptionStepper304) and makes the focused row's value bold
    /// (ValueBold24). D308-27 answer 3 (SPEC-PLAYTEST-TEXT-DIET): the 설명 칸 at x1346 is gone - UiStyle304SO.OptionHelp is no
    /// longer read here. ShownWhilePending = the 화면 tab's "what runs now" block, shown only while a row is 적용 전 (설정 ③).</summary>
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
        readonly List<Row> rows = new List<Row>();

        [Tooltip("selectable above the first row (the current category tab)")] public Selectable Above;
        [Tooltip("selectable below the last row (the first action)")] public Selectable Below;
        [Tooltip("shown only while a row is pending (the display tab: what runs now); null = none")] public GameObject ShownWhilePending;

        public IReadOnlyList<Row> Rows => rows;
        UiStyle304SO S => style != null ? style : UiStyle304SO.Fallback;

        public void Init(UiStyle304SO s) { style = s; }

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
            bool anyPending = false;
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
                bool pending = !blocked && r.Pending != null && r.Pending();
                if (r.Focus.Meta != null) r.Focus.Meta.gameObject.SetActive(pending);
                anyPending |= pending;
            }
            if (ShownWhilePending != null && ShownWhilePending.activeSelf != anyPending) ShownWhilePending.SetActive(anyPending);
            Wire();
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
            foreach (var r in rows)
            {
                bool f = sel != null && r.Focus != null && r.Focus.Button != null && sel == r.Focus.Button.gameObject && r.Focus.Button.interactable;
                if (f != r.Focused && r.Value != null)
                {
                    r.Focused = f;
                    UiText304.ApplyRole(r.Value, S.Role(f ? UiType304.ValueBold24 : UiType304.Body24), S);
                }
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
