using System;
using Newtonsoft.Json.Linq;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 D308-18 (3) (SPEC-WORLD-FALL-RULE-308): one read-only door for the other ledgers of the same realm.
 public static partial class Content308
 {
  /// <summary>content308_scene.json gates.expected_with_cliff_top when gates.realm is the asked realm; 0 = no such number (other realm,
  /// data missing or unreadable). Read only.</summary>
  internal static int GukGatesWithCliffTop308(string realm)
  {
   try
   {
    var gd=SceneData308(out _)["gates"];
    if(gd==null||Opt308(gd,"realm")!=realm)return 0;
    var t=gd["expected_with_cliff_top"];
    return t==null?0:(int)Math.Round(t.Value<double>());
   }
   catch(Refuse308){return 0;}
  }
 }
}
