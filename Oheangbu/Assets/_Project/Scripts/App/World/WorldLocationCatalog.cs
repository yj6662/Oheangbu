using System;
using System.Collections.Generic;
using UnityEngine;
using Oheangbu.App.World.UI;
namespace Oheangbu.App.World
{
 [CreateAssetMenu(menuName="Oheangbu/World/Location arrivals")]
 public sealed class WorldLocationCatalog:ScriptableObject
 {
  [Serializable]public sealed class Entry
  {
   public string Id,Name,MarkerId,RealmId;
   public Vector2[] Polygon=Array.Empty<Vector2>();
   public Vector3 Centre;public float Radius=30,MinimumY=-1000,MaximumY=2000;
   public int Priority=10;
   public Vector3[] FloorPath=Array.Empty<Vector3>();
   public bool Contains(Vector3 p,float margin=0)
   {
    if(p.y<MinimumY||p.y>MaximumY||!FloorContains(FloorPath,p,3))return false;
    var q=new Vector2(p.x,p.z);
    if(Polygon==null||Polygon.Length<3)return Vector2.Distance(q,new Vector2(Centre.x,Centre.z))<=Radius+margin;
    if(WorldMapDiscoveryGrid.Contains(Polygon,q))return true;
    if(margin<=0)return false;
    for(int i=0;i<Polygon.Length;i++){var a=Polygon[i];var d=Polygon[(i+1)%Polygon.Length]-a;float t=d.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(q-a,d)/d.sqrMagnitude):0;if(Vector2.Distance(q,a+d*t)<=margin)return true;}
    return false;
   }
  }
  public Entry[] Entries=Array.Empty<Entry>();
  public static bool FloorContains(Vector3[] path,Vector3 p,float clearance)
  {
   if(path==null||path.Length<2)return true;
   float closest=float.PositiveInfinity,y=0;
   for(int i=1;i<path.Length;i++){var a=new Vector2(path[i-1].x,path[i-1].z);var d=new Vector2(path[i].x,path[i].z)-a;
    float t=d.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(new Vector2(p.x,p.z)-a,d)/d.sqrMagnitude):0;float dist=(new Vector2(p.x,p.z)-a-d*t).sqrMagnitude;
    if(dist<closest){closest=dist;y=Mathf.Lerp(path[i-1].y,path[i].y,t);}}
   return p.y>=y-1&&p.y<=y+clearance;
  }
  public float SettleSeconds=.65f,ExitMargin=5,RepeatSeconds=30;
  public Entry RealmAt(Vector3 p)
  {
   foreach(var e in Entries)if(e!=null&&e.Priority==0&&e.Contains(p))return e;
   return null;
  }
  public string DisplayName(Entry place)
  {
   if(place==null)return "";
   if(place.Priority==0)return place.Name;
   Entry realm=null;
   foreach(var e in Entries)if(e!=null&&e.Priority==0&&e.Id==place.RealmId){realm=e;break;}
   realm=realm??RealmAt(place.Centre);
   return realm!=null?realm.Name+" · "+place.Name:place.Name;
  }
  public void ArrivalNames(Entry place,out string realmName,out string localName)
  {
   realmName="";localName="";if(place==null)return;
   if(place.Priority==0){realmName=place.Name;return;}
   Entry realm=null;foreach(var e in Entries)if(e!=null&&e.Priority==0&&e.Id==place.RealmId){realm=e;break;}
   realm=realm??RealmAt(place.Centre);
   realmName=realm!=null?realm.Name:place.Name;localName=realm!=null?place.Name:"";
  }
  public static Color RealmInk(string id)
  {
   switch(id){case "realm_cheongrim":return new Color(.24f,.46f,.58f);case "realm_jeokro":return new Color(.60f,.28f,.24f);
    case "realm_hwanggyeong":return new Color(.72f,.60f,.30f);case "realm_cheolong":return new Color(.98f,.97f,.91f);
    case "realm_hyeongang":return new Color(.20f,.23f,.26f);default:return new Color(.8f,.8f,.78f);}
  }
  public Entry Resolve(Vector3 p,Entry current)
  {
   Entry best=null;
   foreach(var entry in Entries)if(entry!=null&&entry.Contains(p)&&(best==null||entry.Priority>best.Priority||entry.Priority==best.Priority&&string.CompareOrdinal(entry.Id,best.Id)<0))best=entry;
   if(current!=null&&current.Contains(p,ExitMargin)&&(best==null||best.Priority<=current.Priority))return current;
   return best;
  }
 }
 // Pure clock/volume logic shared by the runtime presenter and boundary fixtures.
 public sealed class WorldLocationTracker
 {
  readonly WorldLocationCatalog catalog;readonly Dictionary<string,float> shown=new Dictionary<string,float>();
  WorldLocationCatalog.Entry candidate;float candidateSince;string pending;
  public WorldLocationCatalog.Entry Current{get;private set;}
  public WorldLocationTracker(WorldLocationCatalog value){catalog=value??throw new ArgumentNullException(nameof(value));}
  public void Step(Vector3 position,float now)
  {
   var next=catalog.Resolve(position,Current);
   if(next!=candidate){candidate=next;candidateSince=now;}
   if(next==Current||now-candidateSince<catalog.SettleSeconds)return;
   Current=next;pending=next?.Id;
  }
  public WorldLocationCatalog.Entry TakeAnnouncement(float now,bool allowed)
  {
   if(!allowed||Current==null||pending!=Current.Id)return null;
   pending=null;if(shown.TryGetValue(Current.Id,out var last)&&now-last<catalog.RepeatSeconds)return null;
   shown[Current.Id]=now;return Current;
  }
  public static float Opacity(float seconds)=>seconds<0?0:seconds<.5f?seconds/.5f:seconds<3.2f?1:Mathf.Clamp01((4.1f-seconds)/.9f);
 }
}
