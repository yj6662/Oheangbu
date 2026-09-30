using System;
using System.Collections.Generic;
using System.Text;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #306 §2-3 session side (SPEC-PLAYTEST-306 #3): every NPC utterance is one DialogueRequest306 raised through RaiseDialogue306
    // (semantics in DialogueRequest306.cs). No subscriber (old UI) = exactly the previous Present() call, so both coexist.
    // Speaker = commission / point field, else a leading "X: " in the text (stripped), else the prompt's name ("벌목꾼과 이야기" -> 벌목꾼).
    // Pages = authored Lines, else the text split at blank lines and packed by sentence up to Content.DialoguePageMaxChars [TEST].
    // Receipts (조선통보 +N) are never pages: they stay notices, raised when the conversation closes so they land on the visible prompt.
    // Evidence, objects and mountain works keep Present(). Menu rows: Trade / Upgrade / Maintain carry the id the UI already opens
    // (village_shop, village_artisan -> OpenEquipmentService; 정비 -> OpenPage), Rest carries the rest point id and rests after the close.
    public sealed partial class WorldMacroPlaytestSession
    {
        // set by the one dialogue view while it is hooked (PlaytestUiRoot.BindScene); observers (harnesses, NPC job actors) may
        // subscribe to DialogueRequested too without making the session think a surface exists
        public bool DialogueViewBound{get;set;}
        public bool DialogueSurfaceBound=>DialogueViewBound&&DialogueRequested!=null;
        // 거절한다: nothing is written; the flag lasts until the giver's next conversation, which offers again
        readonly HashSet<string> commissionDeclined306=new HashSet<string>();
        public bool CommissionDeclined(string commissionId)=>commissionDeclined306.Contains(commissionId);
        string dialogueReceipt306,dialogueRestPending306,dialogueRestArmed306;
        WorldMacroPlaytestSO.DialogueLabels306 labelsFallback306;WorldMacroPlaytestSO.EscortVoice306 voiceFallback306;
        WorldMacroPlaytestSO.DialogueLabels306 Labels306=>Content!=null&&Content.DialogueLabels!=null?Content.DialogueLabels:(labelsFallback306??=new WorldMacroPlaytestSO.DialogueLabels306());
        WorldMacroPlaytestSO.EscortVoice306 EscortVoice306=>Content!=null&&Content.EscortVoice!=null?Content.EscortVoice:(voiceFallback306??=new WorldMacroPlaytestSO.EscortVoice306());
        int PageMaxChars306=>Content!=null?Mathf.Max(8,Content.DialoguePageMaxChars):72;

        // One conversation's context for Chosen: the source, the speaker, the menu (reused by Talk follow-ups) and the Talk pages.
        sealed class Talk306
        {
            public PrologueContentSO.Point Point;public WorldMacroPlaytestSO.CommissionSpec Commission;public DialogueRequest306 Request;
            public string Speaker="",Header="";
            public readonly List<DialogueService306> Services=new List<DialogueService306>();
            public readonly Dictionary<string,string[]> TalkPages=new Dictionary<string,string[]>();
        }

        // ---------- text -> speaker + pages (public static: the editor migration shares the same rules) ----------
        const int MaxSpeakerChars306=12,MaxPromptNameChars306=8;
        static readonly string[] SpeakerParticles306={"에게 ","과 ","와 ","의 "};
        // "노인: 지붕이…" -> 노인 + 지붕이… (a short name before ": " without a line break or sentence mark in it)
        public static bool SplitSpeaker306(string text,out string speaker,out string body)
        {
            speaker="";body=text??"";
            int i=body.IndexOf(": ",StringComparison.Ordinal);if(i<=0||i>MaxSpeakerChars306)return false;
            for(int k=0;k<i;k++){char c=body[k];if(c=='\n'||c=='\r'||c=='.'||c=='?'||c=='!'||c=='…'||c==','||c=='"')return false;}
            string name=body.Substring(0,i).Trim();if(name.Length==0)return false;
            speaker=name;body=body.Substring(i+2).TrimStart();return true;
        }
        // "주막 주인에게 쉬고 정비할 곳 묻기" -> 주막 주인; a prompt without a name particle names nobody
        public static string PromptName306(string prompt)
        {
            if(string.IsNullOrWhiteSpace(prompt))return "";
            int best=-1;foreach(var particle in SpeakerParticles306){int i=prompt.IndexOf(particle,StringComparison.Ordinal);if(i>0&&(best<0||i<best))best=i;}
            return best>0&&best<=MaxPromptNameChars306?prompt.Substring(0,best).Trim():"";
        }
        // authored lines win (blank entries skipped, a prefix naming the same speaker stripped); else the text: a blank line breaks,
        // otherwise whole sentences (and single line breaks) are packed up to maxChars
        public static string[] Pages306(string[] authored,string text,string speaker,int maxChars)
        {
            var pages=new List<string>();
            if(authored!=null)foreach(var line in authored){if(string.IsNullOrWhiteSpace(line))continue;pages.Add(StripSame306(line.Trim(),speaker));}
            if(pages.Count>0||string.IsNullOrWhiteSpace(text))return pages.ToArray();
            var page=new StringBuilder();
            foreach(var block in text.Replace("\r\n","\n").Split(new[]{"\n\n"},StringSplitOptions.RemoveEmptyEntries))
            {
                string para=StripSame306(block.Trim(),speaker);if(para.Length==0)continue;
                if(para.Length<=maxChars){pages.Add(para);continue;}
                page.Clear();
                foreach(var raw in Sentences306(para))
                {
                    if(page.Length>0&&page.ToString().TrimEnd().Length+raw.TrimEnd().Length>maxChars){pages.Add(page.ToString().Trim());page.Clear();}
                    page.Append(page.Length==0?raw.TrimStart():raw);
                }
                if(page.ToString().Trim().Length>0)pages.Add(page.ToString().Trim());
            }
            return pages.ToArray();
        }
        static string StripSame306(string line,string speaker)=>!string.IsNullOrEmpty(speaker)&&SplitSpeaker306(line,out var name,out var body)&&name==speaker?body:line;
        static IEnumerable<string> Sentences306(string para)
        {
            int start=0;
            for(int i=0;i<para.Length;i++)
            {
                char c=para[i];bool next=i+1==para.Length||para[i+1]==' '||para[i+1]=='\n';
                if(c!='\n'&&!((c=='.'||c=='?'||c=='!'||c=='…')&&next))continue;
                yield return para.Substring(start,i+1-start);start=i+1;
            }
            if(start<para.Length)yield return para.Substring(start);
        }

        // ---------- building and raising ----------
        // speaker: explicit (commission field) > point field > "X: " prefix > fallback (escort voice) > prompt name. lines == null = the
        // point's Lines when the text is the point's own Text (a campaign / testimony text replaces them), else derived from the text.
        Talk306 Begin306(PrologueContentSO.Point p,string text,string[] lines,string speaker,string fallbackSpeaker)
        {
            string name=!string.IsNullOrWhiteSpace(speaker)?speaker.Trim():!string.IsNullOrWhiteSpace(p?.Speaker)?p.Speaker.Trim():null;
            string body=text??"";
            if(SplitSpeaker306(body,out var prefix,out var stripped)){body=stripped;if(name==null)name=prefix;}
            if(name==null)name=!string.IsNullOrWhiteSpace(fallbackSpeaker)?fallbackSpeaker.Trim():PromptName306(p?.Prompt);
            var authored=lines??(p!=null&&string.Equals(text,p.Text,StringComparison.Ordinal)?p.Lines:null);
            var r=new DialogueRequest306{SourceId=p?.Id??"",SourcePosition=p!=null?p.Position:Vector3.zero,Speaker=name??"",
                Lines=Pages306(authored,body,name,PageMaxChars306)};
            return new Talk306{Point=p,Request=r,Speaker=r.Speaker};
        }
        bool Open306(Talk306 t,string receipt)
        {
            var r=t.Request;r.Header=t.Header??"";r.Services=t.Services.ToArray();r.Chosen=s=>Chosen306(t,s);
            return Raise306(r,receipt,false);
        }
        // followUp=false opens a conversation (resets the receipt / rest slots); true = shown in place from inside Chosen
        bool Raise306(DialogueRequest306 r,string receipt,bool followUp)
        {
            if(!DialogueSurfaceBound||r==null)return false;
            if(!followUp){dialogueReceipt306=null;dialogueRestPending306=null;}
            if(!string.IsNullOrEmpty(receipt))dialogueReceipt306=receipt;
            r.Closed=DialogueClosed306;
            return RaiseDialogue306(r);
        }
        void DialogueClosed306()
        {
            string receipt=dialogueReceipt306;dialogueReceipt306=null;
            dialogueRestArmed306=dialogueRestPending306;dialogueRestPending306=null;
            if(!string.IsNullOrEmpty(receipt))Show(receipt);
        }
        // pages shown in place; keepMenu = the same menu after them (Talk), otherwise the view closes after the last page.
        // No pages: nothing is raised (the view closes / returns to the menu by itself); a receipt still waits for the close.
        void FollowUp306(Talk306 t,string[] pages,bool keepMenu,string receipt=null)
        {
            if(pages==null||pages.Length==0){if(!string.IsNullOrEmpty(receipt))dialogueReceipt306=receipt;return;}
            var r=new DialogueRequest306{SourceId=t.Request.SourceId,SourcePosition=t.Request.SourcePosition,Speaker=t.Speaker,Lines=pages};
            if(keepMenu){r.Header=t.Header??"";r.Services=t.Services.ToArray();r.Chosen=s=>Chosen306(t,s);}
            Raise306(r,receipt,true);
        }
        string[] Pages306(Talk306 t,string[] authored,string text)=>
            Pages306(authored,SplitSpeaker306(text,out var name,out var body)&&(name==t.Speaker||string.IsNullOrEmpty(t.Speaker))?body:text,t.Speaker,PageMaxChars306);
        static DialogueService306 Service306(DialogueServiceKind306 kind,string label,string id,bool enabled=true)=>new DialogueService306{Kind=kind,Label=label??"",Id=id??"",Enabled=enabled};

        // An NPC line: the dialogue surface when bound, else exactly the old Present(title, text [+ "\n" + receipt]).
        bool Speak306(PrologueContentSO.Point p,string title,string text,string receipt=null,string fallbackSpeaker=null)
        {
            if(!DialogueSurfaceBound){Present(title,string.IsNullOrEmpty(receipt)?text:text+"\n"+receipt);return false;}
            var t=Begin306(p,text,null,null,fallbackSpeaker);AddPointServices306(t,p?.Services);
            if(t.Request.Lines.Length==0&&t.Services.Count==0){if(!string.IsNullOrEmpty(receipt))Show(receipt);return false;}
            return Open306(t,receipt);
        }

        // ---------- menu rows ----------
        void AddPointServices306(Talk306 t,PrologueContentSO.PointService306[] specs)
        {
            if(specs==null)return;var labels=Labels306;
            foreach(var spec in specs)
            {
                if(spec==null)continue;
                bool custom=!string.IsNullOrWhiteSpace(spec.Target);string target=custom?spec.Target.Trim():"",id;
                string label=spec.Label!=null?spec.Label.Trim():"";
                switch(spec.Kind)
                {
                    case PrologueContentSO.PointServiceKind306.Talk:
                        var pages=Pages306(t,spec.Lines,"");if(pages.Length==0)break;
                        id=custom?target:"talk:"+t.TalkPages.Count;t.TalkPages[id]=pages;
                        t.Services.Add(Service306(DialogueServiceKind306.Talk,label.Length>0?label:labels.Talk,id));break;
                    case PrologueContentSO.PointServiceKind306.Trade:
                        id=custom?target:"village_shop";
                        t.Services.Add(Service306(DialogueServiceKind306.Trade,label.Length>0?label:labels.Trade,id,EquipmentReady&&NearEquipmentService(id)));break;
                    case PrologueContentSO.PointServiceKind306.Upgrade:
                        id=custom?target:"village_artisan";
                        t.Services.Add(Service306(DialogueServiceKind306.Upgrade,label.Length>0?label:labels.Upgrade,id,EquipmentReady&&NearEquipmentService(id)));break;
                    case PrologueContentSO.PointServiceKind306.Maintain:
                        t.Services.Add(Service306(DialogueServiceKind306.Maintain,label.Length>0?label:labels.Maintain,custom?target:"정비",AtDemoShop));break;
                    case PrologueContentSO.PointServiceKind306.Rest:
                        var rest=custom?RestPoint306(target):NearestRestPoint306(t.Request.SourcePosition);
                        t.Services.Add(Service306(DialogueServiceKind306.Rest,label.Length>0?label:labels.Rest,rest?.Id??target,rest!=null&&!SaveBlocked));break;
                }
            }
        }
        PrologueContentSO.Point RestPoint306(string id)=>Array.Find(InteractionPoints,x=>x!=null&&x.Id==id&&x.Kind==PrologueInteractionKind.Rest);
        PrologueContentSO.Point NearestRestPoint306(Vector3 from)
        {
            PrologueContentSO.Point best=null;float reach=Mathf.Max(0,Content.DialogueRestReachMeters),bestDistance=float.MaxValue;
            foreach(var p in InteractionPoints)
            {
                if(p==null||p.Kind!=PrologueInteractionKind.Rest||!WorldMacroCheckpointRules.TryResolve(Content,Progress,p.Id,out _))continue;
                float d=Vector3.Distance(from,p.Position);if(d<=reach&&d<bestDistance){best=p;bestDistance=d;}
            }
            return best;
        }
        void Chosen306(Talk306 t,DialogueService306 s)
        {
            if(t==null||s==null)return;
            switch(s.Kind)
            {
                // the view opens these windows itself by Id and returns to the menu; the session only arms the service it trades under
                case DialogueServiceKind306.Trade:case DialogueServiceKind306.Upgrade:if(!EquipmentReady)InitializeEquipment();equipmentService=s.Id;return;
                case DialogueServiceKind306.Maintain:return;
                case DialogueServiceKind306.Rest:dialogueRestPending306=s.Id;return;   // no follow-up: the view closes, then TickDialogueRest306 rests
                case DialogueServiceKind306.Talk:if(t.TalkPages.TryGetValue(s.Id,out var pages))FollowUp306(t,pages,true);return;
                case DialogueServiceKind306.CommissionAccept:AcceptCommission306(t);return;
                case DialogueServiceKind306.CommissionDecline:DeclineCommission306(t);return;
                // CommissionReport is never offered: a satisfied talk reports directly (InteractCommission306)
            }
        }
        // the Rest row, on the first open frame after the view closed (Update, after the input block lifts)
        bool TickDialogueRest306()
        {
            if(string.IsNullOrEmpty(dialogueRestArmed306))return false;
            var point=RestPoint306(dialogueRestArmed306);dialogueRestArmed306=null;
            if(point==null)return false;Rest(point);return true;
        }

        // ---------- the equipment merchant / artisan: a greeting and the service row (the old EquipmentServiceRequested id) ----------
        bool TrySpeakVillageService306(string id)
        {
            if(!DialogueSurfaceBound)return false;
            var p=FindInteractionPoint(id);if(p==null)return false;
            var t=Begin306(p,p.Text,null,null,null);bool shop=id=="village_shop";
            t.Services.Add(Service306(shop?DialogueServiceKind306.Trade:DialogueServiceKind306.Upgrade,shop?Labels306.Trade:Labels306.Upgrade,id,EquipmentReady));
            AddPointServices306(t,p.Services);equipmentService=id;
            return Open306(t,null);
        }

        // ---------- commissions: offer [맡는다 / 거절한다] -> waiting -> (satisfied) the talk reports (receipt on close) -> completed ----------
        string CommissionHeader306(WorldMacroPlaytestSO.CommissionSpec q)
        {
            var labels=Labels306;var header=new StringBuilder(labels.CommissionHeader??"");
            if(!string.IsNullOrWhiteSpace(q.Title))header.Append(header.Length>0?" · ":"").Append(q.Title.Trim());
            if(q.Reward>0&&!string.IsNullOrWhiteSpace(labels.RewardHeader))header.Append(header.Length>0?" · ":"").Append(labels.RewardHeader.Trim()).Append(' ').Append(q.Reward);
            return header.ToString();
        }
        bool InteractCommission306(WorldMacroPlaytestSO.CommissionSpec q,PrologueContentSO.Point giver)
        {
            var ledger=Progress.ledger;var labels=Labels306;
            string text,header="",receipt=null;string[] lines;var rows=new List<DialogueService306>();bool reported=false;
            if(ledger.completed.Contains(ReportedKey(q))){text=q.CompletedText;lines=q.CompletedLines;}
            else if(!ledger.completed.Contains(AcceptedKey(q)))
            {
                text=q.OfferText;lines=q.OfferLines;header=CommissionHeader306(q);commissionDeclined306.Remove(q.Id);
                rows.Add(Service306(DialogueServiceKind306.CommissionAccept,labels.Accept,q.Id));rows.Add(Service306(DialogueServiceKind306.CommissionDecline,labels.Decline,q.Id));
            }
            else if(!CommissionSatisfied(q)){text=q.WaitingText;lines=q.WaitingLines;header=CommissionHeader306(q);}
            else
            {
                // satisfied: this talk reports and pays, as the old flow did (a waiting hint would contradict the item in hand);
                // the reward is a receipt notice on close, InteractionResolved at the talk as today
                if(q.Reward>int.MaxValue-ledger.currency){Show("통보 보유량을 확인한 뒤 다시 시도한다.");return false;}
                var candidate=Detached306();PrologueProgressStore.Complete(candidate.ledger,ReportedKey(q),q.Reward);
                if(!TryCommitInteraction(candidate,out string error)){Show(error);return false;}
                text=q.ReportText;lines=q.ReportLines;reported=true;receipt=q.Reward>0&&!IconPresentation?"조선통보 +"+q.Reward:null;
            }
            var t=Begin306(giver,text,lines??Array.Empty<string>(),q.Speaker,null);t.Commission=q;t.Header=header;t.Services.AddRange(rows);
            AddPointServices306(t,q.Services);AddPointServices306(t,giver.Services);
            bool shown=t.Request.Lines.Length>0||t.Services.Count>0;
            if(shown)Open306(t,receipt);else if(!string.IsNullOrEmpty(receipt))Show(receipt);
            if(reported)InteractionResolved?.Invoke(q.Reward>0?PrologueInteractionKind.Currency:PrologueInteractionKind.Conversation,giver.Position);
            return shown||reported;
        }
        WorldMacroProgress Detached306()=>WorldMacroProgress.MigrateToCurrent(JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Progress)));
        // the accept logic of the old first talk, then a short follow-up (AcceptedLines, else the waiting pages) raised in place
        void AcceptCommission306(Talk306 t)
        {
            var q=t.Commission;if(q==null||Progress.ledger.completed.Contains(AcceptedKey(q)))return;
            var candidate=Detached306();candidate.ledger.completed.Add(AcceptedKey(q));
            if(!TryCommitInteraction(candidate,out string error)){Show(error);return;}   // nothing taken; no follow-up closes the view
            commissionDeclined306.Remove(q.Id);
            InteractionResolved?.Invoke(PrologueInteractionKind.Conversation,t.Point.Position);
            var pages=Pages306(t,q.AcceptedLines,"");if(pages.Length==0)pages=Pages306(t,q.WaitingLines,q.WaitingText);
            FollowUp306(t,pages,false);
        }
        void DeclineCommission306(Talk306 t)
        {
            var q=t.Commission;if(q==null||Progress.ledger.completed.Contains(AcceptedKey(q)))return;
            commissionDeclined306.Add(q.Id);FollowUp306(t,Pages306(t,q.DeclinedLines,""),false);
        }

        // ---------- escort companion (the lines were literals in Escort.cs; now Content.EscortVoice) ----------
        string EscortStageLine306()
        {
            var v=EscortVoice306;var stage=Progress.escort.Stage;
            return stage==DemoEscortStage.Delivered?v.DeliveredText:stage>=DemoEscortStage.Escorting?v.EscortingText:v.ContractedText;
        }
    }
}
