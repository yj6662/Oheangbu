using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Object=UnityEngine.Object;
using Dress295=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  sealed class Woodland295
  {
   public readonly string Id;
   public readonly Vector2 Centre,Radius;
   public readonly int Count;
   public Woodland295(string id,Vector2 centre,Vector2 radius,int count)
   {Id=id;Centre=centre;Radius=radius;Count=count;}
   public float Distance(Vector2 p,float margin=0)
   {var d=p-Centre;return new Vector2(d.x/(Radius.x+margin),d.y/(Radius.y+margin)).magnitude;}
  }
  struct ClearSegment295
  {
   public Vector2 A,B;
   public float Radius;
   public bool Contains(Vector2 p)
   {
    if(p.x<Mathf.Min(A.x,B.x)-Radius||p.x>Mathf.Max(A.x,B.x)+Radius||p.y<Mathf.Min(A.y,B.y)-Radius||p.y>Mathf.Max(A.y,B.y)+Radius)return false;
    var d=B-A;float t=d.sqrMagnitude>.0001f?Mathf.Clamp01(Vector2.Dot(p-A,d)/d.sqrMagnitude):0;
    return (p-A-d*t).sqrMagnitude<Radius*Radius;
   }
  }
  static Woodland295[] WoodlandGroups295()=>new[]{
   new Woodland295("west_palace",new Vector2(1710,4600),new Vector2(92,72),72),
   new Woodland295("north_hillside",new Vector2(2030,5200),new Vector2(102,78),60)
  };

  static float ShoreGroundY295(CompactWorldSurface field,Vector2 p)
  {
   float x=Mathf.Clamp(p.x/field.Cell,0,field.Width-1.0001f),z=Mathf.Clamp(p.y/field.Cell,0,field.Height-1.0001f);
   int ix=(int)x,iz=(int)z;float u=x-ix,v=z-iz,c=field.Cell;
   float a=field.Sample(ix*c,iz*c),b=field.Sample((ix+1)*c,iz*c),d=field.Sample(ix*c,(iz+1)*c),e=field.Sample((ix+1)*c,(iz+1)*c);
   return u+v<=1?a+(b-a)*u+(d-a)*v:e+(d-e)*(1-u)+(b-e)*(1-v);
  }

  static void DressLakeShore295(CompactWorldSurface field,CompactHydrologySO hydro,WorldTerrainQuery query,Dress295 sheet,
   List<Dress295.FixedPlacement> placements,Color32[] pixels,int width,int height,Func<Vector2,bool> clear,List<string> report)
  {
   var protection=hydro.ProtectedMask.bytes;var random=new System.Random(295162);var occupied=new List<Vector2>();
   int projected=0,unresolved=0,omitted=0,rockBanks=0,gravelBanks=0,mudBanks=0,added=0,painted=0;
   float R(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
   bool Protected(Vector2 p,float radius=2)
   {
    int x0=Mathf.Max(0,Mathf.FloorToInt((p.x-radius)/hydro.Cell)),x1=Mathf.Min(hydro.Width-1,Mathf.CeilToInt((p.x+radius)/hydro.Cell));
    int z0=Mathf.Max(0,Mathf.FloorToInt((p.y-radius)/hydro.Cell)),z1=Mathf.Min(hydro.Height-1,Mathf.CeilToInt((p.y+radius)/hydro.Cell));
    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)if(protection[z*hydro.Width+x]!=0)return true;
    return false;
   }
   bool Dry(Vector2 p,float y)=>!query.TryWaterHeight(new Vector3(p.x,y,p.y),out var water)||y>=water+.025f;
   bool Project(Vector2 origin,out Vector2 shore,out Vector2 inland)
   {
    shore=inland=default;float level=hydro.Lake.Level,best=float.PositiveInfinity;
    if(!query.TryWaterHeight(new Vector3(origin.x,level,origin.y),out float actualWater)||Mathf.Abs(actualWater-level)>.05f)return false;
    var normal=field.Normal(origin.x,origin.y);float first=Mathf.Atan2(-normal.z,-normal.x);
    for(int ray=0;ray<17;ray++)
    {
     float angle=ray==0?first:(ray-1)*Mathf.PI/8;var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
     float prior=0;
     for(float distance=.5f;distance<=12;distance+=.5f)
     {
      if(distance>best+1)break;var p=origin+direction*distance;
      if(p.x<4||p.x>3996||p.y<4||p.y>5996)break;
      if(ShoreGroundY295(field,p)<=level+.04f){prior=distance;continue;}
      float lo=prior,hi=distance;
      for(int i=0;i<10;i++){float mid=(lo+hi)*.5f;if(ShoreGroundY295(field,origin+direction*mid)>level+.04f)hi=mid;else lo=mid;}
      var edge=origin+direction*hi;var dry=edge+direction*.65f;float y=ShoreGroundY295(field,dry);
      if(y>level+.04f&&Dry(dry,y)&&field.Normal(dry.x,dry.y).y>=.58f&&hi<best)
      {best=hi;shore=edge;inland=direction;}
      break;
     }
    }
    return float.IsFinite(best);
   }
   void Place(int prototype,Vector2 p,float scale,float sink)
   {
    if(!clear(p)||Protected(p)||occupied.Any(q=>(q-p).sqrMagnitude<1.1f))return;
    float y=ShoreGroundY295(field,p);var proto=sheet.Prototypes[prototype];bool plant=proto.Category==Dress295.Kind.Shrub||proto.Category==Dress295.Kind.Tree;
    if(!Dry(p,y)||field.Normal(p.x,p.y).y<(plant?.9f:.58f)||y>hydro.Lake.Level+(plant?2.4f:7))return;
    var position=new Vector3(p.x,y-sink,p.y);float yaw=R(0,360);
    if(proto.Category==Dress295.Kind.Rock&&proto.GroundPoints!=null&&proto.GroundPoints.Length>0)
    {
     var matrix=Matrix4x4.TRS(position,Quaternion.Euler(0,yaw,0),Vector3.one*scale);
     var offsets=proto.GroundPoints.Select(q=>matrix.MultiplyPoint3x4(q)).Select(q=>ShoreGroundY295(field,new Vector2(q.x,q.z))-q.y).OrderBy(v=>v).ToArray();
     position.y+=offsets[offsets.Length/2]-sink;
    }
    placements.Add(new Dress295.FixedPlacement{Id="watershed295_lakebank_"+added.ToString("D3"),ClusterId="watershed295_lake_shore",PrototypeId=proto.Id,Position=position,Euler=new Vector3(0,yaw,0),Scale=scale,Preserve=true});
    occupied.Add(p);added++;
   }
   foreach(var sample in hydro.Lake.ShorePoints)
   {
    if(!Project(new Vector2(sample.x,sample.z),out var shore,out var inland)){unresolved++;continue;}
    projected++;var centre=shore+inland*.8f;
    if(!clear(centre)||Protected(centre)){omitted++;continue;}
    float normalY=field.Normal(centre.x,centre.y).y;
    bool rock=normalY<.85f,mud=normalY>.965f&&Mathf.PerlinNoise(shore.x*.009f+31,shore.y*.009f+11)>.43f;
    if(rock)rockBanks++;else if(mud)mudBanks++;else gravelBanks++;
    var tangent=new Vector2(-inland.y,inland.x);float along=rock?R(5,9):R(7,14),across=rock?R(1.4f,2.5f):mud?R(4,7):R(3,5);
    float footprint=along+across;
    for(int z=Mathf.Max(0,Mathf.FloorToInt((shore.y-footprint)/2));z<=Mathf.Min(height-1,Mathf.CeilToInt((shore.y+footprint)/2));z++)
    for(int x=Mathf.Max(0,Mathf.FloorToInt((shore.x-footprint)/2));x<=Mathf.Min(width-1,Mathf.CeilToInt((shore.x+footprint)/2));x++)
    {
     var p=new Vector2(x*2,z*2);var delta=p-shore;float lateral=Vector2.Dot(delta,tangent),depth=Vector2.Dot(delta,inland);
     if(depth<-.8f||depth>across||Mathf.Abs(lateral)>along||!clear(p)||Protected(p))continue;
     float y=ShoreGroundY295(field,p),above=y-hydro.Lake.Level;if(above<-.12f||above>(rock?5:2.5f))continue;
     float edge=1-Mathf.SmoothStep(.3f,1,Mathf.Abs(lateral)/along);
     float inlandFade=1-Mathf.SmoothStep(.15f,1,Mathf.Max(0,depth)/across);
     float breakup=Mathf.Lerp(.55f,1,Mathf.PerlinNoise(p.x*.19f+7,p.y*.19f+13));
     float strength=edge*inlandFade*breakup*(rock?.55f:.92f);byte cover=(byte)Mathf.RoundToInt(strength*255);
     float wetness=Mathf.Lerp(mud?.95f:.66f,mud?.62f:.12f,Mathf.Clamp01(above/(mud?1.4f:2)));
     int index=z*width+x;if(cover>pixels[index].r){pixels[index]=new Color32(cover,(byte)Mathf.RoundToInt(cover*wetness),0,255);painted++;}
    }
    // Larger quiet stretches separate irregular local clumps instead of a uniform ring.
    float group=Mathf.PerlinNoise(shore.x*.017f+61,shore.y*.017f+5);
    if(group<.38f||random.NextDouble()>.72)continue;
    int count=rock?random.Next(3,7):mud?random.Next(5,11):random.Next(4,9);
    for(int i=0;i<count;i++)
    {
     var p=shore+tangent*R(-along*.78f,along*.78f)+inland*R(.45f,across*.9f);
     if(rock)Place(random.Next(3),p,R(.24f,.66f),.07f);
     else if(mud&&random.NextDouble()<.72)Place(3+random.Next(3),p,R(.45f,.88f),.025f);
     else Place(random.Next(2),p,R(.13f,.32f),.04f);
    }
   }
   report.Add("Actual lake shore: waterline projections="+projected+" / "+hydro.Lake.ShorePoints.Length+", unresolved steep/wet="+unresolved+", protected/route/precinct omitted="+omitted+", rock/gravel/mud banks="+rockBanks+"/"+gravelBanks+"/"+mudBanks+", dry grounded props="+added+", sediment mask pixel updates="+painted+". Short continuous local waterline search; hydrology and terrain unchanged.");
  }

  // Called after the immutable source placements have been reprojected and filtered.
  // All new placements belong to the candidate sheet; source assets and terrain stay untouched.
  static void EnhanceDressing295(CompactWorldSurface oldField,CompactWorldSurface field,
   CompactHydrologySO hydro,WorldTerrainQuery query,Dress295 source,
   List<Dress295.FixedPlacement> instances,Material ground,Func<Vector3,bool> withinPrecinct,List<string> report)
  {
   var layout=Session292().MountainLayout;
   var protectedBytes=hydro.ProtectedMask!=null?hydro.ProtectedMask.bytes:null;
   if(protectedBytes==null||protectedBytes.Length!=hydro.Width*hydro.Height||hydro.Cell<=0)
    throw new InvalidOperationException("295 woodland requires the verified protected mask.");
   var woods=WoodlandGroups295();
   var prototypes=source.Prototypes.Where(p=>p.Category==Dress295.Kind.Tree&&
    (p.Id=="Hyeongang_SM_Salixpierotii_Summer_1"||p.Id=="Hyeongang_SM_UlmusDavidiana_Summer_2"))
    .OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
   if(prototypes.Length!=2)throw new InvalidOperationException("295 woodland tree prototypes are missing from the immutable Hyeongang palette.");
   var segments=new List<ClearSegment295>();
   void Path(Vector3[] path,float halfWidth)
   {
    if(path==null)return;
    for(int i=1;i<path.Length;i++)segments.Add(new ClearSegment295{A=new Vector2(path[i-1].x,path[i-1].z),B=new Vector2(path[i].x,path[i].z),Radius=halfWidth+5});
   }
   var places=layout.Places.ToDictionary(p=>p.Id);
   foreach(var route in layout.Routes)
   {
    if(!places.TryGetValue(route.From,out var from)||!places.TryGetValue(route.To,out var to))continue;
    var points=new List<Vector2>{from.XZ};points.AddRange(route.Bends);points.Add(to.XZ);
    for(int i=1;i<points.Count;i++)segments.Add(new ClearSegment295{A=points[i-1],B=points[i],Radius=route.Width*.5f+5});
   }
   foreach(var mountain in layout.Mountains){Path(mountain.MainPath,3);Path(mountain.TemplePath,3);Path(mountain.ReturnPath,3);}
   foreach(var passage in source.Passages)segments.Add(new ClearSegment295{A=new Vector2(passage.A.x,passage.A.z),B=new Vector2(passage.B.x,passage.B.z),Radius=passage.Width*.5f+5});
   bool Protected(Vector2 p,float radius)
   {
    int x0=Mathf.Max(0,Mathf.FloorToInt((p.x-radius)/hydro.Cell)),x1=Mathf.Min(hydro.Width-1,Mathf.CeilToInt((p.x+radius)/hydro.Cell));
    int z0=Mathf.Max(0,Mathf.FloorToInt((p.y-radius)/hydro.Cell)),z1=Mathf.Min(hydro.Height-1,Mathf.CeilToInt((p.y+radius)/hydro.Cell));
    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)if(protectedBytes[z*hydro.Width+x]!=0)return true;
    return false;
   }
   bool DryStable(Vector2 p)
   {
    foreach(var offset in new[]{Vector2.zero,Vector2.left*2,Vector2.right*2,Vector2.up*2,Vector2.down*2})
    {
     var q=p+offset;float y=field.Sample(q.x,q.y);
     if(field.Normal(q.x,q.y).y<.87f||query.TryWaterHeight(new Vector3(q.x,y,q.y),out float water)&&y<water+1)return false;
    }
    return true;
   }
   // A spatial index keeps both retained and new crowns from being planted on top of one another.
   const float spacing=7.5f;var occupied=new Dictionary<Vector2Int,List<Vector2>>();
   Vector2Int Key(Vector2 p)=>new Vector2Int(Mathf.FloorToInt(p.x/spacing),Mathf.FloorToInt(p.y/spacing));
   void Occupy(Vector2 p){var key=Key(p);if(!occupied.TryGetValue(key,out var list))occupied[key]=list=new List<Vector2>();list.Add(p);}
   bool TooClose(Vector2 p)
   {
    var key=Key(p);
    for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)if(occupied.TryGetValue(key+new Vector2Int(x,z),out var list)&&list.Any(q=>(q-p).sqrMagnitude<spacing*spacing))return true;
    return false;
   }
   var treeIds=new HashSet<string>(source.Prototypes.Where(p=>p.Category==Dress295.Kind.Tree).Select(p=>p.Id));
   // Also makes an explicit repeated call safe without accumulating a second layer.
   instances.RemoveAll(p=>p.Id!=null&&p.Id.StartsWith("watershed295_woodland_",StringComparison.Ordinal));
   foreach(var item in instances)if(treeIds.Contains(item.PrototypeId))Occupy(new Vector2(item.Position.x,item.Position.z));
   int total=0;
   for(int group=0;group<woods.Length;group++)
   {
    var wood=woods[group];var random=new System.Random(295270+group*113);int accepted=0;
    float R(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
    for(int attempt=0;attempt<12000&&accepted<wood.Count;attempt++)
    {
     var p=wood.Centre+new Vector2(R(-wood.Radius.x,wood.Radius.x),R(-wood.Radius.y,wood.Radius.y));
     float edge=wood.Distance(p);
     if(edge>1)continue;
     // Irregular lobes and a broken edge leave visible open ground between smaller stands.
     float noise=Mathf.PerlinNoise(p.x*.031f+17,p.y*.031f+43);
     float chance=Mathf.SmoothStep(.04f,.8f,noise)*Mathf.SmoothStep(1,.3f,edge);
     if(random.NextDouble()>chance||Protected(p,4)||TooClose(p))continue;
     var pos=new Vector3(p.x,field.Sample(p.x,p.y),p.y);
     if(withinPrecinct(pos)||!RiverClear294(layout,p,5)||segments.Any(s=>s.Contains(p))||!DryStable(p))continue;
     var proto=prototypes[random.Next(prototypes.Length)];float scale=R(Mathf.Max(.8f,proto.Scale.x),Mathf.Min(1.12f,proto.Scale.y));
     instances.Add(new Dress295.FixedPlacement{Id="watershed295_woodland_"+wood.Id+"_"+accepted.ToString("D3"),ClusterId="watershed295_woodland_"+wood.Id,PrototypeId=proto.Id,Position=pos-Vector3.up*.06f,Euler=new Vector3(0,R(0,360),0),Scale=scale,Preserve=true});
     Occupy(p);accepted++;total++;
    }
    report.Add("Private woodland "+wood.Id+": "+accepted+" trees, centre "+wood.Centre+", road-edge clearance >=5m, dry bank >=1m, normal Y >=0.87");
   }
   if(total<80)report.Add("WARN: private woodland accepted only "+total+" / 132 trees; terrain and clearance gates were kept intact.");
  }

  // Run after the bank sheet and its renderer have also been refreshed: banks include occasional willows.
  static void UpdateCanopyDressing295(CompactWorldSurface oldField,CompactWorldSurface field,CompactHydrologySO hydro,
   Dress295 source,List<Dress295.FixedPlacement> instances,Material ground,List<string> report)
  {
   var protectedBytes=hydro.ProtectedMask!=null?hydro.ProtectedMask.bytes:null;
   if(protectedBytes==null||protectedBytes.Length!=hydro.Width*hydro.Height||hydro.Cell<=0)
    throw new InvalidOperationException("295 canopy requires the verified protected mask.");
   CanopyDressing295(oldField,field,hydro,protectedBytes,source,instances,ground,WoodlandGroups295(),report);
  }

  static void CanopyDressing295(CompactWorldSurface oldField,CompactWorldSurface field,CompactHydrologySO hydro,
   byte[] protectedBytes,Dress295 source,List<Dress295.FixedPlacement> instances,Material ground,Woodland295[] woods,List<string> report)
  {
   const int width=1000,height=1500;const float cell=4;
   string originalPath=A292+"/Surface/canopy293.png",targetPath=A295+"/Dressing/Canopy295.png";
   var original=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
   if(!original.LoadImage(File.ReadAllBytes(originalPath))||original.width!=width||original.height!=height)
   {Object.DestroyImmediate(original);throw new InvalidOperationException("Immutable canopy293.png dimensions differ from the 4m world mask.");}
   var pixels=original.GetPixels32();Object.DestroyImmediate(original);
   var cover=new float[width*height];int trees=0;
   void Splat(int x,int z,float value){if(x>=0&&z>=0&&x<width&&z<height)cover[z*width+x]+=value;}
   void Trees(Dress295 sheet,IEnumerable<Dress295.FixedPlacement> placements)
   {
    var lookup=sheet.Prototypes.Where(p=>p.Category==Dress295.Kind.Tree).GroupBy(p=>p.Id).ToDictionary(g=>g.Key,g=>g.First());
    foreach(var p in placements)
    {
     if(!lookup.TryGetValue(p.PrototypeId,out var proto))continue;
     float r=Mathf.Max(proto.Size.x,proto.Size.z)*p.Scale*.5f,area=Mathf.PI*r*r/(cell*cell);
     float fx=p.Position.x/cell-.5f,fz=p.Position.z/cell-.5f;int x=Mathf.FloorToInt(fx),z=Mathf.FloorToInt(fz);float tx=fx-x,tz=fz-z;
     Splat(x,z,area*(1-tx)*(1-tz));Splat(x+1,z,area*tx*(1-tz));Splat(x,z+1,area*(1-tx)*tz);Splat(x+1,z+1,area*tx*tz);trees++;
    }
   }
   Trees(source,instances);
   // Candidate landscape is supplied above before assignment; include other currently rendered tree sheets once.
   var extras=Session292().gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactRebuildArtRenderer>())
    .Where(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.Sheet!=null&&r.Sheet!=source&&r.Sheet.FixedPlacements.Length<=50000)
    .Select(r=>r.Sheet).Distinct().OrderBy(s=>AssetDatabase.GetAssetPath(s),StringComparer.Ordinal);
   foreach(var sheet in extras)Trees(sheet,sheet.FixedPlacements);
   float[] kernel={.037f,.111f,.217f,.271f,.217f,.111f,.037f};var temp=new float[cover.Length];
   for(int pass=0;pass<2;pass++)
   {
    for(int z=0;z<height;z++)for(int x=0;x<width;x++){float v=0;for(int k=0;k<7;k++)v+=kernel[k]*cover[z*width+Mathf.Clamp(x+k-3,0,width-1)];temp[z*width+x]=v;}
    for(int z=0;z<height;z++)for(int x=0;x<width;x++){float v=0;for(int k=0;k<7;k++)v+=kernel[k]*temp[Mathf.Clamp(z+k-3,0,height-1)*width+x];cover[z*width+x]=v;}
   }
   var region=(float[])cover.Clone();const int radius=8;
   for(int pass=0;pass<3;pass++)
   {
    for(int z=0;z<height;z++){float run=0;for(int x=-radius;x<=radius;x++)run+=region[z*width+Mathf.Clamp(x,0,width-1)];for(int x=0;x<width;x++){temp[z*width+x]=run/(2*radius+1);run+=region[z*width+Mathf.Min(x+radius+1,width-1)]-region[z*width+Mathf.Max(x-radius,0)];}}
    for(int x=0;x<width;x++){float run=0;for(int z=-radius;z<=radius;z++)run+=temp[Mathf.Clamp(z,0,height-1)*width+x];for(int z=0;z<height;z++){region[z*width+x]=run/(2*radius+1);run+=temp[Mathf.Min(z+radius+1,height-1)*width+x]-temp[Mathf.Max(z-radius,0)*width+x];}}
   }
   float Percentile(float[] values,float q){var v=values.Where(a=>a>.005f).OrderBy(a=>a).ToArray();return v.Length>0?v[Mathf.Min(v.Length-1,(int)(v.Length*q))]:1;}
   float full=Percentile(cover,.97f),forest=Percentile(region,.9f);int changed=0,preserved=0;
   for(int z=0;z<height;z++)for(int x=0;x<width;x++)
   {
    int i=z*width+x;var p=new Vector2((x+.5f)*cell,(z+.5f)*cell);
    int px=Mathf.Clamp(Mathf.FloorToInt(p.x/hydro.Cell),0,hydro.Width-2),pz=Mathf.Clamp(Mathf.FloorToInt(p.y/hydro.Cell),0,hydro.Height-2);
    bool protectedPixel=protectedBytes[pz*hydro.Width+px]!=0||protectedBytes[pz*hydro.Width+px+1]!=0||protectedBytes[(pz+1)*hydro.Width+px]!=0||protectedBytes[(pz+1)*hydro.Width+px+1]!=0;
    float delta=Mathf.Abs(field.Sample(p.x,p.y)-oldField.Sample(p.x,p.y));
    float weight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.15f,3,delta));
    foreach(var wood in woods)weight=Mathf.Max(weight,Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,.65f,wood.Distance(p,70))));
    if(weight<=0||protectedPixel){preserved++;continue;}
    var before=pixels[i];float d=Mathf.Pow(Mathf.Clamp01(cover[i]/full),.8f),f=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.75f,region[i]/forest));
    pixels[i]=new Color32((byte)Mathf.RoundToInt(Mathf.Lerp(before.r,d*255,weight)),(byte)Mathf.RoundToInt(Mathf.Lerp(before.g,f*255,weight)),before.b,before.a);
    if(pixels[i].r!=before.r||pixels[i].g!=before.g)changed++;else preserved++;
   }
   DevSceneKit.EnsureFolder(A295+"/Dressing");
   var result=new Texture2D(width,height,TextureFormat.RGBA32,false,true);result.SetPixels32(pixels);result.Apply(false,false);
   byte[] png=result.EncodeToPNG();Object.DestroyImmediate(result);
   if(!File.Exists(targetPath)||!File.ReadAllBytes(targetPath).SequenceEqual(png))File.WriteAllBytes(targetPath,png);
   AssetDatabase.ImportAsset(targetPath,ImportAssetOptions.ForceUpdate);
   var importer=(TextureImporter)AssetImporter.GetAtPath(targetPath);
   importer.textureType=TextureImporterType.Default;importer.sRGBTexture=false;importer.mipmapEnabled=true;
   importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;importer.npotScale=TextureImporterNPOTScale.None;
   importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
   ground.SetTexture("_Canopy293",AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath));EditorUtility.SetDirty(ground);
   report.Add("Private canopy from "+trees+" actual retained/added trees; changed pixels="+changed+", byte-preserved pixels="+preserved+"; original canopy retained outside changed terrain / woodland influence and on protected cells.");
  }
 }
}
