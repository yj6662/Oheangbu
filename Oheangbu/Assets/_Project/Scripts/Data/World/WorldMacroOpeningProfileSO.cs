using UnityEngine;

namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName="Oheangbu/World/Village Opening")]
    public sealed class WorldMacroOpeningProfileSO : ScriptableObject
    {
        public const string CheckpointId="village_office";
        public const string CommissionId="village_commission";
        public bool Enabled=true;
        public Vector3 StartFeet;
        public float StartYaw;
        public PrologueContentSO.Point Commission=new PrologueContentSO.Point
        {
            Id=CommissionId,Kind=PrologueInteractionKind.Conversation,Radius=2.5f,
            Prompt="아전에게 폐광 조사 의뢰 확인",
            Text="폐광 쪽에서 폭파 소리가 났소. 먼저 현장을 살펴봐 주시오.\n\n오행부 메뉴에서 마석 자동차를 불러 폐광으로 가시오. 흔적을 확인하기 전에는 섣불리 짐작하지 맙시다."
        };
        public string EvidenceInteractionId="mine_inquiry";
        public string IntroductionTitle="관청에서 받은 부름";
        [TextArea] public string IntroductionText="관청 아전에게 폐광 폭파 조사 의뢰를 확인한다.\n\n아전에게 다가가 [F]를 누르세요.";
        public string BeforeCommissionObjective="관청 아전에게 폐광 폭파 조사 의뢰를 확인한다 [F]";
        public string BeforeVehicleObjective="[G] 오행부로 마석 자동차를 부른다";
        public string BeforeEvidenceObjective="폐광으로 이동해 폭파 흔적을 조사한다";

        public bool IsConfigured => Finite(StartFeet)&&float.IsFinite(StartYaw)&&Commission!=null&&
            Commission.Id==CommissionId&&Commission.Kind==PrologueInteractionKind.Conversation&&
            Commission.Currency==0&&Finite(Commission.Position)&&float.IsFinite(Commission.Radius)&&Commission.Radius>0&&
            !string.IsNullOrWhiteSpace(Commission.Prompt)&&!string.IsNullOrWhiteSpace(Commission.Text)&&
            !string.IsNullOrWhiteSpace(EvidenceInteractionId);
        static bool Finite(Vector3 value)=>float.IsFinite(value.x)&&float.IsFinite(value.y)&&float.IsFinite(value.z);
    }
}
