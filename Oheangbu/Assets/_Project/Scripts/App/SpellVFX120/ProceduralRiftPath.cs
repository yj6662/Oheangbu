using System;
using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
 // Local coordinates: x across, y along. Shared junctions are constructed once.
 public sealed class ProceduralRiftPath
 {
  public sealed class Branch
  {
   public Vector2 Start,End;
   public float Bend,Phase,Width;
   public Vector2 At(float along)
   {
    float u=Mathf.InverseLerp(Start.y,End.y,along);
    var p=Vector2.Lerp(Start,End,u);
    p.x+=Mathf.Sin(u*Mathf.PI)*Bend*(.65f*Mathf.Sin(u*Mathf.PI+Phase)+.35f*Mathf.Sin(u*Mathf.PI*5+Phase));
    return p;
   }
  }
  public readonly Branch[] Branches=new Branch[7];
  public ProceduralRiftPath(int seed,float length,float radius)
  {
   var rng=new System.Random(seed);
   float R(float a,float b)=>Mathf.Lerp(a,b,(float)rng.NextDouble());
   void Add(int lane,Vector2 start,Vector2 end,float width)
   { Branches[lane]=new Branch{Start=start,End=end,Bend=R(.025f,.09f)*radius,Phase=R(0,6.28f),Width=width*R(.85f,1.15f)}; }
   Add(0,Vector2.zero,new Vector2(R(-.13f,.13f)*radius,R(.24f,.34f)*length),.46f);
   Add(1,Branches[0].End,new Vector2(R(-.43f,-.20f)*radius,R(.50f,.65f)*length),.24f);
   Add(2,Branches[0].End,new Vector2(R(.18f,.45f)*radius,R(.55f,.70f)*length),.24f);
   Add(3,Branches[1].End,new Vector2(R(-.96f,-.74f)*radius,R(.84f,.99f)*length),.12f);
   Add(4,Branches[1].End,new Vector2(R(-.34f,-.08f)*radius,R(.86f,.97f)*length),.12f);
   Add(5,Branches[2].End,new Vector2(R(.08f,.36f)*radius,R(.88f,.98f)*length),.12f);
   Add(6,Branches[2].End,new Vector2(R(.70f,.96f)*radius,length),.12f);
  }
  public int Lane(int terminal,float along)
  {
   int tip=3+Math.Abs(terminal%4),parent=tip<5?1:2;
   return along<Branches[0].End.y?0:along<Branches[parent].End.y?parent:tip;
  }
 }
}
