using System;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    // #306 contract between the session (track S builds requests from content data) and the dialogue view (track U: one Elden
    // Ring style surface — speaker name + subtitle line at the bottom centre, then a talk menu; SPEC-PLAYTEST-306 #3).
    // One interaction = one surface (text-one-surface rule): the prompt is hidden while this is open. No instruction text.
    //
    // Semantics both sides rely on:
    // - Show(r) while already open replaces the content in place (follow-up line after a menu pick) without a second Pause.Begin.
    // - F advances a page; F on the last page opens the menu when Services is non-empty, otherwise closes. Esc = Leave = close.
    // - Trade / Upgrade / Maintain are opened by the view itself (UI-owned windows, Id = the existing shop/station id) and return
    //   to the menu when that window closes; Chosen is still invoked first so the session can log or refuse.
    // - Talk / CommissionAccept / CommissionDecline / CommissionReport / Rest: the view invokes Chosen; the session answers
    //   synchronously inside Chosen by raising a follow-up request (shown in place) or not. With no follow-up, Talk returns to
    //   the menu and the others close the view. Rest: the session starts the rest presentation after the view closed.
    // - Closed is invoked exactly once per opened conversation, after the surface is gone: the view calls the Closed of the request
    //   showing at that moment (a replaced request's Closed is dropped). The session raises every request, follow-ups included,
    //   through WorldMacroPlaytestSession.DialogueRequested, which also raises DialogueEnded(SourceId) from that Closed.
    public enum DialogueServiceKind306 { Talk, Trade, Upgrade, Rest, Maintain, CommissionAccept, CommissionDecline, CommissionReport, Leave }

    [Serializable]
    public sealed class DialogueService306
    {
        public DialogueServiceKind306 Kind;
        public string Label = "";
        public string Id = "";
        public bool Enabled = true;
    }

    public sealed class DialogueRequest306
    {
        public string SourceId = "";                                            // interaction point id (logs, re-entry cooldown, NPC actor match)
        public Vector3 SourcePosition;                                          // the point's world position (NPC actor match)
        public string Speaker = "";                                             // name line; empty = object or narrator text (no name)
        public string Header = "";                                              // optional small line above (commission: "의뢰 · 제목 · 사례 조선통보 N")
        public string[] Lines = Array.Empty<string>();                         // pages: F advances; after the last page the menu opens or the view closes
        public DialogueService306[] Services = Array.Empty<DialogueService306>(); // talk menu entries after the lines; the view appends Leave
        public Action<DialogueService306> Chosen;                               // a menu entry was picked
        public Action Closed;                                                   // the conversation ended (F on the last page, Esc, Leave)
    }

    public interface IDialogueSurface306
    {
        bool IsOpen { get; }
        bool Show(DialogueRequest306 request);
        void Close();
    }
}
