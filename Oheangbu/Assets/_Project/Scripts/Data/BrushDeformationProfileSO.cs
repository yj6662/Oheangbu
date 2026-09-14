using UnityEngine;

namespace Oheangbu.Data
{
    /// <summary>표시용 붓털 응답만 소유한다. 입력 수집·인식·필세에는 사용하지 않는다.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/Presentation/Brush Deformation Profile")]
    public sealed class BrushDeformationProfileSO : ScriptableObject
    {
        [Tooltip("붓털을 포함한 전체 제작 길이(m). 런타임은 모델을 이 길이로 늘리지 않는다.")]
        [Min(0.01f)] public float NominalTotalLength = 0.9f;
        [Tooltip("붓대 제작 길이 초깃값(m). 실제 모델 비율 확인 후 조정하는 제작 메타데이터다.")]
        [Min(0.01f)] public float NominalShaftLength = 0.7f;
        [Tooltip("최대 굽힘/퍼짐에 도달하는 표시 획 속도(m/s).")]
        [Min(0.001f)] public float FullBendSpeed = 1.5f;
        [Range(0f, 60f)] public float MaximumBendDegrees = 28f;
        [Tooltip("호 길이에 따른 굽힘 분포의 지수. 1보다 크면 뿌리 쪽을 덜 굽힌다.")]
        [Range(1f, 4f)] public float BendDistributionPower = 1.7f;
        [Tooltip("초당 지수 응답률. 고정 목표에 대해 프레임 간격에 독립적이다.")]
        [Min(0.01f)] public float BendResponse = 22f;
        [Min(0.01f)] public float RecoveryResponse = 12f;
        [Range(0f, 1f)] public float MaximumSplay = 0.65f;
        [Min(0.01f)] public float SplayResponse = 18f;
        [Min(0.01f)] public float SplayRecoveryResponse = 10f;
        [Tooltip("이보다 긴 갱신 공백은 일시정지/컨텍스트 전환으로 보고 중립으로 재설정한다.")]
        [Min(0.1f)] public float ResetAfterGap = 0.5f;
    }
}
