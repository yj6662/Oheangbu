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

        static string GraphDump(string path)
        {
            var (_, graph) = OpenGraph(path);
            var sb = new StringBuilder();
            foreach (var c in Kids(graph))
            {
                if (!VT("VFXContext").IsInstanceOfType(c)) continue;
                sb.Append(" [").Append(c.GetType().Name);
                try { var d = Call(VT("VFXContext"), c, "GetData"); if (d != null) { var cap = FieldOf(d.GetType(), "capacity"); if (cap != null) sb.Append(" cap ").Append(cap.GetValue(d)); } } catch { }
                sb.Append(':');
                foreach (var b in Kids(c))
                {
                    sb.Append(' ').Append(b.GetType().Name).Append('(');
                    foreach (var s in Slots(b)) sb.Append(SlotName(s)).Append('=').Append(SlotText(s, 0)).Append(';');
                    sb.Append(')');
                }
                sb.Append(']');
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
