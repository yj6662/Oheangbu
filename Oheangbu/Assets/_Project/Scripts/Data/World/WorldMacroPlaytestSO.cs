using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName="Oheangbu/World/Macro Playtest")]
    public sealed class WorldMacroPlaytestSO:ScriptableObject
    {
        [Serializable] public sealed class CheckpointSpec
        {
            public string Id,Label;
            public Vector3 Feet;
            public float Yaw;
            public bool Shop;
            public string ShopRequiredStageId;
            public bool IsConfigured=>!string.IsNullOrWhiteSpace(Id)&&float.IsFinite(Feet.x)&&float.IsFinite(Feet.y)&&float.IsFinite(Feet.z)&&float.IsFinite(Yaw);
        }
        [Serializable] public sealed class Encounter
        {
            public string Id,ContentId;
            public Vector3 Feet;
            public Vector3[] Patrol;
            public bool Ranged;
            public bool RespawnOnRest=true;
            public float Detection=16,Leash=28,Speed=2.6f,Activation=180;
        }
        // Side commissions (의뢰, SPEC-ROADSIDE-COMMISSIONS-303): the giver offers, waits, takes the report and pays once.
        // Evidence is an ordinary interaction point that stays locked until the commission is accepted; a target
        // encounter counts once its defeat is on record (EncounterEvidence survives rest respawns).
        [Serializable] public sealed class CommissionSpec
        {
            public string Id,GiverId,EvidenceId,TargetEncounterId;
            [TextArea] public string OfferText,WaitingText,ReportText,CompletedText;
            public int Reward;
            public string State="TEST";
            // #306 dialogue (SPEC-PLAYTEST-306 #3), all optional: Speaker empty = a leading "X: " in the text, else the giver's prompt name;
            // a *Lines array empty = pages derived from the matching *Text; Title feeds the offer header; Accepted / Declined = the
            // follow-up after 맡는다 / 거절한다 (Accepted empty = the waiting pages, Declined empty = none); Services = extra talk menu rows.
            public string Speaker="",Title="";
            [TextArea] public string[] OfferLines=Array.Empty<string>(),WaitingLines=Array.Empty<string>(),ReportLines=Array.Empty<string>(),CompletedLines=Array.Empty<string>();
            [TextArea] public string[] AcceptedLines=Array.Empty<string>(),DeclinedLines=Array.Empty<string>();
            public PrologueContentSO.PointService306[] Services=Array.Empty<PrologueContentSO.PointService306>();
            public bool IsConfigured=>!string.IsNullOrWhiteSpace(Id)&&!string.IsNullOrWhiteSpace(GiverId)&&
                (!string.IsNullOrWhiteSpace(EvidenceId)||!string.IsNullOrWhiteSpace(TargetEncounterId))&&Reward>=0;
        }
        public CommissionSpec[] Commissions=Array.Empty<CommissionSpec>();
        public Oheangbu.Data.Demo.DemoCampaignProfile Campaign;
        public Oheangbu.Data.Demo.DemoEconomyProfileSO Economy;
        public Oheangbu.Data.Demo.EquipmentCatalogSO EquipmentCatalog;
        public EquipmentUiArtSO EquipmentUiArt;
        public string SaveSlot="world-macro-playtest-v2";
        public string TerrainRevision="macro-first-section-1";
        public PrologueContentSO TestRules;
        public Vector3 StartFeet;
        public float StartYaw;
        [Tooltip("Optional village opening for new saves only. Existing mine start coordinates and saved progress are preserved.")]
        public WorldMacroOpeningProfileSO Opening;
        public PrologueContentSO.Point[] Points=Array.Empty<PrologueContentSO.Point>();
        [Tooltip("Optional checkpoints keyed by the matching Rest interaction ID. Empty preserves legacy mine/inn/office behavior.")]
        public CheckpointSpec[] Checkpoints=Array.Empty<CheckpointSpec>();
        public Encounter[] Encounters=Array.Empty<Encounter>();
        public Vector3[] MainPath=Array.Empty<Vector3>(),BranchPath=Array.Empty<Vector3>();
        public Vector3 InnCheckpointFeet;
        [Header("#306 상호작용 입력 [TEST]")]
        [Tooltip("Same target cannot be re-opened with [F] until this long after the last modal closed or the last interaction (unscaled s).")]
        [Min(0)] public float InteractRepeatCooldownSeconds=.3f;
        [Header("#306 근접 자연물 충돌 [TEST]")]
        [Tooltip("Safe-feet probes within this distance of the walker and of the last ensured point reuse it instead of re-querying the natural solid pool (m). Keep well under the pool's activate radius.")]
        [Min(0)] public float NaturalSolidProbeReuseMeters=4f;
        // #306 dialogue surface (SPEC-PLAYTEST-306 #3) [TEST]: menu labels, page packing, and the escort companion's lines (was Escort.cs).
        [Serializable] public sealed class DialogueLabels306
        {
            public string Talk="이야기",Trade="거래",Upgrade="손질",Rest="쉬기",Maintain="정비",Accept="맡는다",Decline="거절한다";
            public string CommissionHeader="의뢰",RewardHeader="사례 조선통보";
        }
        [Serializable] public sealed class EscortVoice306
        {
            public string Speaker="왕소";
            // #308 D308-2 §5 (SPEC-CONTENT-PACING-308) [TEST, NARR-VOICE 통독 대상]: the departure condition is said in the world's words,
            // never as an instruction (D304/D306). The #308 texts live in the content assets (Content308 content-apply writes them);
            // the code defaults keep the pre-#308 behaviour for a content that has not been migrated (WorldContent298 serializes no
            // EscortVoice block, so its defaults ARE its values): the new fields default to empty and fall back to the old ones.
            [TextArea] public string ContractedText="화물은 이곳에 두었소. 출발할 준비가 되면 다시 이야기합시다.";
            [Tooltip("#308: after the contract once departure is possible (escort stage prerequisites met). Empty = ContractedText.")]
            [TextArea] public string ContractedReadyText="";
            [TextArea] public string EscortingText="화물은 내가 지키겠소. 앞에서 기다리면 따라가리다.";
            [TextArea] public string DeliveredText="봉인은 온전했소. 다음에 만나면 이 빚을 갚으리다.";
            [Tooltip("#308: escort_start talked to while departure is not yet possible (one short prompt line). Empty = StartBoardNotice.")]
            [TextArea] public string StationWaitingText="";
            [Tooltip("#308: escort_start talked to once departure is possible (one short prompt line). Empty = StartBoardNotice.")]
            [TextArea] public string StationReadyText="";
            [Tooltip("LEGACY (#308 D308-2 §5): #308 content sets it empty. Shown only when the two Station texts are empty (unmigrated content) or after departure. Empty = none.")]
            [TextArea] public string StartBoardNotice="왕소와 화물을 안전한 좌석에 싣고 정차한 가마에 탑승하면 출발한다.";
        }
        [Header("#306 대화 화면 [TEST]")]
        public DialogueLabels306 DialogueLabels=new DialogueLabels306();
        [Tooltip("Pages derived from a text (no Lines authored) pack whole sentences up to this many characters; a blank line always breaks.")]
        [Min(8)] public int DialoguePageMaxChars=72;
        [Tooltip("A Rest talk row with no Target rests at the nearest rest point within this distance of the speaker (m).")]
        [Min(0)] public float DialogueRestReachMeters=30f;
        public EscortVoice306 EscortVoice=new EscortVoice306();
        [Header("#308 자동차 시점 [TEST]")]
        [Tooltip("SPEC-CONTENT-PACING-308 §3 (D308-2): the 마석 자동차 call (G), boarding and the menu card need this campaign fact as well as the opening commission (e.g. defeated:cheongryong). Empty = the previous behaviour (other scenes unchanged).")]
        public string VehicleRequiredFact="";
    }
}
