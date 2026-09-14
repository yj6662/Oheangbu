using UnityEngine;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Shared engineering geometry for bridge decks and short supported end aprons.</summary>
    public static class WorldMacroBridgeGeometry
    {
        public sealed class Plan
        {
            public WorldMacroSheetSO Sheet;
            public Vector3 Center,Axis;
            public float DeckHalfLength,DeckTop;
            public const float ApronLength=30f,Width=9f;
            public float SurfaceHeight(Vector3 p,float terrainHeight)
            {
                var delta=p-Center;float along=Vector3.Dot(delta,Axis);
                float across=delta.x*Axis.z-delta.z*Axis.x;
                if(Mathf.Abs(across)>Width*.5f||Mathf.Abs(along)>DeckHalfLength+ApronLength)return terrainHeight;
                if(Mathf.Abs(along)<=DeckHalfLength)return Mathf.Max(terrainHeight,DeckTop);
                return ApronHeight(along,across,terrainHeight);
            }
            public float ApronHeight(float along,float across,float terrainHeight)
            {
                var side=new Vector3(Axis.z,0,-Axis.x);
                var end=Center+Axis*Mathf.Sign(along)*(DeckHalfLength+ApronLength)+side*across;
                float endY=WorldMacroTerrain.SurfaceHeight(Sheet,end.x,end.z)+.015f;
                float t=Mathf.Clamp01((Mathf.Abs(along)-DeckHalfLength)/ApronLength);
                return Mathf.Max(terrainHeight+.015f,Mathf.Lerp(DeckTop,endY,t));
            }
        }
        public static Plan Create(WorldMacroSheetSO s,WorldMacroSheetSO.SiteSpec site)
        {
            float nearest=float.MaxValue,width=40,water=site.Position.y-4;var axis=Vector3.right;
            foreach(var river in s.Rivers)for(int i=1;i<river.Points.Length;i++)
            {
                float d=WorldMacroTerrain.SegmentDistance(site.Position.x,site.Position.z,river.Points[i-1],river.Points[i],out float t);
                if(d>=nearest)continue;nearest=d;width=river.Width;water=Mathf.Lerp(river.Points[i-1].y,river.Points[i].y,t);
                var tangent=river.Points[i]-river.Points[i-1];axis=new Vector3(-tangent.z,0,tangent.x).normalized;
            }
            var p=new Plan{Sheet=s,Center=site.Position,Axis=axis,DeckHalfLength=width*.5f+45f,DeckTop=water+4.9f};
            var lateral=new Vector3(axis.z,0,-axis.x);
            for(int sign=-1;sign<=1;sign+=2)for(int k=-1;k<=1;k++)
            {
                var v=site.Position+axis*sign*p.DeckHalfLength+lateral*k*4.5f;
                p.DeckTop=Mathf.Max(p.DeckTop,WorldMacroTerrain.SurfaceHeight(s,v.x,v.z)+.15f);
            }
            return p;
        }
    }
}
