using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 ledger recovery (SPEC-CONTENT-PACING-308 "원장 받아들이기 (adopt)"): the words every adopt command shares.
 // The ledgers of Art/Playtest308 were lost while the scenes stayed as they were. A tool whose apply refuses "revert first" and
 // whose revert finds no ledger is then a dead end. Its adopt command writes the ledger apply WOULD have written for the objects
 // the tool created - from the opened scene, after the tool's own comparison of that scene with the present data says "equal".
 // An adopt writes one ledger file and nothing else: no scene, no asset, no backup. It never overwrites a ledger; a second call
 // answers "변경 없음". What the lost ledger held and the scene cannot give back (the bytes before the first apply, the sha before,
 // rows of earlier passes) is written down as "original unknown", never invented.
 internal static class Adopt308
 {
  internal const string Unknown="original unknown";
  internal static string Utc()=>DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture);
  // the mark of an adopted ledger (its "adopted" field): nobody takes it for the ledger an apply wrote
  internal static string Stamp(string utc,string sceneSha)=>"adopted "+utc+" from scene "+(sceneSha??"").ToLowerInvariant();
  internal static string AssetStamp(string utc,string what)=>"adopted "+utc+" from "+what;
  internal static bool Is(string stamp)=>!string.IsNullOrEmpty(stamp)&&stamp.StartsWith("adopted ",StringComparison.Ordinal);
  internal static string NoBackup=>Unknown+" (adopted ledger: the bytes before the first apply are not held anywhere)";
  internal static string Mark(string stamp)=>Is(stamp)?" ["+stamp.Substring(0,Math.Min(stamp.Length,28))+"]":"";
  // the line a revert of an adopted ledger prints before it starts
  internal static string RevertNote(string stamp)=>Is(stamp)?"  note "+stamp+": this revert removes the objects the tool created and restores nothing else - the state before the first apply is "+Unknown+" (no backup, no before-value)\n":"";
  // a ledger file is there already: the second call of an adopt (no-op text), or somebody's ledger (the words of the refusal)
  internal static string Already(string file,string stamp,int live)=>"변경 없음 (the ledger is already adopted: "+stamp+", "+live+" live row(s); nothing written) "+file;
  internal static string Taken(string file,int live)=>"a ledger exists ("+live+" live row(s), not an adopted one): "+file+" - an adopt never overwrites a ledger. Nothing written";
  internal static string NotEqual(string what,List<string> bad,int show)=>what+" is not what the present data builds ("+bad.Count+"): "+string.Join("; ",bad.GetRange(0,Math.Min(show,bad.Count)))+(bad.Count>show?"; ...":"")+" - no ledger is written for a scene the data does not describe. Nothing written";
  // the tool's dry pass wrote no row for a part it could NOT compare (no ground under it, a child gone): that is not "equal"
  internal static string NotCompared(string what,List<string> blind,int show)=>what+" cannot be shown to be what the present data builds ("+blind.Count+" part(s) the dry pass left uncompared): "+string.Join("; ",blind.GetRange(0,Math.Min(show,blind.Count)))+(blind.Count>show?"; ...":"")+" - no ledger is written for a scene the tool could not compare. Nothing written";
  // a write an adopt made carries "original unknown ..." where an apply carries its backup path. A ledger counts as adopted (status
  // mark, revert note) only while a live row still sits in such a write: after a revert and a real apply it is a plain ledger again
  internal static bool UnknownBackup(string backup)=>!string.IsNullOrEmpty(backup)&&backup.StartsWith(Unknown,StringComparison.Ordinal);

  // null = the two meshes hold the same geometry (per submesh: the same triangles; vertices / normals / uv within tol); else why not
  internal static string MeshDiff(Mesh have,Mesh built,float tol)
  {
   if(have==null||built==null)return "a mesh is missing";
   if(have.vertexCount!=built.vertexCount)return "vertices "+have.vertexCount+" != "+built.vertexCount;
   if(have.subMeshCount!=built.subMeshCount)return "submeshes "+have.subMeshCount+" != "+built.subMeshCount;
   for(int s=0;s<have.subMeshCount;s++)
   {
    int[] a=have.GetTriangles(s),b=built.GetTriangles(s);if(a.Length!=b.Length)return "submesh "+s+": "+a.Length/3+" triangles != "+b.Length/3;
    for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return "submesh "+s+": triangle index "+i+" differs";
   }
   Vector3[] va=have.vertices,vb=built.vertices,na=have.normals,nb=built.normals;
   for(int i=0;i<va.Length;i++)if((va[i]-vb[i]).magnitude>tol)return "vertex "+i+" is "+(va[i]-vb[i]).magnitude.ToString("F4",CultureInfo.InvariantCulture)+" m off";
   if(na.Length!=nb.Length)return "normals "+na.Length+" != "+nb.Length;
   for(int i=0;i<na.Length;i++)if((na[i]-nb[i]).magnitude>tol)return "normal "+i+" differs";
   var ua=new List<Vector2>();var ub=new List<Vector2>();have.GetUVs(0,ua);built.GetUVs(0,ub);
   if(ua.Count!=ub.Count)return "uv "+ua.Count+" != "+ub.Count;
   for(int i=0;i<ua.Count;i++)if((ua[i]-ub[i]).magnitude>tol)return "uv "+i+" differs";
   return null;
  }
 }
}
