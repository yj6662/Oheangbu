namespace Oheangbu.App.World.UI
{
    public sealed partial class PlaytestUiRoot
    {
        bool TryShowOpeningIntroduction()
        {
            if(Session==null||!Session.OpeningIntroductionPending)return false;
            ShowDetail(Session.OpeningIntroductionTitle,Session.OpeningIntroductionText);
            if(Page!="상세")return false;
            if(!Session.TryMarkOpeningIntroductionSeen(out var error))ShowNotice(error,8);
            return true;
        }
    }
}
