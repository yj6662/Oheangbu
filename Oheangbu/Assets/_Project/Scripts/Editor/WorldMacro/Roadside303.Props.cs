using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class Roadside303
    {
        const float WheelR = .6f, AxleHalf = .86f;
        const string Sack = "Assets/KoreanTraditionalFestival/Prefabs/SM_SackOfRice.prefab";
        const string Crate = "Assets/HwaseongHaenggung/Prefabs/SM_M_WoodenBox.prefab";
        const string Lattice = "Assets/HwaseongHaenggung/Prefabs/SM_M_LatticeSmall.prefab";
        const string Thatched = "Assets/_Project/Art/CodexWorld/ThatchedInn/ThatchedInn.prefab";

        // ---------- cart (소달구지): two spoked wheels, axle, plank bed with rails, two shafts and a yoke bar ----------

        static Mesh WheelMesh(string name, bool broken)
        {
            var mb = new MeshBuilder303(); const int rim = 16;
            for (int i = 0; i < rim; i++)
            {
                if (broken && i >= 11 && i <= 13) continue;   // a burst felloe
                float a = (i + .5f) * Mathf.PI * 2f / rim;
                var pos = new Vector3(0, Mathf.Sin(a), Mathf.Cos(a)) * (WheelR - .045f);
                mb.Box(Matrix4x4.TRS(pos, Quaternion.Euler(-(a * Mathf.Rad2Deg + 90f), 0, 0), Vector3.one), new Vector3(.09f, .08f, 2f * Mathf.PI * WheelR / rim * 1.05f));
            }
            for (int i = 0; i < 10; i++)
            {
                if (broken && (i == 7 || i == 8)) continue;
                float a = i * Mathf.PI * 2f / 10f; var dir = new Vector3(0, Mathf.Sin(a), Mathf.Cos(a));
                mb.Beam(dir * .09f, dir * (WheelR - .08f), .045f);
            }
            mb.Cylinder(Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), Vector3.one), .11f, .22f, 12);
            return SaveMesh(mb, name);
        }

        // cart-local: origin on the ground under the axle centre, +z toward the shafts
        static Mesh BodyMesh(string name, System.Random rng, float axleY, int missingPlanks, bool rails, bool leftShaftStub, bool yoke)
        {
            var mb = new MeshBuilder303(); float y = axleY;
            mb.Box(Matrix4x4.Translate(new Vector3(0, y, 0)), new Vector3(AxleHalf * 2f - .1f, .09f, .09f));
            foreach (float x in new[] { -.42f, .42f }) mb.Beam(new Vector3(x, y + .1f, -1.05f), new Vector3(x, y + .1f, 1.1f), .1f);
            var skip = new HashSet<int>(); while (skip.Count < Mathf.Min(missingPlanks, 5)) skip.Add(rng.Next(0, 6));
            for (int i = 0; i < 6; i++)
            {
                if (skip.Contains(i)) continue;
                float x = -.45f + i * .18f;
                mb.Box(Matrix4x4.TRS(new Vector3(x, y + .18f, R(rng, -.03f, .03f)), Quaternion.Euler(0, R(rng, -1.5f, 1.5f), 0), Vector3.one), new Vector3(.17f, .05f, 2.0f + R(rng, -.1f, .04f)));
            }
            if (rails)
                foreach (float x in new[] { -.53f, .53f })
                {
                    foreach (float z in new[] { -.9f, 0f, .9f }) mb.Beam(new Vector3(x, y + .16f, z), new Vector3(x, y + .62f, z), .05f);
                    mb.Beam(new Vector3(x, y + .6f, -.98f), new Vector3(x, y + .6f, .98f), .06f);
                }
            mb.Box(Matrix4x4.Translate(new Vector3(0, y + .33f, -1.0f)), new Vector3(1.1f, .28f, .04f));
            mb.Beam(new Vector3(.42f, y + .12f, 1.0f), new Vector3(.30f, y + .12f, 3.6f), .08f);
            if (leftShaftStub) mb.Beam(new Vector3(-.42f, y + .12f, 1.0f), new Vector3(-.39f, y + .12f, 2.05f), .08f);
            else mb.Beam(new Vector3(-.42f, y + .12f, 1.0f), new Vector3(-.30f, y + .12f, 3.6f), .08f);
            if (yoke && !leftShaftStub) mb.Beam(new Vector3(-.36f, y + .12f, 3.42f), new Vector3(.36f, y + .12f, 3.42f), .07f);
            return SaveMesh(mb, name);
        }

        static Transform Wheel(Transform parent, Mesh mesh, Material m, Vector3 local, Quaternion rotation, bool collider)
        {
            var go = MeshObject(parent, "Wheel", mesh, m, collider);
            go.transform.localPosition = local; go.transform.localRotation = rotation;
            return go.transform;
        }

        // the terrain normal at p from four samples (props lean with the slope they sit on)
        static Vector3 TerrainNormal(Vector3 p, float r)
        {
            if (!Ground(p + Vector3.right * r, null, out var a) || !Ground(p - Vector3.right * r, null, out var b) ||
                !Ground(p + Vector3.forward * r, null, out var c) || !Ground(p - Vector3.forward * r, null, out var d)) return Vector3.up;
            var n = Vector3.Cross(c.point - d.point, a.point - b.point).normalized;
            return n.y < 0 ? -n : n;
        }

        static void Cart(Transform site, SiteSpec s, Mats303 m, System.Random rng, Transform root)
        {
            bool burnt = s.kind == "cart_burnt"; var wood = burnt ? m.Charred : m.Wood; string id = s.id;
            var cart = new GameObject("Cart").transform; cart.SetParent(site, false);
            // roughly along the road (site +z faces the road), either way round
            float yaw = 90f + R(rng, -30f, 30f) + (rng.NextDouble() < .5 ? 180f : 0f);
            var wheel = WheelMesh(id + "_wheel", false);
            var brokenWheel = WheelMesh(id + "_wheelBroken", true);
            Vector3 spill = new Vector3(R(rng, -.4f, .4f), 0, -1f).normalized;   // away from the road (downhill on downhill sites)
            float axleY = WheelR; float pitch = Mathf.Atan2(WheelR + .12f, 3.6f) * Mathf.Rad2Deg, roll = 0;
            Transform body;
            switch (s.kind)
            {
                case "cart_empty":
                    body = MeshObject(cart, "Body", BodyMesh(id + "_body", rng, axleY, rng.Next(0, 2), true, false, true), wood, true).transform;
                    Wheel(cart, wheel, wood, new Vector3(-AxleHalf, axleY, 0), Quaternion.Euler(R(rng, 0, 36), 0, 0), true);
                    Wheel(cart, wheel, wood, new Vector3(AxleHalf, axleY, 0), Quaternion.Euler(R(rng, 0, 36), 0, 0), true);
                    break;
                case "cart_overturned":
                    body = MeshObject(cart, "Body", BodyMesh(id + "_body", rng, axleY, rng.Next(1, 3), true, true, false), wood, true).transform;
                    Wheel(cart, wheel, wood, new Vector3(AxleHalf, axleY, 0), Quaternion.Euler(R(rng, 0, 36), 0, 0), true);
                    pitch = R(rng, -4f, 4f); roll = 100f + R(rng, -6f, 6f);
                    break;
                case "cart_axlebroken":
                case "cart_burnt":
                    body = MeshObject(cart, "Body", BodyMesh(id + "_body", rng, axleY, burnt ? rng.Next(2, 4) : rng.Next(0, 2), !burnt, false, !burnt), wood, true).transform;
                    Wheel(cart, burnt ? brokenWheel : wheel, wood, new Vector3(AxleHalf, axleY, 0), Quaternion.Euler(R(rng, 0, 36), 0, 0), true);
                    pitch = pitch * .6f; roll = -15f + R(rng, -3f, 3f);
                    break;
                default: // cart_stripped: wheels taken off, the bed propped on two stones
                    axleY = .42f; pitch = Mathf.Atan2(axleY + .12f, 3.6f) * Mathf.Rad2Deg;
                    body = MeshObject(cart, "Body", BodyMesh(id + "_body", rng, axleY, rng.Next(1, 3), false, false, true), wood, true).transform;
                    foreach (float x in new[] { -AxleHalf + .1f, AxleHalf - .1f })
                    {
                        var mb = new MeshBuilder303(); mb.Box(Matrix4x4.Translate(new Vector3(0, .19f, 0)), new Vector3(.34f, .38f, .32f));
                        var stone = MeshObject(cart, "PropStone", SaveMesh(mb, id + "_stone" + (x < 0 ? "L" : "R")), m.FieldStone, true);
                        stone.transform.localPosition = new Vector3(x, 0, 0); stone.transform.localRotation = Quaternion.Euler(0, R(rng, -12, 12), 0);
                    }
                    break;
            }
            // pose: lean with the ground, then the variant's own pitch/roll, then seat the lowest point
            var normal = TerrainNormal(site.position, 1.6f);
            cart.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, site.eulerAngles.y + yaw, 0) * Quaternion.Euler(pitch, 0, roll);
            Seat(cart, site.TransformPoint(new Vector3(0, 0, s.downhill ? -.4f : 0)), roll != 0 ? .06f : .03f);

            // loose wheels
            if (s.kind == "cart_overturned")
                LooseWheel(site, wheel, wood, rng, site.TransformPoint(spill * 2.4f + new Vector3(1.4f, 0, 0)), flat: true);
            else if (s.kind == "cart_axlebroken" || s.kind == "cart_burnt")
                LooseWheel(site, burnt ? brokenWheel : wheel, wood, rng, cart.TransformPoint(new Vector3(-AxleHalf - .75f, 0, R(rng, -.4f, .4f))), flat: rng.NextDouble() < .5);
            else if (s.kind == "cart_stripped")
            {
                LooseWheel(site, wheel, wood, rng, cart.TransformPoint(new Vector3(-AxleHalf - 1.2f, 0, -.6f)), flat: true);
                LooseWheel(site, wheel, wood, rng, cart.TransformPoint(new Vector3(-AxleHalf - 1.25f, 0, .7f)), flat: true);
            }
            if (s.kind == "cart_overturned")
            {
                var mb = new MeshBuilder303(); mb.Beam(Vector3.zero, new Vector3(0, 0, 1.6f), .08f);
                var piece = MeshObject(site, "SnappedShaft", SaveMesh(mb, id + "_shaftPiece"), wood, false);
                piece.transform.rotation = Quaternion.Euler(0, site.eulerAngles.y + yaw + R(rng, 20, 60), 0);
                Seat(piece.transform, cart.TransformPoint(new Vector3(-.6f, 0, 2.9f)), .03f);
            }
            Cargo(site, cart, s, m, rng, spill);
        }

        static void LooseWheel(Transform site, Mesh mesh, Material m, System.Random rng, Vector3 at, bool flat)
        {
            var w = MeshObject(site, "LooseWheel", mesh, m, true).transform;
            w.rotation = flat ? Quaternion.Euler(0, R(rng, 0, 360), 90f + R(rng, -4, 4)) : Quaternion.Euler(R(rng, -8, 8), R(rng, 0, 360), 72f + R(rng, -6, 6));
            Seat(w, at, flat ? .02f : .05f);
        }

        static void Cargo(Transform site, Transform cart, SiteSpec s, Mats303 m, System.Random rng, Vector3 spill)
        {
            bool onBed = s.kind == "cart_axlebroken" || s.kind == "cart_empty";
            switch (s.cargo)
            {
                case "rice":
                    for (int i = 0; i < 4; i++)
                    {
                        var at = site.TransformPoint(spill * R(rng, 1.4f, 3.2f) + new Vector3(R(rng, -1.6f, 1.6f), 0, 0));
                        var sack = PrefabCopy(site, "SpilledSack", Sack, site.InverseTransformPoint(at), R(rng, 0, 360), new Vector3(R(rng, -8, 8), 0, R(rng, 70, 95)), R(rng, .78f, .9f));
                    }
                    if (onBed)
                        for (int i = 0; i < 2; i++)
                        {
                            var sack = PrefabCopy(cart, "BedSack", Sack, Vector3.zero, 0, Vector3.zero, .82f);
                            sack.localRotation = Quaternion.Euler(0, 90 + R(rng, -10, 10), 90);
                            sack.localPosition = new Vector3(R(rng, -.2f, .2f), WheelR + .45f, -.5f + i * .8f);
                        }
                    break;
                case "herbs":
                    for (int i = 0; i < 4; i++)
                    {
                        var mb = new MeshBuilder303(); mb.Box(Matrix4x4.Translate(new Vector3(0, .1f, 0)), new Vector3(.36f, .2f, .26f));
                        mb.Box(Matrix4x4.Translate(new Vector3(0, .2f, 0)), new Vector3(.04f, .02f, .28f));   // cord
                        var b = MeshObject(site, "HerbBundle", SaveMesh(mb, s.id + "_herb" + i), i == 0 ? m.Paper : m.Hemp, false).transform;
                        b.rotation = Quaternion.Euler(R(rng, -12, 12), R(rng, 0, 360), R(rng, -12, 12));
                        Seat(b, site.TransformPoint(i == 0 ? new Vector3(.5f, 0, .9f) : spill * R(rng, 1.2f, 2.8f) + new Vector3(R(rng, -1.2f, 1.2f), 0, 0)), .02f);
                    }
                    break;
                case "seized":
                    for (int i = 0; i < 2; i++)
                        PrefabCopy(site, "EmptiedCrate", Crate, spill * 1.2f + new Vector3(1.6f + i * 1.1f, 0, R(rng, -.4f, .4f)), R(rng, 0, 360), i == 0 ? new Vector3(0, 0, 88) : Vector3.zero, R(rng, .8f, 1.0f), m.Wood, collider: i == 1);
                    for (int i = 0; i < 5; i++)
                    {
                        var mb = new MeshBuilder303(); mb.Box(Matrix4x4.identity, new Vector3(.16f, .04f, R(rng, .8f, 1.6f)));
                        var p = MeshObject(site, "PriedPlank", SaveMesh(mb, s.id + "_plank" + i), m.Wood, false).transform;
                        p.rotation = Quaternion.Euler(0, R(rng, 0, 360), R(rng, -4, 4));
                        Seat(p, site.TransformPoint(spill * R(rng, .8f, 2.2f) + new Vector3(R(rng, -2f, 2.4f), 0, 0)), .01f);
                    }
                    break;
                case "ash":
                    for (int i = 0; i < 4; i++)
                    {
                        var mb = new MeshBuilder303(); mb.Box(Matrix4x4.identity, new Vector3(R(rng, .4f, .8f), .06f, R(rng, .35f, .7f)));
                        var a = MeshObject(site, "AshHeap", SaveMesh(mb, s.id + "_ash" + i), m.Ash, false).transform;
                        a.rotation = Quaternion.Euler(0, R(rng, 0, 360), 0);
                        Seat(a, cart.TransformPoint(new Vector3(R(rng, -1.2f, 1.2f), 0, R(rng, -1.2f, 1.6f))), .03f);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        var mb = new MeshBuilder303(); mb.Box(Matrix4x4.identity, new Vector3(.15f, .05f, R(rng, .4f, 1.1f)));
                        var p = MeshObject(site, "CharredPlank", SaveMesh(mb, s.id + "_charred" + i), m.Charred, false).transform;
                        p.rotation = Quaternion.Euler(0, R(rng, 0, 360), R(rng, -6, 6));
                        Seat(p, site.TransformPoint(spill * R(rng, .6f, 2f) + new Vector3(R(rng, -2f, 2f), 0, 0)), .01f);
                    }
                    break;
            }
        }

        // ---------- 지게 (A-frame carrier) left on its prop stick, an axe and scattered firewood ----------

        static Mesh JigeMesh(string name)
        {
            var mb = new MeshBuilder303();
            foreach (float s in new[] { -1f, 1f })
            {
                mb.Beam(new Vector3(.2f * s, 0, 0), new Vector3(.15f * s, 1.6f, 0), .06f);
                mb.Beam(new Vector3(.19f * s, .5f, 0), new Vector3(.25f * s, .86f, .42f), .05f);   // 가지
            }
            foreach (float y in new[] { .5f, .8f, 1.1f, 1.36f }) { float w = Mathf.Lerp(.2f, .15f, y / 1.6f); mb.Beam(new Vector3(-w, y, 0), new Vector3(w, y, 0), .045f); }
            return SaveMesh(mb, name);
        }

        static Transform JigeAt(Transform parent, Mats303 m, System.Random rng, Vector3 at, float yaw, string name)
        {
            var j = MeshObject(parent, "Jige", JigeMesh(name), m.Wood, false).transform;
            j.rotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-14f, 0, 0);
            Seat(j, at, .03f);
            var top = j.TransformPoint(new Vector3(0, 1.3f, 0));
            var foot = j.TransformPoint(new Vector3(0, 0, -.95f)); if (Ground(foot, null, out var fh)) foot = fh.point;
            var mb = new MeshBuilder303(); mb.Beam(Vector3.zero, top - foot, .04f);
            var stick = MeshObject(parent, "PropStick", SaveMesh(mb, name + "_stick"), m.Wood, false);
            stick.transform.position = foot; stick.transform.rotation = Quaternion.identity;
            var pad = new MeshBuilder303(); pad.Box(Matrix4x4.Translate(new Vector3(0, .98f, -.06f)), new Vector3(.3f, .26f, .05f));
            var p = MeshObject(j, "BackPad", SaveMesh(pad, name + "_pad"), m.Hemp, false); p.transform.localPosition = Vector3.zero; p.transform.localRotation = Quaternion.identity; p.transform.localScale = Vector3.one;
            return j;
        }

        static void Jige(Transform site, Mats303 m, System.Random rng, Transform root)
        {
            JigeAt(site, m, rng, site.position, site.eulerAngles.y + R(rng, 150, 210), site.name + "_jige");
            var handle = new MeshBuilder303(); handle.Beam(Vector3.zero, new Vector3(0, 0, .72f), .035f);
            var axe = MeshObject(site, "AxeHandle", SaveMesh(handle, site.name + "_axeHandle"), m.Wood, false).transform;
            axe.rotation = Quaternion.Euler(0, R(rng, 0, 360), 0); Seat(axe, site.TransformPoint(new Vector3(.9f, 0, .4f)), .01f);
            var head = new MeshBuilder303(); head.Box(Matrix4x4.identity, new Vector3(.03f, .1f, .17f));
            var ah = MeshObject(axe, "AxeHead", SaveMesh(head, site.name + "_axeHead"), m.Iron, false).transform;
            ah.localPosition = new Vector3(0, .03f, .7f); ah.localRotation = Quaternion.Euler(0, 0, 90);
            for (int i = 0; i < 6; i++)
            {
                var mb = new MeshBuilder303(); mb.Beam(Vector3.zero, new Vector3(0, 0, R(rng, .45f, .9f)), R(rng, .04f, .07f));
                var w = MeshObject(site, "Firewood", SaveMesh(mb, site.name + "_wood" + i), m.Wood, false).transform;
                w.rotation = Quaternion.Euler(0, R(rng, 0, 360), 0);
                Seat(w, site.TransformPoint(new Vector3(R(rng, -1.4f, 1.4f), 0, R(rng, -1.2f, 1.0f))), .01f);
            }
        }

        // ---------- 초가 (ThatchedInn parts): lived-in, abandoned (door gone) or collapsed (roof sunk onto its front eave) ----------

        static void House(Transform site, string kind, Mats303 m, System.Random rng, Transform root)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Thatched) ?? throw new Exception("missing " + Thatched);
            bool home = kind == "house_home", ruin = kind == "house_collapsed", empty = kind == "house_abandoned";
            const float scale = .72f;
            var house = new GameObject("House").transform; house.SetParent(site, false); house.localScale = Vector3.one * scale;
            var roof = new GameObject("Roof").transform; roof.SetParent(house, false);
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = f.GetComponent<MeshRenderer>(); if (r == null || f.sharedMesh == null) continue;
                string n = f.name; bool lantern = f.transform.parent != null && f.transform.parent.name.Contains("Lantern");
                if (!home && lantern) continue;
                if ((ruin || empty) && n == "house_Re_Door") continue;
                if (n == "h1_house_ground") continue;   // the prefab's yard block (2.6 m tall) would stand up out of the slope; the skirt replaces it
                if (ruin && (n == "house_Re_Ground_02" || n == "house_Re_Ground_04" || n == "house_Re_Roof_ACC" || n.StartsWith("Inn_Entry_Step"))) continue;
                if (empty && n == "house_Re_Ground_04") continue;
                bool isRoof = n.StartsWith("house_Re_Roof");
                var g = new GameObject(n, typeof(MeshFilter), typeof(MeshRenderer)); g.transform.SetParent(isRoof ? roof : house, false);
                var mm = prefab.transform.worldToLocalMatrix * f.transform.localToWorldMatrix;
                g.transform.localPosition = mm.GetColumn(3); g.transform.localRotation = mm.rotation; g.transform.localScale = mm.lossyScale;
                g.GetComponent<MeshFilter>().sharedMesh = f.sharedMesh;
                // left alone for years: thatch and plaster go dark and grey (tinted copies; the shared originals stay)
                g.GetComponent<MeshRenderer>().sharedMaterials = home ? r.sharedMaterials : r.sharedMaterials.Select(x => Aged(x, isRoof ? .62f : .78f)).ToArray();
                if (n == "house_Re_Stone" || n == "house_Re_Ground_03" || (isRoof && n == "house_Re_Roof_01" && ruin))
                { var bc = g.AddComponent<BoxCollider>(); bc.center = f.sharedMesh.bounds.center; bc.size = f.sharedMesh.bounds.size; }
            }
            if (ruin || empty)
            {
                // pivot the roof on its back eave line: the ruin's front sinks onto the ground, the empty house only sags
                var pivot = new Vector3(0, 3.2f, -4.9f);
                foreach (Transform c in roof) c.localPosition -= pivot;
                roof.localPosition = pivot;
                roof.localRotation = ruin ? Quaternion.Euler(14f, R(rng, -2f, 2f), R(rng, -3f, 3f)) : Quaternion.Euler(2.6f, 0, R(rng, -1.2f, 1.2f));
                roof.localPosition += ruin ? new Vector3(0, -1.15f, .2f) : new Vector3(0, -.14f, 0);
            }
            // seat on the highest ground under the footprint so the stone base sinks into the slope instead of floating
            Physics.SyncTransforms();
            float top = float.MinValue;
            for (int k = 0; k < 9; k++)
            {
                var q = site.TransformPoint(new Vector3((k % 3 - 1) * 3.6f, 0, (k / 3 - 1) * 3.4f));
                if (Ground(q, null, out var h)) top = Mathf.Max(top, h.point.y);
            }
            if (top > float.MinValue) house.position = new Vector3(house.position.x, top - .35f, house.position.z);
            // a stone skirt down into the ground hides any gap on the downhill side
            var skirt = new MeshBuilder303(); skirt.Box(Matrix4x4.Translate(new Vector3(-.3f, -.75f, .8f)), new Vector3(10.4f, 2.1f, 9.3f));   // under the stone base only (top at +.3)
            var sk = MeshObject(house, "StoneSkirt", SaveMesh(skirt, site.name + "_skirt"), m.Stone, false); sk.transform.localPosition = Vector3.zero; sk.transform.localRotation = Quaternion.identity; sk.transform.localScale = Vector3.one;

            if (ruin || empty)
            {
                // the fallen lattice door lies in the yard
                PrefabCopy(site, "FallenDoor", Lattice, new Vector3(R(rng, -1.5f, 1.5f), 0, 4.8f), R(rng, 0, 360), new Vector3(86, 0, 0), 1.6f, m.Wood);
            }
            if (ruin)
            {
                // rafters sliding out from under the sunk eave, rubble along the front, the cupboard and the tablet under a beam
                for (int i = 0; i < 7; i++)
                {
                    var a = site.TransformPoint(new Vector3(-3.4f + i * 1.1f + R(rng, -.2f, .2f), 0, 2.8f));
                    var b = site.TransformPoint(new Vector3(-3.6f + i * 1.15f + R(rng, -.4f, .4f), 0, 5.2f + R(rng, -.4f, .6f)));
                    if (Ground(a, null, out var ha)) a = ha.point + Vector3.up * .45f; if (Ground(b, null, out var hb)) b = hb.point + Vector3.up * .05f;
                    var mb = new MeshBuilder303(); mb.Beam(Vector3.zero, b - a, .09f, R(rng, 0, 40));
                    var raf = MeshObject(site, "FallenRafter", SaveMesh(mb, site.name + "_rafter" + i), m.Wood, false); raf.transform.position = a; raf.transform.rotation = Quaternion.identity;
                }
                for (int i = 0; i < 16; i++)
                {
                    var mb = new MeshBuilder303(); mb.Box(Matrix4x4.identity, new Vector3(R(rng, .18f, .42f), R(rng, .12f, .26f), R(rng, .2f, .45f)));
                    var chip = MeshObject(site, "Rubble", SaveMesh(mb, site.name + "_rubble" + i), m.FieldStone, false).transform;
                    chip.rotation = Quaternion.Euler(R(rng, -20, 20), R(rng, 0, 360), R(rng, -20, 20));
                    Seat(chip, site.TransformPoint(new Vector3(R(rng, -4.5f, 4.5f), 0, R(rng, 3.0f, 5.2f))), .06f);
                }
                var chest = PrefabCopy(site, "Cupboard", Crate, new Vector3(.7f, 0, 3.4f), site.eulerAngles.y + 8f, new Vector3(0, 0, 18), .9f, m.Wood);
                var tab = new MeshBuilder303();
                tab.Box(Matrix4x4.Translate(new Vector3(0, .02f, 0)), new Vector3(.16f, .04f, .08f));
                tab.Box(Matrix4x4.Translate(new Vector3(0, .19f, 0)), new Vector3(.1f, .3f, .03f));
                var tablet = MeshObject(site, "AncestralTablet", SaveMesh(tab, site.name + "_tablet"), m.Lacquer, false).transform;
                tablet.rotation = Quaternion.Euler(0, site.eulerAngles.y + 170f, 0);
                Seat(tablet, site.TransformPoint(new Vector3(1.25f, 0, 3.9f)), .0f);
                var beam = new MeshBuilder303(); beam.Beam(Vector3.zero, new Vector3(2.4f, -.5f, .3f), .14f);
                var fallen = MeshObject(site, "FallenBeam", SaveMesh(beam, site.name + "_beam"), m.Wood, true).transform;
                fallen.position = site.TransformPoint(new Vector3(-.4f, 0, 3.3f)) + Vector3.up * .9f; fallen.rotation = Quaternion.Euler(0, site.eulerAngles.y, 0);
            }
            if (home)
            {
                // a little life: firewood stack and a hemp bundle by the door
                for (int i = 0; i < 8; i++)
                {
                    var mb = new MeshBuilder303(); mb.Beam(Vector3.zero, new Vector3(1.1f, 0, 0), .09f);
                    var w = MeshObject(site, "FirewoodStack", SaveMesh(mb, site.name + "_stack" + i), m.Wood, false).transform;
                    w.position = site.TransformPoint(new Vector3(-4.6f, 0, 3.6f)); if (Ground(w.position, null, out var hw)) w.position = hw.point + Vector3.up * (.06f + (i / 3) * .09f) + site.forward * ((i % 3) * .1f);
                    w.rotation = Quaternion.Euler(0, site.eulerAngles.y + R(rng, -3, 3), 0);
                }
            }
        }

        static Material Aged(Material source, float k)
        {
            if (source == null) return null;
            string path = AssetFolder + "/Materials/Aged303_" + source.name.Replace('/', '_') + "_" + Mathf.RoundToInt(k * 100) + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(source); AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(source);
            foreach (var prop in new[] { "_BaseColor", "_Color" })
                if (m.HasProperty(prop)) { var c = m.GetColor(prop); float g = (c.r + c.g + c.b) / 3f; m.SetColor(prop, Color.Lerp(c, new Color(g, g, g, c.a), .35f) * new Color(k, k, k, 1)); }
            EditorUtility.SetDirty(m); return m;
        }

        // ---------- givers ----------

        static void Tint(GameObject giver, NpcSpec n, Mats303 m)
        {
            if (n.tint == null || n.tint.Length < 3) return;
            var c = new Color(n.tint[0], n.tint[1], n.tint[2]);
            foreach (var r in giver.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!r.name.StartsWith("C02_Mesh")) continue;
                var mats = r.sharedMaterials.ToArray();
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string path = AssetFolder + "/Materials/Giver303_" + n.id + "_" + i + ".mat";
                    var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (copy == null) { copy = new Material(mats[i]); AssetDatabase.CreateAsset(copy, path); } else copy.CopyPropertiesFromMaterial(mats[i]);
                    if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", copy.GetColor("_BaseColor") * c);
                    if (copy.HasProperty("_Color")) copy.SetColor("_Color", copy.GetColor("_Color") * c);
                    EditorUtility.SetDirty(copy); mats[i] = copy;
                }
                r.sharedMaterials = mats;
            }
        }

        static void GiverProp(GameObject giver, string prop, Mats303 m)
        {
            var t = giver.transform; var rng = new System.Random(StableHash(giver.name));
            switch (prop)
            {
                case "stick":
                {
                    var mb = new MeshBuilder303(); mb.Beam(Vector3.zero, new Vector3(-.03f, 1.18f, -.02f), .035f);
                    var s = MeshObject(t, "WalkingStick", SaveMesh(mb, giver.name + "_stick"), m.Wood, false);
                    s.transform.position = t.TransformPoint(new Vector3(.36f, 0, .14f)); s.transform.rotation = t.rotation;
                    break;
                }
                case "jige":
                    JigeAt(t, m, rng, t.TransformPoint(new Vector3(.95f, 0, -.35f)), t.eulerAngles.y + 200f, giver.name + "_jige");
                    break;
                case "sack":
                    PrefabCopy(t, "RiceSack", Sack, new Vector3(-.75f, 0, .2f), t.eulerAngles.y + 30f, new Vector3(0, 0, 88), .82f);
                    break;
            }
        }
    }
}
