using UnityEngine;
namespace Oheangbu.App.World.UI {
 [CreateAssetMenu(menuName="Oheangbu/UI/Compact icon presentation")]
 public sealed class CompactUiProfileSO : ScriptableObject {
 public Sprite Heart, InkBottle, Hand, Mountain, Inn, Cave, Resume, Save, Exit;
 public Color Health=new Color(.38f,.055f,.065f,1), Ink=new Color(.055f,.11f,.13f,1);
 public Sprite Marker(WorldMapMarkerSpec marker)=>marker.Kind==WorldMapMarkerKind.Mountain?Mountain:marker.Kind==WorldMapMarkerKind.Rest?Inn:Cave;
 }
}
