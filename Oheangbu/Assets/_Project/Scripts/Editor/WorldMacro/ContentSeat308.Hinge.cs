using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 relayout (D308-16) step D7 = relayout op D03, BUILD_BRIEF T8: the door hinge of the two doorway rests.
    // WorldMacroInnRestPresentation swings its Door about HingeWorld (a world point written once by the #242 / #245 builds); the
    // houses were re-seated since, so the point is tens of metres from the door. D7 writes the door leaf's present world place.
    //   plan:<alias>            lists D7 when contentseat308.json has "hinges" (read only)
    //   apply:<alias>:D7        only when named; the scene file is backed up and saved by the common apply (ledger: before / after)
    //   verify:<alias>          hinge = the anchor, hinge within hinges.max_from_door_m of the leaf's renderer bounds
    //   revert:<alias>:D7       the ledger's before value back
    // The component is reached through the session's own reference (session_field), never through a name path; the ledger key is
    // "<KeyOf(the component's object)>|<PointId>". A row whose component is absent in a scene is skipped with a note. Nothing but
    // the one Vector3 is written: no object moves, no light, no text.
    public static partial class ContentSeat308
    {
        static WorldMacroInnRestPresentation HingeOf(Ctx k, JToken row)
        {
            string field = Str(row, "session_field");
            switch (field)
            {
                case "InnRestPresentation": return k.Session.InnRestPresentation;
                case "VillageRestPresentation": return k.Session.VillageRestPresentation;
                default: throw new Refuse("data: hinges row " + Str(row, "id") + " has unknown session_field '" + field + "' (InnRestPresentation | VillageRestPresentation)");
            }
        }

        static string HingeKey(WorldMacroInnRestPresentation c) => BuildingAudit308.KeyOf(c.transform) + "|" + c.PointId;

        static WorldMacroInnRestPresentation HingeByKey(Ctx k, string key)
        {
            int cut = key.LastIndexOf('|'); if (cut <= 0) return null;
            var t = BuildingAudit308.Resolve(k.Scene, key.Substring(0, cut)); string pointId = key.Substring(cut + 1);
            if (t == null || BuildingAudit308.KeyOf(t) != key.Substring(0, cut)) return null;
            var found = t.GetComponents<WorldMacroInnRestPresentation>().Where(c => c != null && c.PointId == pointId).ToArray();
            return found.Length == 1 ? found[0] : null;
        }

        // leaf = Door.position; edge = the #242 build rule on the leaf's present renderer bounds
        static bool HingeTarget(Ctx k, JToken row, WorldMacroInnRestPresentation c, out Vector3 target, out string why)
        {
            target = default; why = null; string anchor = Opt(row, "anchor") ?? "leaf";
            if (anchor == "leaf") { target = c.Door.position; return true; }
            if (anchor != "edge") { why = "unknown anchor '" + anchor + "' (leaf | edge)"; return false; }
            var r = c.Door.GetComponent<Renderer>();
            if (r == null) { why = "anchor edge needs a Renderer on the door leaf"; return false; }
            var cp = Array.Find(k.Content.Checkpoints ?? Array.Empty<WorldMacroPlaytestSO.CheckpointSpec>(), p => p != null && p.Id == c.PointId);
            if (cp == null) { why = "anchor edge needs the checkpoint " + c.PointId + " in the content"; return false; }
            var b = r.bounds; var toward = cp.Feet - b.center; toward.y = 0;
            if (toward.sqrMagnitude < 1e-6f) { why = "the checkpoint feet stand on the door"; return false; }
            toward.Normalize(); var side = Vector3.Cross(Vector3.up, toward);
            float half = Mathf.Abs(side.x) * b.extents.x + Mathf.Abs(side.z) * b.extents.z;
            target = b.center - side * (half * OptNum(row, "edge_sign", 1f));
            return true;
        }

        // 0 when the hinge lies inside the leaf's renderer bounds
        static float HingeFromDoor(WorldMacroInnRestPresentation c, Vector3 hinge)
        {
            var r = c.Door != null ? c.Door.GetComponent<Renderer>() : null;
            return r != null ? Mathf.Sqrt(r.bounds.SqrDistance(hinge)) : c.Door != null ? Vector3.Distance(c.Door.position, hinge) : float.PositiveInfinity;
        }

        static StepPlan PlanD7(Ctx k)
        {
            var sp = new StepPlan { step = "D7", detail = "door hinges - HingeWorld to the door leaf" };
            var hg = k.Data["hinges"];
            if (hg == null || hg.Type == JTokenType.Null) { sp.status = "blocked"; sp.detail = "contentseat308.json has no 'hinges' section (308.seat.3 data)"; return sp; }
            if (!Flag(hg, "enabled")) { sp.status = "off"; sp.detail = "off in data"; return sp; }
            float tol = Num(hg, "tol_m");
            foreach (var row in Arr(hg, "rows"))
            {
                string id = Str(row, "id"); var comp = HingeOf(k, row);
                if (comp == null) { sp.notes.Add("absent " + id + ": the session has no " + Str(row, "session_field") + " in this scene (skipped)"); continue; }
                var ch = new Change { step = "D7", key = HingeKey(comp), kind = "hinge", before = Vec(comp.HingeWorld), at = comp.transform.position };
                sp.changes.Add(ch);
                string block = null; Vector3 target = default;
                if (comp.gameObject.scene != k.Scene) block = "the component is not in " + k.ScenePath;
                else if (comp.PointId != Str(row, "point_id")) block = "the component's PointId is '" + comp.PointId + "', the data says '" + Str(row, "point_id") + "'";
                else if (comp.Door == null) block = "the component has no Door";
                else if (HingeByKey(k, ch.key) != comp) block = "the key " + ch.key + " does not resolve to this one component (same-named siblings or two components with this PointId)";
                else if (ProtectedObject(k, comp.transform)) block = "the component stands under a protected tree";
                else if (!HingeTarget(k, row, comp, out target, out string why)) block = why;
                if (block != null) { ch.state = "blocked"; ch.note = block; sp.detail += "; " + id + ": " + block; continue; }
                ch.after = Vec(target); ch.at = target;
                float stale = Vector3.Distance(comp.HingeWorld, target);
                N(k, sp, "D7." + id + ".stale_m", stale); N(k, sp, "D7." + id + ".x", target.x); N(k, sp, "D7." + id + ".y", target.y); N(k, sp, "D7." + id + ".z", target.z);
                var prior = Prior(k.Ledger, "D7", ch.key, "hinge");
                if (stale <= tol) { ch.state = "already"; ch.before = prior != null ? prior.before : ""; ch.note = "the hinge is the door leaf anchor (" + (Opt(row, "anchor") ?? "leaf") + ")"; }
                else if (prior != null && ParseVec(prior.after, out var pa) && ParseVec(prior.before, out var pb) && Vector3.Distance(comp.HingeWorld, pa) > tol && Vector3.Distance(comp.HingeWorld, pb) > tol)
                { ch.state = "mismatch"; ch.note = "HingeWorld " + V(comp.HingeWorld) + " is neither the ledger's before " + V(pb) + " nor its after " + V(pa) + " (changed by another tool since): revert:" + k.Alias + ":D7:force, or close the old op first"; }
                else
                {
                    ch.state = "apply";
                    ch.note = "door " + BuildingAudit308.KeyOf(comp.Door) + " at " + V(comp.Door.position) + "; HingeWorld " + V(comp.HingeWorld) + " is " + F(stale) + " m from the anchor (flat " + F(Harness303.Flat(comp.HingeWorld, target)) + ", dy " + F(comp.HingeWorld.y - target.y) + ") -> " + V(target);
                }
            }
            Settle(sp);
            return sp;
        }

        static void ExecuteHinge(Ctx k, Change c)
        {
            var comp = HingeByKey(k, c.key) ?? throw new InvalidOperationException("hinge component not found: " + c.key);
            if (ProtectedObject(k, comp.transform)) throw new Refuse("protected object " + c.key);
            if (!ParseVec(c.after, out var v)) throw new FormatException(c.after);
            comp.HingeWorld = v; EditorUtility.SetDirty(comp);
            if (PrefabUtility.IsPartOfPrefabInstance(comp)) PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
        }

        static string UndoHinge(Ctx k, Change c, bool force)
        {
            var comp = HingeByKey(k, c.key);
            if (comp == null) return "lost: hinge component not found";
            if (ProtectedObject(k, comp.transform)) return "under a protected tree";
            if (!ParseVec(c.before, out var before)) return "no before value";
            float tol = Num(Req(k.Data, "hinges"), "tol_m");
            if (Vector3.Distance(comp.HingeWorld, before) <= tol) return null;
            if (!force && ParseVec(c.after, out var after) && Vector3.Distance(comp.HingeWorld, after) > tol) return "drifted: HingeWorld is " + V(comp.HingeWorld) + ", not the ledger's after value (revert …:force writes the before value anyway)";
            comp.HingeWorld = before; EditorUtility.SetDirty(comp);
            if (PrefabUtility.IsPartOfPrefabInstance(comp)) PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
            return null;
        }

        static void VerifyD7(Ctx k, Action<bool, string> Row, Action<string> Info, StepPlan sink)
        {
            var hg = Req(k.Data, "hinges"); if (!Flag(hg, "enabled")) { Info("D7 off in data"); return; }
            float tol = Num(hg, "tol_m"), max = Num(hg, "max_from_door_m");
            foreach (var row in Arr(hg, "rows"))
            {
                string id = Str(row, "id"); var comp = HingeOf(k, row);
                if (comp == null) { Info("D7 " + id + ": the session has no " + Str(row, "session_field") + " in this scene (skipped)"); continue; }
                if (comp.Door == null) { Row(false, "D7 " + id + ": the component has no Door"); continue; }
                Row(comp.PointId == Str(row, "point_id"), "D7 " + id + ": the component's PointId is " + comp.PointId);
                if (!HingeTarget(k, row, comp, out var target, out string why)) { Row(false, "D7 " + id + ": " + why); continue; }
                float off = Vector3.Distance(comp.HingeWorld, target), from = HingeFromDoor(comp, comp.HingeWorld);
                N(k, sink, "D7." + id + ".x", comp.HingeWorld.x); N(k, sink, "D7." + id + ".y", comp.HingeWorld.y); N(k, sink, "D7." + id + ".z", comp.HingeWorld.z);
                Row(off <= tol, "D7 " + id + ": HingeWorld " + V(comp.HingeWorld) + " is the door leaf anchor (" + (Opt(row, "anchor") ?? "leaf") + ", " + F(off, "F3") + " m off)");
                Row(from <= max, "D7 " + id + ": hinge " + F(from) + " m from the door leaf " + BuildingAudit308.KeyOf(comp.Door) + " (need ≤ " + F(max) + ")");
            }
        }
    }
}
