using UnityEngine;
namespace Oheangbu.App.Prologue {
 [CreateAssetMenu(menuName="Oheangbu/Journey/NPC Attention")]
 public sealed class JourneyNpcAttentionProfileSO:ScriptableObject {
  public string AfterReportPointId;
  public string StopAfterDefeatedId;
  [Min(1)] public float EngageDistance=5;
  [Min(1)] public float DisengageDistance=7;
  [Min(1)] public float TurnDegreesPerSecond=110;
  [Min(.1f)] public float GuideSeconds=3.5f;
 }
}
