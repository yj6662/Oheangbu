using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
 // A4 (SPEC-FIELD-FIRST-HOUR-308 §8.15, TEST): sheet vegetation rows that close a sightline leg, taken out through the
 // BuildingFix308.VegRows ledger (Fix/veg-ledger.json, tag from the data), so "Veg replay" brings the removal back after a #296
 // re-bake and the sheet gets a byte backup before the write. No scene is opened or saved.
 //   field308-rows[:plan|:apply|:revert]   field117.json sight_rows{tag, tol_m, rows[]: sheet, id, proto, x, y, z}; plan is read only
 public static partial class FieldFirst308
 {
  static string Rows(string verb)
  {
   var d=Data()["sight_rows"]??throw new Refused("field117.json: sight_rows missing (python Tools/Art/FieldFirst308/field117_build.py)");
   string tag=S(d,"tag");float tol=d["tol_m"].Value<float>();
   if(verb=="revert"){PostLedger308.RequireEditable();return BuildingFix308.VegRowsRevert(tag,tol);}
   var asks=new List<BuildingFix308.VegRowAsk308>();
   foreach(var r in (d["rows"] as JArray)??new JArray())
    asks.Add(new BuildingFix308.VegRowAsk308{sheet=S(r,"sheet"),id=S(r,"id"),proto=S(r,"proto"),at=new Vector3(r["x"].Value<float>(),r["y"].Value<float>(),r["z"].Value<float>())});
   if(asks.Count==0)throw new Refused("field117.json: sight_rows.rows is empty");
   if(verb=="apply"){PostLedger308.RequireEditable();return BuildingFix308.VegRowsApply(tag,asks,tol);}
   if(verb!="plan")throw new Refused("field308-rows:"+verb+" (plan | apply | revert)");
   return BuildingFix308.VegRowsPlan(tag,asks,tol);
  }
 }
}
