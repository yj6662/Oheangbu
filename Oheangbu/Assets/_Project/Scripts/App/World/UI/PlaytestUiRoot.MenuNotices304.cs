using System;
using System.Text.RegularExpressions;
using UnityEngine;
using V=Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    // #304 알림 routing (IMPLEMENTATION §7.2). Every sender goes through Menu304Notify -> UiStyle304SO.Notices (UiNoticeChannelSO)
    // -> MenuToastStack304 on the menu canvas ("Notice" root). Senders: ShowNotice (legacy text), OnCollected (석경 사건 카드),
    // Session.NoticeRaised (filtered below), RefreshSaveFailureIcon (save errors), and Menu304Notify callers.
    public sealed partial class PlaytestUiRoot
    {
        /// <summary>Raises one notice on the style's channel (the toast stack listens); without a channel asset the stack is fed
        /// directly. mirror = also write the hidden legacy noticeText (older checks read it) and keep the Notice root active.</summary>
        public void Menu304Notify(UiNotice304 notice,bool mirror=true)
        {
            if(mirror)
            {
                if(noticeText!=null)noticeText.text=string.IsNullOrEmpty(notice.Source)?notice.Title:notice.Title+"\n"+notice.Source;
                if(noticeRoot!=null){noticeRoot.gameObject.SetActive(true);noticeRoot.SetAsLastSibling();}
            }
            if(Theme==null)return;
            var s=V.Style(Theme);
            if(s.Notices!=null)
            {
                if(menu304Toasts!=null)menu304Toasts.Bind(s,Menu304ToastsWait,Menu304Drawing);
                s.Notices.Raise(notice);
            }
            else if(menu304Toasts!=null)menu304Toasts.Enqueue(notice);
        }

        /// <summary>Session.NoticeRaised carries every short interaction line too (they belong to the HUD prompt surface: one text
        /// surface per interaction), so only system events pass: recovered saves, an accepted commission,
        /// the south gate / boss result, the escort vehicle, recovered coins and taken bundles (습득 기록), and errors. Hints and
        /// "이미 ~" lines never become toasts (지시문 금지).</summary>
        void Menu304OnSessionNotice(string message)
        {
            if(!Menu304ClassifySessionNotice(message,out var kind,out string title,out string source))return;
            Menu304Notify(new UiNotice304(kind,title,source));
        }

        /// <summary>The Session.NoticeRaised filter above as a pure rule: true (with the toast's kind, title, source) when the line
        /// becomes a toast. The wake line is false here (the death presentation raises it on the channel itself).</summary>
        static bool Menu304ClassifySessionNotice(string message,out UiNoticeKind304 kind,out string title,out string source)
        {
            kind=UiNoticeKind304.Info;title="";source="";
            if(string.IsNullOrEmpty(message))return false;
            string m=message.Trim();
            var coins=Regex.Match(m,@"^조선통보\s+([0-9,]+)\s+회수\.?$");
            if(coins.Success){kind=UiNoticeKind304.Pickup;title="조선통보 회수 ×"+coins.Groups[1].Value;return true;}
            // the wake line ("마지막 쉼터에서 눈을 떴다 …") is raised by the death presentation once its veil lifts (HUD area)
            if(m.StartsWith("마지막 쉼터에서 눈을 떴다",StringComparison.Ordinal))return false;
            if(m.Contains("진행을 복구했다"))kind=UiNoticeKind304.Info;
            else if(m.Contains("의뢰를 확인했다"))kind=UiNoticeKind304.Info;
            else if(m.Contains("남문이 열렸다")||m.Contains("장수를 물리쳤다"))kind=UiNoticeKind304.Realm;
            else if(m.Contains("호송 쉼터에서"))kind=UiNoticeKind304.Vehicle;
            else if(m.EndsWith("챙겼다.",StringComparison.Ordinal)||m.EndsWith("챙겼다",StringComparison.Ordinal))kind=UiNoticeKind304.Pickup;
            else if(m.Contains("확인할 수 없다")||m.Contains("다시 시도한다"))kind=UiNoticeKind304.Error;
            else return false;
            Menu304SplitSentence(m,out title,out source);
            return true;
        }

        /// <summary>#304 integration (HUD prompt, one surface per line): true when a live root turns this Session.NoticeRaised line
        /// into a toast, so WorldMacroPlaytestHudPresenter leaves it off the prompt while it is the session's LastFeedback.</summary>
        public static bool Menu304RoutesToToast(string message)
        {
            var root=Instance;
            if(root==null||!root.isActiveAndEnabled||!root.bound||root.Theme==null||string.IsNullOrEmpty(message))return false;
            // asked every HUD frame while the line is LastFeedback (7 s): classify each distinct line once (no per-frame Regex)
            if(!ReferenceEquals(message,root.menu304RouteText)){root.menu304RouteText=message;root.menu304RouteResult=Menu304ClassifySessionNotice(message,out _,out _,out _);}
            return root.menu304RouteResult;
        }
        string menu304RouteText;bool menu304RouteResult;

        // ------------------------------------------------------------------ ShowNotice kinds (QA #304-2)
        // The last 석경 bundle OnCollected announced: a ShowNotice "석경 조각을 얻었다" right after it takes the same glyphs and
        // source, so the two collapse into one card (MenuToastStack304.IsDuplicate) instead of an event card + a light line.
        string menu304LastBundleGlyphs="",menu304LastBundleTitle="";float menu304LastBundleAt=-100f;
        const float Menu304BundleGlyphWindow=10f;
        static readonly string[] Menu304ErrorWords={"못했다","수 없다","실패","오류","손상","확인해 주세요","다시 시도","준비되지 않았다","유효하지 않"};
        static readonly string[] Menu304VehicleWords={"자동차","마법가마","오행부를 집어넣었다"};

        void Menu304RememberBundle(string glyphs,string title)
        {
            menu304LastBundleGlyphs=glyphs??"";menu304LastBundleTitle=title??"";menu304LastBundleAt=Time.unscaledTime;
        }

        /// <summary>Kind of a legacy ShowNotice line, from its text. Only real errors are Error: the line carries LastError or the
        /// session's SaveError (Flow / Loading set LastError right before they call ShowNotice), matches the error vocabulary, or
        /// is a raw non-Korean exception message shown for 8 s. Then the Session.NoticeRaised rules (복구 / 의뢰 / 남문 / 호송 /
        /// 챙겼다 / 통보 회수), vehicle lines, collection lines (…얻었다 = Pickup); everything else is Info.</summary>
        UiNoticeKind304 Menu304NoticeKind(string text,float seconds)
        {
            string m=(text??"").Trim();
            if(m.Length==0)return UiNoticeKind304.Info;
            if(Menu304Carries(m,LastError)||Menu304Carries(m,Session!=null?Session.SaveError:null))return UiNoticeKind304.Error;
            if(Menu304ClassifySessionNotice(m.Replace('\n',' '),out var kind,out _,out _))return kind;
            foreach(var w in Menu304VehicleWords)if(m.Contains(w))return UiNoticeKind304.Vehicle;
            if(m.Contains("얻었다"))return UiNoticeKind304.Pickup;
            foreach(var w in Menu304ErrorWords)if(m.Contains(w))return UiNoticeKind304.Error;
            if(Theme!=null&&seconds*1000f>=V.Style(Theme).Motion.ToastHoldErrorMs&&!Menu304HasHangul(m))return UiNoticeKind304.Error;
            return UiNoticeKind304.Info;
        }
        static bool Menu304Carries(string text,string error)
        {
            if(string.IsNullOrEmpty(error))return false;
            string e=error.Trim();
            return e.Length>=4&&text.Contains(e);
        }
        static bool Menu304HasHangul(string text)
        {
            foreach(char c in text)if(c>='가'&&c<='힣')return true;
            return false;
        }

        /// <summary>Glyphs for a 석경 pickup line when known: the bundle whose title is the source line
        /// (WorldMacroCollectionCatalog), else the bundle OnCollected announced in the last few seconds (then its title becomes
        /// the source too, so both senders collapse into one card). Other pickups (통보, 소재) have none: the light record.</summary>
        string Menu304PickupGlyphs(string title,ref string source)
        {
            if(string.IsNullOrEmpty(title)||title.IndexOf("석경",StringComparison.Ordinal)<0)return null;
            string src=(source??"").Trim();
            if(src.Length>0)
                foreach(var bundle in WorldMacroCollectionCatalog.AllBundles)
                {
                    if(bundle==null||!string.Equals(bundle.Title,src,StringComparison.Ordinal)||bundle.Fragments==null)continue;
                    var sb=new System.Text.StringBuilder();
                    foreach(var f in bundle.Fragments)if(f!=null)sb.Append(f.Letter);
                    return sb.ToString();
                }
            if(Time.unscaledTime-menu304LastBundleAt<=Menu304BundleGlyphWindow&&menu304LastBundleGlyphs.Length>0
               &&(src.Length==0||string.Equals(src,menu304LastBundleTitle,StringComparison.Ordinal)))
            {
                source=menu304LastBundleTitle;
                return menu304LastBundleGlyphs;
            }
            return null;
        }

        /// <summary>"A.\nB" -> title "A", source "B" (legacy ShowNotice text).</summary>
        static void Menu304SplitNotice(string text,out string title,out string source)
        {
            title="";source="";
            if(string.IsNullOrEmpty(text))return;
            string t=text.Trim();
            int nl=t.IndexOf('\n');
            if(nl<0){Menu304SplitSentence(t,out title,out source);return;}
            title=Menu304Clean(t.Substring(0,nl));source=Menu304Clean(t.Substring(nl+1).Replace('\n',' '));
        }

        /// <summary>"A. B." -> title "A", source "B" (toast titles drop the final period like the mockups).</summary>
        static void Menu304SplitSentence(string text,out string title,out string source)
        {
            int dot=text.IndexOf(". ",StringComparison.Ordinal);
            if(dot<0){title=Menu304Clean(text);source="";return;}
            title=Menu304Clean(text.Substring(0,dot));source=Menu304Clean(text.Substring(dot+2));
        }

        static string Menu304Clean(string s)
        {
            if(string.IsNullOrEmpty(s))return "";
            s=s.Trim();
            while(s.EndsWith(".",StringComparison.Ordinal))s=s.Substring(0,s.Length-1).TrimEnd();
            return s;
        }
    }
}
