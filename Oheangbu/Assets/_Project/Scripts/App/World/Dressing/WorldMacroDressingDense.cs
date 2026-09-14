using System;
using System.Collections.Generic;
using UnityEngine;
using Oheangbu.Data.World;

namespace Oheangbu.App.World.Dressing
{
    public sealed partial class WorldMacroDressingRenderer
    {
        float GroundResidentDistance=>Sheet.DenseVegetation?Mathf.Max(Sheet.GrassDistance,Sheet.GroundCoverDistance):Sheet.GrassDistance;
        void PrepareGround(LiveCell cell)
        {
            cell.Grass=Generate(cell.Source,2,false);
            if(Sheet.DenseVegetation&&Sheet.LowGrassInfill)
            {
                var low=Generate(cell.Source,2,false,false,true);
                int count=cell.Grass.Length;Array.Resize(ref cell.Grass,count+low.Length);Array.Copy(low,0,cell.Grass,count,low.Length);
            }
            cell.Cover=Sheet.DenseVegetation?Generate(cell.Source,2,false,true):null;
            cell.GrassReady=true;
        }
        WorldMacroCombatWalker activeWalker;
        float nextObserverSearch;
        void ResolveObserver()
        {
            // The macro fly camera stays authoritative while enabled. Playtest disables it;
            // residency and collision then follow the actual walk/vehicle camera, not its old POI.
            if(Observer!=null&&Observer.isActiveAndEnabled)return;
            if(Application.isPlaying)
            {
                if(activeWalker==null&&Time.unscaledTime>=nextObserverSearch)
                {activeWalker=FindFirstObjectByType<WorldMacroCombatWalker>();nextObserverSearch=Time.unscaledTime+.5f;}
                var actual=activeWalker!=null?activeWalker.ViewCamera:null;
                if(actual!=null&&actual.isActiveAndEnabled){Observer=actual;return;}
            }
            var main=Camera.main;if(main!=null&&main.isActiveAndEnabled)Observer=main;
        }
        int ChooseLowGrass(int realm,byte habitat,float altitude,float slope)
        {
            foreach(int i in prototypes[realm,2])
            {var p=Sheet.Prototypes[i];if(p.LowInfill&&(!p.WetBank||habitat==2)&&altitude<=p.MaximumAltitude&&slope<=p.MaximumSlope)return i;}
            return -1;
        }
        void IndexPassages()
        {
            passagesByCell.Clear();exclusionsByCell.Clear();if(Sheet==null||!Sheet.DenseVegetation||Sheet.Geography==null)return;
            foreach(var area in typedExclusions)
            {
                var q=Quaternion.Euler(0,area.Yaw,0);var bounds=new Bounds(area.Centre,Vector3.zero);
                for(int i=0;i<4;i++)bounds.Encapsulate(area.Centre+q*new Vector3((i&1)==0?-area.HalfSize.x:area.HalfSize.x,0,(i&2)==0?-area.HalfSize.y:area.HalfSize.y));
                bounds.Expand(12);int x0=Mathf.FloorToInt((bounds.min.x-Sheet.Geography.BoundsMin.x)/256),x1=Mathf.FloorToInt((bounds.max.x-Sheet.Geography.BoundsMin.x)/256),z0=Mathf.FloorToInt((bounds.min.z-Sheet.Geography.BoundsMin.y)/256),z1=Mathf.FloorToInt((bounds.max.z-Sheet.Geography.BoundsMin.y)/256);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){int key=Key(x,z);if(!exclusionsByCell.TryGetValue(key,out var list)){list=new List<WorldMacroDressingSheetSO.PreserveArea>();exclusionsByCell.Add(key,list);}list.Add(area);}
            }
            foreach(var route in Sheet.Passages)
            {
                float pad=route.Width*.5f+12;
                int x0=Mathf.FloorToInt((Mathf.Min(route.A.x,route.B.x)-pad-Sheet.Geography.BoundsMin.x)/256);
                int x1=Mathf.FloorToInt((Mathf.Max(route.A.x,route.B.x)+pad-Sheet.Geography.BoundsMin.x)/256);
                int z0=Mathf.FloorToInt((Mathf.Min(route.A.z,route.B.z)-pad-Sheet.Geography.BoundsMin.y)/256);
                int z1=Mathf.FloorToInt((Mathf.Max(route.A.z,route.B.z)+pad-Sheet.Geography.BoundsMin.y)/256);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                {int key=Key(x,z);if(!passagesByCell.TryGetValue(key,out var list)){list=new List<WorldMacroDressingSheetSO.Passage>();passagesByCell.Add(key,list);}list.Add(route);}
            }
        }
        float RouteClusterGain(WorldMacroDressingSheetSO.Cell cell,Vector3 pos,int category)
        {
            if(category>1||!passagesByCell.TryGetValue(Key(cell.X,cell.Z),out var routes))return 1;
            float edge=float.PositiveInfinity;foreach(var route in routes)edge=Mathf.Min(edge,WorldMacroDressingSheetSO.PassageEdgeDistance(route,pos));
            if(edge>45)return 1;
            // Low shoulder vegetation yields to sight; irregular dense groups sit beyond the tread.
            float edgeGain=category==0?Mathf.Lerp(.75f,1.25f,Mathf.InverseLerp(3,14,edge)):Mathf.Lerp(.55f,1.35f,Mathf.InverseLerp(1,8,edge));
            float patch=Mathf.PerlinNoise((pos.x+719)*.043f,(pos.z-311)*.043f);
            return Mathf.Lerp(edgeGain,1,Mathf.InverseLerp(25,45,edge))*Mathf.Lerp(.8f,1.2f,patch);
        }
        bool Blocked(WorldMacroDressingSheetSO.Cell cell,Vector3 pos,WorldMacroDressingSheetSO.Kind kind,float radius,float groundFootprint=0)
        {
            if(exclusionsByCell.TryGetValue(Key(cell.X,cell.Z),out var exclusions))foreach(var area in exclusions)if(WorldMacroDressingSheetSO.Excludes(area,pos,kind))return true;
            if(passagesByCell.TryGetValue(Key(cell.X,cell.Z),out var routes))
            {
                // Small grass may meet the tread edge. Trunks and rocks retain physical clearance.
                float clearance=kind==WorldMacroDressingSheetSO.Kind.Grass?.08f:kind==WorldMacroDressingSheetSO.Kind.Shrub?.65f:Mathf.Max(.8f,radius)+.8f;
                foreach(var route in routes)if(WorldMacroDressingSheetSO.PassageEdgeDistance(route,pos)<clearance+groundFootprint)return true;
            }
            return false;
        }
        [Serializable] public sealed class CellDiagnostic
        {
            public int trees,shrubs,grass,cover,lowGrass,lowGrassBlocked,lowGrassDuplicateIds,duplicateTreeIds,treePlacementMismatches,blockedInstances;
            public float minimumTreeHeight=100000,maximumTreeHeight;
            public bool finite=true;public string scope="Deterministic population generation; not visibility, gameplay traversal or measured FPS.";
        }
        public CellDiagnostic DiagnoseCell(int x,int z)
        {
            if(loadedSheet!=Sheet||prototypes==null)ResetCache();
            if(!geographyCells.TryGetValue(Key(x,z),out var cell))throw new ArgumentException("Unknown authored cell.");
            var report=new CellDiagnostic();var a=Generate(cell,0,false);var b=Generate(cell,0,true);var ids=new HashSet<long>();
            report.trees=a.Length;report.shrubs=Generate(cell,1,false).Length;report.grass=Generate(cell,2,false).Length;report.cover=Generate(cell,2,false,true).Length;
            if(Sheet.LowGrassInfill)
            {
                var low=Generate(cell,2,false,false,true);report.lowGrass=low.Length;var lowIds=new HashSet<long>();
                foreach(var item in low){var p=Sheet.Prototypes[item.Prototype];if(!lowIds.Add(item.Id))report.lowGrassDuplicateIds++;if(Blocked(cell,item.Position,p.Category,p.Radius*item.Scale,p.Radius*item.Scale))report.lowGrassBlocked++;}
            }
            var far=new Dictionary<long,Item>();foreach(var i in b)far[i.Id]=i;
            foreach(var i in a)
            {
                if(!ids.Add(i.Id))report.duplicateTreeIds++;
                if(!far.TryGetValue(i.Id,out var other)||other.Matrix!=i.Matrix||other.Prototype!=i.Prototype)report.treePlacementMismatches++;
                report.finite&=!float.IsNaN(i.Position.x)&&!float.IsInfinity(i.Position.x)&&!float.IsNaN(i.Position.y)&&!float.IsInfinity(i.Position.y)&&!float.IsNaN(i.Position.z)&&!float.IsInfinity(i.Position.z);
                var p=Sheet.Prototypes[i.Prototype];float height=p.Size.y*i.Scale;report.minimumTreeHeight=Mathf.Min(report.minimumTreeHeight,height);report.maximumTreeHeight=Mathf.Max(report.maximumTreeHeight,height);
                if(Blocked(cell,i.Position,p.Category,p.Radius*i.Scale))report.blockedInstances++;
            }
            return report;
        }
    }
}
