using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // Spec 11.32 / TE-VFXG-1: the 노 smoke graph (concepts C + D, 2026-10-15) authored by code. The VFX Graph editor model is
    // internal, so it is reached by reflection here (a friend assembly could not be checked by the offline compile). Exit:
    // once the graph is settled the .vfx stays the source and this command goes, or a public graph API replaces it.
    //   graph:build  copy the package sample Smoke.vfx to NaSmokeGraph and change it (capacity, flat fan, turbulence, ground)
    //   graph:dump   contexts -> blocks -> input slot values of a graph (default NaSmokeGraph)
    public static partial class Vfx308Build
    {
        const string NaSmokeGraph = "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_smoke.vfx";
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static Assembly _vfxEditor;
        static Assembly VfxEditor => _vfxEditor ??= AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetName().Name == "Unity.VisualEffectGraph.Editor");
        static Type VT(string name) => VfxEditor.GetTypes().First(t => t.Name == name);

        static object Call(Type t, object self, string method, params object[] args)
        {
            var m = t.GetMethods(All).First(x => x.Name == method && x.GetParameters().Length == args.Length);
            return m.Invoke(self, args);
        }
        static object Prop(object self, string name, Type declaring = null)
        {
            var p = (declaring ?? self.GetType()).GetProperty(name, All | BindingFlags.DeclaredOnly) ?? self.GetType().GetProperty(name, All);
            return p.GetValue(self);
        }
        static FieldInfo FieldOf(Type t, string name)
        {
            for (; t != null; t = t.BaseType) { var f = t.GetField(name, All | BindingFlags.DeclaredOnly); if (f != null) return f; }
            return null;
        }
        // a VFXSetting by name; enum values are given as text and parsed against the field's own (internal) enum type
        static void Setting(object model, string name, object value)
        {
            var f = FieldOf(model.GetType(), name) ?? throw new ArgumentException(model.GetType().Name + " has no setting " + name);
            if (f.FieldType.IsEnum && value is string s) value = Enum.Parse(f.FieldType, s);
            Call(VT("VFXModel"), model, "SetSettingValue", name, value);
        }

        static IEnumerable Kids(object model) => (IEnumerable)Prop(model, "children", VT("VFXModel"));
        static IEnumerable Slots(object model) => (IEnumerable)VT("IVFXSlotContainer").GetProperty("inputSlots").GetValue(model);
        static string SlotName(object slot) { var p = Prop(slot, "property"); return (string)p.GetType().GetField("name").GetValue(p); }
        static object Slot(object model, string name)
        {
            foreach (var s in Slots(model)) if (SlotName(s) == name) return s;
            throw new ArgumentException(model.GetType().Name + " has no input " + name + " (has " + string.Join(", ", Slots(model).Cast<object>().Select(SlotName)) + ")");
        }
        // the slot stores its own property type (pq_vfxg4: a velocity slot is the spaceable VFX Vector, not Vector3, and a
        // mismatched value is dropped with only a logged error) - wrap a Vector3 into the one Vector3 field of such a struct,
        // convert numbers, and read the value back so a silent drop fails the build
        static void Put(object model, string name, object value)
        {
            var s = Slot(model, name);
            var p = Prop(s, "property"); var pt = (Type)p.GetType().GetProperty("type", All).GetValue(p);
            object v = value;
            if (!pt.IsInstanceOfType(value))
            {
                if (value is Vector3 && pt.IsValueType && pt.GetFields(All).Count(f => f.FieldType == typeof(Vector3)) == 1)
                { v = Activator.CreateInstance(pt); pt.GetFields(All).First(f => f.FieldType == typeof(Vector3)).SetValue(v, value); }
                else if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(pt)) v = Convert.ChangeType(value, pt);
                else throw new ArgumentException(model.GetType().Name + "." + name + " is " + pt.Name + ", cannot take " + value.GetType().Name);
            }
            s.GetType().GetProperty("value", All).SetValue(s, v);
            var back = Get(model, name);
            if (!Equals(back, v)) throw new Exception(model.GetType().Name + "." + name + " did not take " + v + " (reads " + back + ")");
        }
        static object Get(object model, string name) { var s = Slot(model, name); return s.GetType().GetProperty("value", All).GetValue(s); }
        static void Space(object model, string name, string space)
        {
            var s = Slot(model, name);
            var p = s.GetType().GetProperty("space", All);
            p.SetValue(s, Enum.Parse(p.PropertyType, space));
        }
        // a struct-valued slot (internal VFX types such as Transform): change named fields of the boxed value and put it back
        static void PutFields(object model, string name, params (string field, object value)[] fields)
        {
            var s = Slot(model, name);
            var vp = s.GetType().GetProperty("value", All);
            object v = vp.GetValue(s);
            foreach (var (field, value) in fields) v.GetType().GetField(field, All).SetValue(v, value);
            vp.SetValue(s, v);
        }

        static object Context(object graph, string type)
        {
            foreach (var c in Kids(graph)) if (c.GetType().Name == type) return c;
            throw new ArgumentException("graph has no " + type);
        }
        static object Block(object context, string type, params (string name, object value)[] settings)
        {
            var b = ScriptableObject.CreateInstance(VT(type));
            foreach (var (name, value) in settings) Setting(b, name, value);
            Call(VT("VFXModel"), context, "AddChild", b, -1, true);
            return b;
        }

        static (object resource, object graph) OpenGraph(string path)
        {
            var resType = Type.GetType("UnityEditor.VFX.VisualEffectResource, UnityEditor.VFXModule") ?? throw new Exception("VisualEffectResource type missing");
            var res = resType.GetMethod("GetResourceAtPath", All).Invoke(null, new object[] { path }) ?? throw new Exception("no VFX resource at " + path);
            var graph = Call(VT("VisualEffectResourceExtensions"), null, "GetOrCreateGraph", res);
            return (res, graph);
        }

        static string GraphBuild()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(SampleSmoke) == null) return "graph:build FAIL sample missing " + SampleSmoke;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(NaSmokeGraph));
            if (AssetDatabase.LoadMainAssetAtPath(NaSmokeGraph) != null) AssetDatabase.DeleteAsset(NaSmokeGraph);
            if (!AssetDatabase.CopyAsset(SampleSmoke, NaSmokeGraph)) return "graph:build FAIL copy";
            AssetDatabase.ImportAsset(NaSmokeGraph, ImportAssetOptions.ForceUpdate);
            var (res, graph) = OpenGraph(NaSmokeGraph);

            var init = Context(graph, "VFXBasicInitialize");
            var update = Context(graph, "VFXBasicUpdate");
            // room for the wave: the fan's emit window is ~0.3 s, the burst has to be dense inside it
            Setting(Call(VT("VFXContext"), init, "GetData"), "capacity", 600u);
            // narrow fan (concept C, user 2026-10-16 after pq_vfxg7): a ground streak to the target, a little spread to tear at
            var fan = Block(init, "SetAttribute", ("attribute", "velocity"), ("Composition", "Add"), ("Random", "PerComponent"));
            Space(fan, "A", "Local"); Space(fan, "B", "Local");   // space first: changing it resets the slot value (pq_vfxg4: A = B = 0)
            Put(fan, "A", new Vector3(-.7f, -.3f, -3.5f)); Put(fan, "B", new Vector3(.7f, .2f, 1.5f));   // pq_vfxg7: +-2.2 = a wide wave; pq_vfxg8: +-.8 along = every wisp stopped at one distance (a puck)
            // the roll and tear: an absolute curl-noise force (relative mode would drag the throw back toward the field)
            var turb = Block(update, "Turbulence", ("Mode", "Absolute"), ("NoiseType", "Perlin"));
            // pq_vfxg4: 4 / .45 stood the soot up into a column; pq_vfxg7: 1.6 drifted the cloud sideways
            Put(turb, "Intensity", 1.8f); Put(turb, "frequency", .55f);   // pq_vfxg8: 1 / .35 under force 3 = no tearing; small eddies tear more than they drift
            Put(turb, "octaves", 2); Put(turb, "roughness", .5f);   // pq_vfxg4-7: these sat inside the comment above, octaves stayed 1
            PutFields(turb, "FieldTransform", ("scale", Vector3.one));
            // the ground: a plane at the host's own height (the seam puts the host on the ground point), no bounce, high
            // friction so the soot drags and rolls along the paving; the quad sits on the plane by its size
            var ground = Block(update, "CollisionShape", ("shape", "Plane"), ("radiusMode", "FromSize"));
            Space(ground, "Plane", "Local");
            Put(ground, "Bounce", 0f); Put(ground, "Friction", .1f);   // pq_vfxg5: 1.5 stopped the soot 2 m out; pq_vfxg7: .3 still short

            string before = "fanA " + Get(fan, "A") + " fanB " + Get(fan, "B") + " oct " + Get(turb, "octaves") + " spaceA " + Slot(fan, "A").GetType().GetProperty("space", All).GetValue(Slot(fan, "A"));
            Call(VT("VisualEffectResourceExtensions"), null, "WriteAssetWithSubAssets", res);
            AssetDatabase.ImportAsset(NaSmokeGraph, ImportAssetOptions.ForceUpdate);
            return "graph:build ok " + NaSmokeGraph + " | set: " + before + " | " + GraphDump(NaSmokeGraph);
        }

        // 11.34 fire8: three more 노 graphs, each a copy of the sample with its own spawn shape added LAST (the seam writes
        // Initial Position per cast, so an Overwrite block after it owns the position). Positions are in the host's space: the
        // seam puts the host on the caster's ground point facing the cast, the target dummy ~6 m down +Z.
        const string NaRingGraph = "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_ring.vfx";
        const string NaScorchGraph = "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_scorch.vfx";
        const string NaColumnGraph = "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_column.vfx";

        // a field inside a nested struct slot ("circle.transform.angles"): the boxed copies are set leaf first and put back up
        // the chain (PutFields reaches only the top level), then read back so a dropped value fails the build
        static void PutPath(object model, string name, string path, object value)
        {
            var s = Slot(model, name); var vp = s.GetType().GetProperty("value", All);
            var names = path.Split('.');
            vp.SetValue(s, SetPath(vp.GetValue(s), names, 0, value));
            object back = vp.GetValue(s);
            foreach (var n in names) back = back.GetType().GetField(n, All).GetValue(back);
            if (!Equals(back, value)) throw new Exception(model.GetType().Name + "." + name + "." + path + " did not take " + value + " (reads " + back + ")");
        }
        static object SetPath(object box, string[] names, int i, object value)
        {
            var f = box.GetType().GetField(names[i], All)
                ?? throw new ArgumentException(box.GetType().Name + " has no field " + names[i] + " (has " + string.Join(", ", box.GetType().GetFields(All).Select(x => x.Name)) + ")");
            object v = i == names.Length - 1 ? value : SetPath(f.GetValue(box), names, i + 1, value);
            if (!f.FieldType.IsInstanceOfType(v) && v is IConvertible) v = Convert.ChangeType(v, f.FieldType);
            f.SetValue(box, v);   // box is a boxed struct: SetValue changes the box itself
            return box;
        }
        static object Orient(object output) => Kids(output).Cast<object>().First(b => b.GetType().Name == "Orient");

        static string GraphBuildAt(string path, uint capacity, Action<object, object, object> recipe)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(SampleSmoke) == null) return "graph:build FAIL sample missing " + SampleSmoke;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            if (!AssetDatabase.CopyAsset(SampleSmoke, path)) return "graph:build FAIL copy " + path;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var (res, graph) = OpenGraph(path);
            var init = Context(graph, "VFXBasicInitialize");
            Setting(Call(VT("VFXContext"), init, "GetData"), "capacity", capacity);
            recipe(init, Context(graph, "VFXBasicUpdate"), Context(graph, "VFXPlanarPrimitiveOutput"));
            Call(VT("VisualEffectResourceExtensions"), null, "WriteAssetWithSubAssets", res);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return "graph:build ok " + path + " |" + GraphDump(path);
        }

        // concept D frame 3 ⓐ: a low ring of tongues round the target, each leaning outward (born on a ring band, pushed out
        // from a point under the ring's centre, drawn along its velocity), the core left dark
        static string GraphBuildRing() => GraphBuildAt(NaRingGraph, 400u, (init, update, output) =>
        {
            var ring = Block(init, "PositionShape", ("shape", "Circle"), ("compositionPosition", "Overwrite"), ("positionMode", "ThicknessRelative"));
            Space(ring, "arcCircle", "Local");
            PutPath(ring, "arcCircle", "circle.transform.position", new Vector3(0f, .1f, 6f));
            PutPath(ring, "arcCircle", "circle.transform.angles", new Vector3(90f, 0f, 0f));   // the circle lies in its local XY plane -> onto the ground
            PutPath(ring, "arcCircle", "circle.radius", 2.4f);   // fire9: wider than the target, as frame 3
            Put(ring, "Thickness", .5f);
            var lean = Block(init, "VelocitySpherical", ("composition", "Add"));
            try { Space(lean, "center", "Local"); } catch (Exception) { }   // a plain Vector3 here: then it is in the system's (local) space
            Put(lean, "center", new Vector3(0f, .1f, 6f)); Put(lean, "Speed", .6f);   // fire9: from the ring's own plane -> mostly outward, a ~30° lean with the up push
            Setting(Orient(output), "mode", "AlongVelocity");
        });

        // ⓑ the scorched road: red-ink blots laid flat on the ground from the caster to the target, each drying through the
        // scorch sheet's 16 cels from its first one
        static string GraphBuildScorch() => GraphBuildAt(NaScorchGraph, 200u, (init, update, output) =>
        {
            var line = Block(init, "PositionShape", ("shape", "Line"), ("compositionPosition", "Overwrite"));
            Space(line, "line", "Local");
            PutPath(line, "line", "start", new Vector3(0f, .2f, .8f));   // fire10: .12 still drew nothing readable (42 alive) - lifted again
            PutPath(line, "line", "end", new Vector3(0f, .2f, 6.8f));
            var side = Block(init, "SetAttribute", ("attribute", "position"), ("Composition", "Add"), ("Random", "PerComponent"));
            Space(side, "A", "Local"); Space(side, "B", "Local");
            Put(side, "A", new Vector3(-.7f, 0f, 0f)); Put(side, "B", new Vector3(.7f, 0f, 0f));
            // fire10: frame 3's road marks are dragged streaks along the cast, not round blots - stretched along local Y (= forward)
            var drag = Block(init, "SetAttribute", ("attribute", "scale"), ("Composition", "Overwrite"), ("Random", "Uniform"), ("channels", "Y"));
            Put(drag, "A", 1.8f); Put(drag, "B", 2.8f);
            var first = Block(init, "SetAttribute", ("attribute", "texIndex"), ("Composition", "Overwrite"));
            Put(first, "_TexIndex", 0f);
            var o = Orient(output); Setting(o, "mode", "Advanced");
            Space(o, "AxisZ", "Local"); Space(o, "AxisY", "Local");
            Put(o, "AxisZ", Vector3.up); Put(o, "AxisY", Vector3.forward);
            Setting(output, "cullMode", "Off"); Setting(output, "useSoftParticle", false);
        });

        // ⓒ a thin soot column rising behind the target's right shoulder, torn by turbulence (fire10: born above and behind the
        // ring, not inside it - fire9's puffs grew into one dark ball over the target)
        static string GraphBuildColumn() => GraphBuildAt(NaColumnGraph, 120u, (init, update, output) =>
        {
            var disc = Block(init, "PositionShape", ("shape", "Circle"), ("compositionPosition", "Overwrite"), ("positionMode", "Volume"));
            Space(disc, "arcCircle", "Local");
            PutPath(disc, "arcCircle", "circle.transform.position", new Vector3(1.2f, 1.6f, 7.8f));
            PutPath(disc, "arcCircle", "circle.transform.angles", new Vector3(90f, 0f, 0f));
            PutPath(disc, "arcCircle", "circle.radius", .25f);
            var turb = Block(update, "Turbulence", ("Mode", "Absolute"), ("NoiseType", "Perlin"));
            Put(turb, "Intensity", .8f); Put(turb, "frequency", .4f); Put(turb, "octaves", 2); Put(turb, "roughness", .5f);
            PutFields(turb, "FieldTransform", ("scale", Vector3.one));
        });

        static string GraphDump(string path, bool settings = false)
        {
            var (_, graph) = OpenGraph(path);
            var sb = new StringBuilder();
            foreach (var c in Kids(graph))
            {
                if (!VT("VFXContext").IsInstanceOfType(c)) continue;
                sb.Append(" [").Append(c.GetType().Name);
                try { var d = Call(VT("VFXContext"), c, "GetData"); if (d != null) { var cap = FieldOf(d.GetType(), "capacity"); if (cap != null) sb.Append(" cap ").Append(cap.GetValue(d)); } } catch { }
                if (settings) sb.Append(" {").Append(SettingText(c)).Append('}');
                sb.Append(':');
                foreach (var b in Kids(c))
                {
                    sb.Append(' ').Append(b.GetType().Name);
                    if (settings) sb.Append('{').Append(SettingText(b)).Append('}');
                    sb.Append('(');
                    foreach (var s in Slots(b)) sb.Append(SlotName(s)).Append('=').Append(SlotText(s, 0)).Append(';');
                    sb.Append(')');
                }
                sb.Append(']');
            }
            return sb.ToString();
        }

        static System.Collections.Generic.IEnumerable<FieldInfo> SettingFields(Type t)
        {
            for (; t != null && t != typeof(ScriptableObject); t = t.BaseType)
                foreach (var f in t.GetFields(All | BindingFlags.DeclaredOnly))
                    if (f.GetCustomAttributes(true).Any(x => x.GetType().Name == "VFXSettingAttribute")) yield return f;
        }
        static string SettingText(object model) => string.Join(" ", SettingFields(model.GetType()).Select(f => f.Name + "=" + f.GetValue(model)));

        // 11.33 8th pass: graph:blocks:<filter>[:setting=value ...] lists the VFX block / context types whose name contains filter,
        // with their settings (enum values spelled out) and the input slots a fresh instance has after the given settings - the
        // recipe is then written from real names (class and slot names moved between VFX Graph versions)
        static string GraphBlocks(string[] parts)
        {
            string filter = parts.Length > 2 ? parts[2] : "";
            var sb = new StringBuilder("graph:blocks " + filter);
            var types = VfxEditor.GetTypes().Where(t => !t.IsAbstract && (VT("VFXBlock").IsAssignableFrom(t) || VT("VFXContext").IsAssignableFrom(t))
                && t.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(t => t.Name).Take(40);
            foreach (var t in types)
            {
                sb.Append("\n").Append(t.Name).Append(" {");
                foreach (var f in SettingFields(t))
                    sb.Append(f.Name).Append(':').Append(f.FieldType.IsEnum ? f.FieldType.Name + "[" + string.Join("|", Enum.GetNames(f.FieldType)) + "]" : f.FieldType.Name).Append(' ');
                sb.Append('}');
                try
                {
                    var b = ScriptableObject.CreateInstance(t);
                    for (int i = 3; i < parts.Length; i++) { var kv = parts[i].Split('='); try { Setting(b, kv[0], kv[1]); } catch (Exception x) { sb.Append(" !").Append(kv[0]).Append(' ').Append(x.GetBaseException().Message); } }
                    sb.Append(" in(");
                    foreach (var s in Slots(b)) { var p = Prop(s, "property"); sb.Append(SlotName(s)).Append(':').Append(((Type)p.GetType().GetProperty("type", All).GetValue(p)).Name).Append(' '); }
                    sb.Append(')');
                    UnityEngine.Object.DestroyImmediate(b);
                }
                catch (Exception x) { sb.Append(" slots? ").Append(x.GetBaseException().Message); }
            }
            return sb.ToString();
        }

        // a slot's own value, or what feeds it: an exposed parameter by name, an operator with its inputs (pq_vfxg9: the
        // soot stopped 3-4 m out whatever the exposed velocity said - the dump showed only the unlinked default values)
        static string SlotText(object s, int depth)
        {
            foreach (var l in (IEnumerable)Prop(s, "LinkedSlots"))
            {
                var owner = Prop(l, "owner");
                if (owner == null) return "<?";
                var exposed = owner.GetType().GetProperty("exposedName", All);
                if (exposed != null) return "<" + exposed.GetValue(owner);
                if (depth >= 4) return "<" + owner.GetType().Name;
                return "<" + owner.GetType().Name + "(" + string.Join(",", Slots(owner).Cast<object>().Select(x => SlotName(x) + "=" + SlotText(x, depth + 1))) + ")";
            }
            object v = s.GetType().GetProperty("value", All).GetValue(s);
            return v is float f ? f.ToString("0.##") : v?.ToString() ?? "-";
        }
    }
}
