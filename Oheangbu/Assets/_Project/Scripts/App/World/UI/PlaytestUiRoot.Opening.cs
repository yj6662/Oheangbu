namespace Oheangbu.App.World.UI
{
    public sealed partial class PlaytestUiRoot
    {
        // No forced full-screen exposition on spawn — "해설 컷신 없음" (헌법 안티-비전 7 / SPEC-PLAYTEST-TEXT-DIET).
        // The intro is retired to a silent state advance so the opening flow still proceeds; the era's mood is
        // carried by 방(榜)·소문 world objects (NARR:182), not a popup. Returns false so the caller shows only the
        // brief control hint. The objective banner stays for now until 광맥 색-안내 is placed (사용자 확정 2026-09-19).
        bool TryShowOpeningIntroduction()
        {
            if(Session==null||!Session.OpeningIntroductionPending)return false;
            if(!Session.TryMarkOpeningIntroductionSeen(out var error))ShowNotice(error,8);
            return false;
        }
    }
}
