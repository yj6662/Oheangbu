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
            => GraphBuildAt(path, capacity, (Action<object, object, object, object>)((spawner, init, update, output) => recipe(init, update, output)));
        // fire18: the recipe also gets the spawner context (the ember's one-shot burst)
        static string GraphBuildAt(string path, uint capacity, Action<object, object, object, object> recipe)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(SampleSmoke) == null) return "graph:build FAIL sample missing " + SampleSmoke;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            if (!AssetDatabase.CopyAsset(SampleSmoke, path)) return "graph:build FAIL copy " + path;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var (res, graph) = OpenGraph(path);
            var init = Context(graph, "VFXBasicInitialize");
            Setting(Call(VT("VFXContext"), init, "GetData"), "capacity", capacity);
            recipe(Context(graph, "VFXBasicSpawner"), init, Context(graph, "VFXBasicUpdate"), Context(graph, "VFXPlanarPrimitiveOutput"));
            Call(VT("VisualEffectResourceExtensions"), null, "WriteAssetWithSubAssets", res);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return "graph:build ok " + path + " |" + GraphDump(path);
        }

        // concept D frame 3 ⓐ: a low ring of tongues round the target, each leaning outward (born on a ring band, pushed out
        // from a point under the ring's centre, drawn along its velocity), the core left dark
        // fire16 (user 2026-10-16 "네가 자유롭게 만든다면 ... 그렇게 진행해줘"): the painted tongue sheet (fire308.py brush, 4 tongues x
        // 16-cel lives). Each tongue stands on its root (pivot at the cel's root line), barely moves (the velocity only sets the
        // lean), is one of the 4 tongues for its whole life (a per-particle start cel from a 4-texel map) and plays that life once
        static string GraphBuildRing() => GraphBuildAt(NaRingGraph, 400u, (init, update, output) =>
        {
            var ring = Block(init, "PositionShape", ("shape", "Circle"), ("compositionPosition", "Overwrite"), ("positionMode", "ThicknessRelative"));
            Space(ring, "arcCircle", "Local");
            PutPath(ring, "arcCircle", "circle.transform.position", new Vector3(0f, .1f, 6f));
            PutPath(ring, "arcCircle", "circle.transform.angles", new Vector3(90f, 0f, 0f));   // the circle lies in its local XY plane -> onto the ground
            PutPath(ring, "arcCircle", "circle.radius", 2.3f);   // fire9: wider than the target, as frame 3
            Put(ring, "Thickness", .4f);   // fire16: a band 1.4-2.3 m out, over the pool's red strokes
            var lean = Block(init, "VelocitySpherical", ("composition", "Add"));
            try { Space(lean, "center", "Local"); } catch (Exception) { }   // a plain Vector3 here: then it is in the system's (local) space
            Put(lean, "center", new Vector3(0f, .1f, 6f)); Put(lean, "Speed", .06f);   // fire16: with the host's .12 up = a ~27° outward lean, ~.15 m of drift
            var pivot = Block(init, "SetAttribute", ("attribute", "pivot"), ("Composition", "Overwrite"), ("Random", "Off"));
            PutOnly(pivot, new Vector3(0f, -.43f, 0f));   // the brush sheet's root line sits at .935 of the cel (fire308.py base_y)
            var scale = Block(init, "SetAttribute", ("attribute", "scale"), ("Composition", "Overwrite"), ("Random", "Uniform"));
            Put(scale, "A", Vector3.one * .6f); Put(scale, "B", Vector3.one * 1.4f);
            var pick = Block(init, "AttributeFromMap", ("attribute", "texIndex"), ("Composition", "Overwrite"), ("SampleMode", "RandomConstantPerParticle"));
            Put(pick, "attributeMap", BrushVariantMap());
            Setting(Orient(output), "mode", "AlongVelocity");
            Setting(output, "primitiveType", "Quad");   // the sample's octagon clips the cel corners, where hooked tips can reach
        });

        // the ring host hides a tongue for the first RingHide of its life (size 0, until the poured fire reaches the target) and
        // plays RingCels cels over the rest, so each tongue starts RingCels x RingHide / (1 - RingHide) cels before its variant's
        // first cel (variant k = rows 2k, 2k+1 = texIndex 16k): the hidden part runs through cels nobody sees
        const float RingHide = .21f, RingCels = 15f;
        const string BrushVariantMapPath = "Assets/_Project/Art/SpellVFX308/V2/Graphs/T_V308_brush_variants.asset";
        static Texture2D BrushVariantMap()
        {
            if (AssetDatabase.LoadMainAssetAtPath(BrushVariantMapPath) != null) AssetDatabase.DeleteAsset(BrushVariantMapPath);
            float lead = RingCels * RingHide / (1f - RingHide);
            var t = new Texture2D(4, 1, TextureFormat.RFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "T_V308_brush_variants" };
            t.SetPixelData(new[] { -lead, 16f - lead, 32f - lead, 48f - lead }, 0);
            t.Apply(false, false);
            AssetDatabase.CreateAsset(t, BrushVariantMapPath);
            return t;
        }
        // a block whose only input is the value (SetAttribute with Random Off: the slot is named after the attribute)
        static void PutOnly(object model, object value) => Put(model, SlotName(Slots(model).Cast<object>().First()), value);

        // ⓑ the scorched road: red-ink blots laid flat on the ground from the caster to the target, each drying through the
        // scorch sheet's 16 cels from its first one
        static string GraphBuildScorch() => GraphBuildAt(NaScorchGraph, 200u, (init, update, output) =>
        {
            // fire13: one flat box over the road, 1.8 m wide from .8 to 6.8 m. fire11-12 used a line plus a "position Add" of
            // +-.9 in Local space - a position-typed slot converts with the host's translation too, so every streak was added the
            // host's world position again and drawn kilometres away (27 alive, none on screen)
            var road = Block(init, "PositionShape", ("shape", "OrientedBox"), ("compositionPosition", "Overwrite"), ("positionMode", "Volume"));
            Space(road, "Box", "Local");
            // fire11: the probe put the host on the floor (59.80), so the .2 lift only floated the marks; just clear of z-fighting
            // fire14: centres from 2.0 m (was .8) - a streak reaches ~1.1 m back from its centre (head end toward the caster), and
            // in fire13 the first ones lay behind the caster and blurred across the bottom of the behind view
            PutPath(road, "Box", "center", new Vector3(0f, .05f, 4.3f));
            PutPath(road, "Box", "size", new Vector3(1.8f, 0f, 4.6f));
            // fire10: frame 3's road marks are dragged streaks along the cast, not round blots - stretched along local Y (= forward)
            var drag = Block(init, "SetAttribute", ("attribute", "scale"), ("Composition", "Overwrite"), ("Random", "Uniform"), ("channels", "Y"));
            Put(drag, "A", 2.2f); Put(drag, "B", 3.4f);   // fire11: the drag sheet's streaks are narrow in the cel - longer on the road
            // fire11: one of the drag sheet's 16 streaks per particle, held for its whole life (the host's flipbook rate is 0)
            var pick = Block(init, "SetAttribute", ("attribute", "texIndex"), ("Composition", "Overwrite"), ("Random", "Uniform"));
            Put(pick, "A", 0f); Put(pick, "B", 15.99f);
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

        // fire16: two more layers under the ring (concept D frame 3: the tongues are painted over a dark red-black ground)
        const string NaWashGraph = "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_wash.vfx";
        const string NaPoolGraph = "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_pool.vfx";

        // ⓓ the soot wash: soft red-soot puffs filling the disc behind and between the tongues, slowly lifting and torn a little.
        // Every puff starts from the puff sheet's first (darkest, youngest) cel
        static string GraphBuildWash() => GraphBuildAt(NaWashGraph, 160u, (init, update, output) =>
        {
            var disc = Block(init, "PositionShape", ("shape", "Circle"), ("compositionPosition", "Overwrite"), ("positionMode", "Volume"));
            Space(disc, "arcCircle", "Local");
            PutPath(disc, "arcCircle", "circle.transform.position", new Vector3(0f, .6f, 6.4f));   // a little behind the target
            PutPath(disc, "arcCircle", "circle.transform.angles", new Vector3(90f, 0f, 0f));
            PutPath(disc, "arcCircle", "circle.radius", 1.9f);
            var first = Block(init, "SetAttribute", ("attribute", "texIndex"), ("Composition", "Overwrite"), ("Random", "Off"));
            PutOnly(first, 0f);
            var turb = Block(update, "Turbulence", ("Mode", "Absolute"), ("NoiseType", "Perlin"));
            Put(turb, "Intensity", .5f); Put(turb, "frequency", .5f); Put(turb, "octaves", 2); Put(turb, "roughness", .5f);
            PutFields(turb, "FieldTransform", ("scale", Vector3.one));
        });

        // ⓔ the ground pool: the red-black wash painted on the ground round the target (fire308.py pool: one 16-cel life - painted
        // on, held, dried to soot and lifted in patches), laid flat like the scorch streaks were, turned at random
        static string GraphBuildPool() => GraphBuildAt(NaPoolGraph, 32u, (init, update, output) =>
        {
            var disc = Block(init, "PositionShape", ("shape", "Circle"), ("compositionPosition", "Overwrite"), ("positionMode", "Volume"));
            Space(disc, "arcCircle", "Local");
            PutPath(disc, "arcCircle", "circle.transform.position", new Vector3(0f, .04f, 6f));   // just clear of z-fighting
            PutPath(disc, "arcCircle", "circle.transform.angles", new Vector3(90f, 0f, 0f));
            PutPath(disc, "arcCircle", "circle.radius", .3f);
            var first = Block(init, "SetAttribute", ("attribute", "texIndex"), ("Composition", "Overwrite"), ("Random", "Off"));
            PutOnly(first, 0f);
            var o = Orient(output); Setting(o, "mode", "Advanced");
            Space(o, "AxisZ", "Local"); Space(o, "AxisY", "Local");
            Put(o, "AxisZ", Vector3.up); Put(o, "AxisY", Vector3.forward);
            Setting(output, "cullMode", "Off"); Setting(output, "useSoftParticle", false);
            Setting(output, "primitiveType", "Quad");
        });

        // fire17 (user 2026-10-16 "네가 자유롭게 만든다면 어떻게 진행할거야? 그렇게 진행해줘"): the fire is a baked fluid sim
        // (fire17_fluid.py band: 4 variants x 16-cel lives of a 2D heat / smoke sim painted as red-ink brush fire) stood round the
        // target on camera-facing quads, over one splash stain laid flat on the ground (fire17_fluid.py stain)
        const string NaBankGraph = "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_bank.vfx";
        const string NaStainGraph = "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_stain.vfx";
        // the quad the lab composite judged: 2.6 m x 3.85 m, standing on the cel's root line 1.03 of the cel height from the top
        const float BankCels = 16f, BankAspect = 2.6f / 3.85f, BankPivot = .5f - 1.03f;
        // 11.69 D308-113 (the cone picture = the judgment, CombatConfig +-40 deg x 10 m): the ring was centre 6 m, r 3.1 - reach 9.1 m
        // but only asin(3.1 / 6) = 31 deg wide. Centre 6.3, r 3.7: reach 10.0 m, asin(3.7 / 6.3) = 36 deg (both inside the 10 %
        // the rubric allows); the band stays 1.0 m wide (D308-104 cap: thickness 1 / 3.7)
        internal const float BankCentre = 6.3f, BankRadius = 3.7f, BankThickness = .27f, BankWiden = BankRadius / 3.1f;

        // ⓕ the fire bank: born on a band r 2.1-3.1 m round the target, facing the camera plane, standing on its root, one of the
        // 4 variants for its whole life (played from its first cel - the sheet's births rise out of the ground), mirrored half
        // the time, sized .65-1.35. "가까운 쪽 낮게" (near side lower) is TBD: uniform scale this round
        static string GraphBuildBank() => GraphBuildAt(NaBankGraph, 190u, (init, update, output) => BankRecipe(init, output, new Vector3(90f, 0f, 0f), 0f, 7u, 3u));
        // arc 0 = the full circle (the sample's default arc); else the band covers local angles 0..arc, turned by angles.z
        static void BankRecipe(object init, object output, Vector3 angles, float arc, uint flipSeed, uint pickSeed)
        {
            var ring = Block(init, "PositionShape", ("shape", "Circle"), ("compositionPosition", "Overwrite"), ("positionMode", "ThicknessRelative"));
            Space(ring, "arcCircle", "Local");
            PutPath(ring, "arcCircle", "circle.transform.position", new Vector3(0f, .05f, BankCentre));
            PutPath(ring, "arcCircle", "circle.transform.angles", angles);
            PutPath(ring, "arcCircle", "circle.radius", BankRadius);
            if (arc > 0f) PutPath(ring, "arcCircle", "arc", arc);
            Put(ring, "Thickness", BankThickness);   // band 2.7-3.7 m (1/3.7 as a folded constant does not read back bit-equal)
            var pivot = Block(init, "SetAttribute", ("attribute", "pivot"), ("Composition", "Overwrite"), ("Random", "Off"));
            PutOnly(pivot, new Vector3(0f, BankPivot, 0f));
            // one random lerp over all three channels keeps the quad's aspect; the mirror after it multiplies x by -1 or 1
            var scale = Block(init, "SetAttribute", ("attribute", "scale"), ("Composition", "Overwrite"), ("Random", "Uniform"));
            Put(scale, "A", new Vector3(BankAspect, 1f, 1f) * .65f); Put(scale, "B", new Vector3(BankAspect, 1f, 1f) * 1.35f);
            var flip = Block(init, "AttributeFromMap", ("attribute", "scale"), ("Composition", "Multiply"), ("SampleMode", "RandomConstantPerParticle"), ("channels", "X"));
            Put(flip, "attributeMap", BankMapShared(BankFlipMapPath, "T_V308_bank_flip", new[] { -1f, 1f })); Put(flip, "Seed", flipSeed);
            var pick = Block(init, "AttributeFromMap", ("attribute", "texIndex"), ("Composition", "Overwrite"), ("SampleMode", "RandomConstantPerParticle"));
            Put(pick, "attributeMap", BankMapShared(BankVariantMapPath, "T_V308_bank_variants", new[] { 0f, BankCels, 2f * BankCels, 3f * BankCels })); Put(pick, "Seed", pickSeed);
            Setting(Orient(output), "mode", "FaceCameraPlane");
            Setting(output, "cullMode", "Off");   // a mirrored quad shows its back face
            Setting(output, "primitiveType", "Quad");
        }
        const string BankVariantMapPath = "Assets/_Project/Art/SpellVFX308/V2/Graphs/T_V308_bank_variants.asset";
        const string BankFlipMapPath = "Assets/_Project/Art/SpellVFX308/V2/Graphs/T_V308_bank_flip.asset";
        static Texture2D BankMap(string path, string name, float[] values)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            var t = new Texture2D(values.Length, 1, TextureFormat.RFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            t.SetPixelData(values, 0);
            t.Apply(false, false);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }
        // fire18: the maps are shared by the full bank and the five arcs - re-creating one gives it a new GUID and every graph
        // built before loses its reference, so an existing map with the same values is kept
        static Texture2D BankMapShared(string path, string name, float[] values)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            try { if (t != null && t.width == values.Length && t.format == TextureFormat.RFloat && t.GetPixelData<float>(0).ToArray().SequenceEqual(values)) return t; }
            catch (Exception) { }   // not readable: made again below
            return BankMap(path, name, values);
        }

        // fire18 (user 2026-10-16 "시전자 쪽에서 작게 시작된 불씨가 시전 지역으로 가서 번지는듯한 느낌으로. 지금 vfx는 아주 좋아"): the
        // 17th bank cut into five 72° arcs, one graph each, so the seam opens them in turn - the near side where the ember lands,
        // then the two sides, then the far side (each host's Vfx308SeamTiming.Lead). The Circle block samples
        // (sin θ, cos θ, 0) (PositionCircle.cs), θ in 0..72°; turned by angles (90, 0, phi) (Z, then X, then Y) a particle lands at
        // host-local (sin(θ-phi), 0, cos(θ-phi)), i.e. world angle 90+phi-θ from +X toward +Z, so the arc covers
        // phi+18..phi+90° round the target (270° = toward the caster). Near = 234..306 -> phi 216. Each arc draws its own
        // variant / mirror per particle (distinct seeds), 19/s each = the 17th bank's 95/s in all
        static readonly (string name, string path, float phi, uint flipSeed, uint pickSeed, float lead)[] BankArcs =
        {
            ("BankVfx_near", "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_bank_near.vfx", 216f, 11u, 13u, -.12f),
            ("BankVfx_sideL", "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_bank_sideL.vfx", 144f, 17u, 19u, -.20f),
            ("BankVfx_sideR", "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_bank_sideR.vfx", 288f, 23u, 29u, -.20f),
            ("BankVfx_farL", "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_bank_farL.vfx", 72f, 31u, 37u, -.28f),
            ("BankVfx_farR", "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_bank_farR.vfx", 0f, 41u, 43u, -.28f),
        };
        // 11.69: capacity x 1.19 with the rate (BankWiden), so the longer band keeps the 17th's headroom
        static string GraphBuildBanks()
        {
            var sb = new StringBuilder();
            foreach (var b in BankArcs)
                sb.Append(GraphBuildAt(b.path, 48u, (init, update, output) => BankRecipe(init, output, new Vector3(90f, 0f, b.phi), 1.2566f, b.flipSeed, b.pickSeed))).Append('\n');
            return sb.ToString();
        }

        // fire18 the ember: a few small tongues thrown once from the brush tip (one burst at Play, the spawner's constant rate is 0
        // on the host), arcing down to the near side of the ring. No trail, nothing left on the ground
        const string NaEmberGraph = "Assets/_Project/Art/SpellVFX308/V2/Graphs/VFX_V308_na_ember.vfx";
        static string GraphBuildEmber() => GraphBuildAt(NaEmberGraph, 16u, (spawner, init, update, output) =>
        {
            var burst = Block(spawner, "VFXSpawnerBurst", ("repeat", "Single"), ("spawnMode", "Constant"), ("delayMode", "Constant"));
            Put(burst, "Count", 4f); Put(burst, "Delay", 0f);
            Setting(output, "primitiveType", "Quad");   // the octagon clips the tongue cels' corners
        });

        // ⓖ the stain: the splash stain (soot inside, a ragged red-black band at r 2.55, flung rays) laid flat on the ground under
        // the bank. Capacity 1: the first spawn of the emit window lands at once, the rest are dropped while it lives
        static string GraphBuildStain() => GraphBuildAt(NaStainGraph, 1u, (init, update, output) =>
        {
            var at = Block(init, "PositionShape", ("shape", "Circle"), ("compositionPosition", "Overwrite"), ("positionMode", "Volume"));
            Space(at, "arcCircle", "Local");
            PutPath(at, "arcCircle", "circle.transform.position", new Vector3(0f, .03f, BankCentre));   // just clear of z-fighting, under the pool's .04; 11.69 under the moved bank
            PutPath(at, "arcCircle", "circle.transform.angles", new Vector3(90f, 0f, 0f));
            PutPath(at, "arcCircle", "circle.radius", .01f);
            var first = Block(init, "SetAttribute", ("attribute", "texIndex"), ("Composition", "Overwrite"), ("Random", "Off"));
            PutOnly(first, 0f);
            // 19th pass ("자국도 불 따라 늦게"): the stainwipe sheet reveals near -> far over its 16 cels; run them in the first
            // .106 of the life (.36 of 3.4 s) and hold the whole stain. Added last in update so it overwrites the flipbook player;
            // the hold is exactly 15 (the player wraps, 16 would be cel 0)
            var wipe = Block(update, "AttributeFromCurve", ("attribute", "texIndex"), ("Composition", "Overwrite"), ("SampleMode", "OverLife"));
            const float wipeEnd = .106f, slope = 15f / wipeEnd;
            Put(wipe, "TexIndex", new AnimationCurve(new Keyframe(0f, 0f, 0f, slope), new Keyframe(wipeEnd, 15f, slope, 0f), new Keyframe(1f, 15f, 0f, 0f)));
            var o = Orient(output); Setting(o, "mode", "Advanced");
            Space(o, "AxisZ", "Local"); Space(o, "AxisY", "Local");
            Put(o, "AxisZ", Vector3.up); Put(o, "AxisY", Vector3.forward);
            Setting(output, "cullMode", "Off"); Setting(output, "useSoftParticle", false);
            Setting(output, "primitiveType", "Quad");
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
