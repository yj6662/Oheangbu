using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>알림 종류 (DESIGN §5.10). Error holds 8 s with the cinnabar edge, Pickup 5 s, the rest 3.6 s.</summary>
    public enum UiNoticeKind304 { Info = 0, Pickup = 1, Error = 2, Realm = 3, Vehicle = 4, Service = 5, Wake = 6 }

    /// <summary>One toast. Seconds &lt;= 0 means "the ToastSpec default for Kind" (resolve with UiStyle304SO.ToastSeconds).</summary>
    [Serializable]
    public struct UiNotice304
    {
        public UiNoticeKind304 Kind;
        [Tooltip("Serif600 24 line, e.g. 석경 조각을 얻었다")] public string Title;
        [Tooltip("Meta 20 line, e.g. 산길 고개의 석경")] public string Source;
        [Tooltip("fragment glyphs (Serif900 50, Rubbing_Lite); at most ToastSpec.MaxGlyphs shown, then +N")] public string Glyphs;
        public float Seconds;

        public UiNotice304(UiNoticeKind304 kind, string title, string source = null, string glyphs = null, float seconds = 0)
        { Kind = kind; Title = title ?? ""; Source = source ?? ""; Glyphs = glyphs ?? ""; Seconds = seconds; }

        /// <summary>Visible glyphs (max `max`, whitespace / separators dropped) and the "+N" overflow label ("" when none).</summary>
        public static string SplitGlyphs(string glyphs, int max, out string overflow)
        {
            overflow = ""; if (string.IsNullOrEmpty(glyphs)) return "";
            var shown = new StringBuilder(); int count = 0;
            foreach (char c in glyphs)
            {
                if (char.IsWhiteSpace(c) || c == ',' || c == '·' || c == '/') continue;
                if (count < max) shown.Append(c);
                count++;
            }
            if (count > max) overflow = "+" + (count - max);
            return shown.ToString();
        }
    }

    /// <summary>SO event channel for the quiet toast stack (IMPLEMENTATION §7.2). Senders call Raise; the one Toast304Stack
    /// under the HUD subscribes to Raised (and drains Pending on subscribe). Not a singleton: reference the asset
    /// Assets/_Project/Art/UI/UI304/UiNoticeChannel.asset. Subscribers must unsubscribe in OnDisable; all listeners and the
    /// pending buffer are cleared on SubsystemRegistration because the editor keeps SO state across Play sessions.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/UI/UI304 Notice Channel", fileName = "UiNoticeChannel")]
    public sealed class UiNoticeChannelSO : ScriptableObject
    {
        public event Action<UiNotice304> Raised;
        [Tooltip("notices raised while nobody listens are kept (oldest dropped) until a stack drains them")]
        [Min(0)] public int PendingLimit = 8;
        [NonSerialized] readonly List<UiNotice304> pending = new List<UiNotice304>();
        [NonSerialized] UiNotice304 last;
        [NonSerialized] int raisedCount;

        public bool HasListeners => Raised != null;
        public int PendingCount => pending.Count;
        /// <summary>Most recent notice (harness / capture checks).</summary>
        public UiNotice304 Last => last;
        public int RaisedCount => raisedCount;

        public void Raise(UiNotice304 notice)
        {
            notice.Title ??= ""; notice.Source ??= ""; notice.Glyphs ??= "";
            last = notice; raisedCount++;
            var handler = Raised;
            if (handler != null) { handler(notice); return; }
            if (PendingLimit <= 0) return;
            if (pending.Count >= PendingLimit) pending.RemoveAt(0);
            pending.Add(notice);
        }

        public void Raise(UiNoticeKind304 kind, string title, string source = null, string glyphs = null, float seconds = 0)
            => Raise(new UiNotice304(kind, title, source, glyphs, seconds));

        /// <summary>Hands every buffered notice to `into` (in order) and clears the buffer. Returns how many.</summary>
        public int DrainPending(Action<UiNotice304> into)
        {
            if (into == null || pending.Count == 0) return 0;
            var copy = pending.ToArray(); pending.Clear();
            foreach (var n in copy) into(n);
            return copy.Length;
        }

        public void ClearListeners() { Raised = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAll()
        {
            foreach (var channel in Resources.FindObjectsOfTypeAll<UiNoticeChannelSO>())
            { channel.Raised = null; channel.pending.Clear(); channel.last = default; channel.raisedCount = 0; }
        }
    }
}
