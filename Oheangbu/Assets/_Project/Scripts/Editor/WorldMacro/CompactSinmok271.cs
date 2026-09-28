using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class CompactRebuildAuthoring
    {
        const string Output271 = Output + "/Sinmok271";
        static string Folder271 => Path.GetDirectoryName(FrontageScene249().path).Replace('\\', '/') + "/Sinmok271";
        public static string Sinmok271(string command)
        {
            if (command == "apply") return Apply271();
            if (command == "check") return Check271();
            if (command == "capture") return Capture271();
            if (command == "combat") { var result = CombatChecks263(); File.WriteAllText(Output271 + "/combat-checks.txt", result); return result; }
            throw new ArgumentException(command);
        }
        static T Asset271<T>(string name, Func<T> create) where T : Object
        {
            Directory.CreateDirectory(Folder271);
            string path = Folder271 + "/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) { asset = create(); asset.name = name; AssetDatabase.CreateAsset(asset, path); }
            return asset;
        }

        // Swept, elliptical wood volumes, merged by material. Positions and binding use actor space.
        // Parallel transported frames avoid the abrupt twist of a world-up frame at curved shoulders.
        sealed class WoodVolume271
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> triangles = new List<int>();
            readonly List<BoneWeight> weights = new List<BoneWeight>();
            readonly Transform[] bones;
            public int Parts;
            public WoodVolume271(Transform[] binding) { bones = binding; }
            public void Limb(Vector3[] p, Vector2[] r, string[] binding, int seed, int rings = 22, int sides = 14)
            {
                Parts++;
                Vector3 Point(float u)
                {
                    u = Mathf.Clamp(u, 0, p.Length - 1); int k = Mathf.Min(p.Length - 2, (int)u); float t = u - k;
                    Vector3 a = p[Math.Max(0, k - 1)], b = p[k], c = p[k + 1], d = p[Math.Min(p.Length - 1, k + 2)];
                    return .5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
                }
                int start = Vertices.Count; Vector3 previousAxis = (p[1] - p[0]).normalized;
                Vector3 across = Vector3.Cross(previousAxis, Mathf.Abs(previousAxis.y) > .9f ? Vector3.forward : Vector3.up).normalized;
                float length = 0; Vector3 previous = p[0];
                for (int i = 0; i <= rings; i++)
                {
                    float u = i / (float)rings * (p.Length - 1); int k = Math.Min(p.Length - 2, (int)u); float t = u - k;
                    Vector3 center = Point(u), axis = (Point(u + .012f) - Point(u - .012f)).normalized;
                    across = Quaternion.FromToRotation(previousAxis, axis) * across;
                    Vector3 other = Vector3.Cross(axis, across).normalized; previousAxis = axis;
                    length += Vector3.Distance(center, previous); previous = center;
                    Vector2 radius = Vector2.Lerp(r[k], r[k + 1], t);
                    int b0 = Array.FindIndex(bones, b => b.name == binding[Math.Min(k, binding.Length - 1)]);
                    int b1 = Array.FindIndex(bones, b => b.name == binding[Math.Min(k + 1, binding.Length - 1)]);
                    if (b0 < 0 || b1 < 0) throw new Exception("Missing creature binding");
                    for (int j = 0; j <= sides; j++)
                    {
                        float a = j / (float)sides * Mathf.PI * 2;
                        float groove = 1 + .10f * Mathf.Sin(a * 5 + seed + u * .7f) + .045f * Mathf.Sin(a * 11 + seed * 1.7f + u * 2);
                        float knot = 1 + .055f * Mathf.Sin(u * 9 + seed);
                        Vertices.Add(center + (across * (Mathf.Cos(a) * radius.x) + other * (Mathf.Sin(a) * radius.y)) * groove * knot);
                        uv.Add(new Vector2(j / (float)sides * Mathf.Max(.4f, radius.x * 3), length * .65f));
                        weights.Add(new BoneWeight { boneIndex0 = b0, boneIndex1 = b1, weight0 = 1 - t, weight1 = t });
                        if (i > 0 && j > 0) { int n = Vertices.Count - 1; triangles.AddRange(new[] { n - sides - 2, n - 1, n, n - sides - 2, n, n - sides - 1 }); }
                    }
                }
                for (int j = 1; j < sides; j++)
                {
                    triangles.AddRange(new[] { start, start + j + 1, start + j });
                    int last = start + rings * (sides + 1); triangles.AddRange(new[] { last, last + j, last + j + 1 });
                }
            }
            public Mesh Bake(string name, Matrix4x4[] poses)
            {
                var mesh = Asset271(name, () => new Mesh()); mesh.Clear(); mesh.indexFormat = IndexFormat.UInt32;
                for(int i=0;i<triangles.Count;i+=3){int swap=triangles[i+1];triangles[i+1]=triangles[i+2];triangles[i+2]=swap;}
                mesh.SetVertices(Vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
                mesh.boneWeights = weights.ToArray(); mesh.bindposes = poses;
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
            }
        }
        static string Apply271()
        {
            var scene = FrontageScene249(); if (scene.isDirty) throw new Exception("Candidate has unsaved edits.");
            Directory.CreateDirectory(Output271);
            var actor = VillageSession().Actors.Single(a => a.Id == WorldMacroPlaytestSession.SinmokId);
            var reference = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Trunk");
            var bones = reference.bones; var poses = reference.sharedMesh.bindposes;
            // The legacy sweep generator wound the tube walls inward. Correct only candidate root copies.
            foreach(var rootSkin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.StartsWith("RootPart")))
            {
                var original=AssetDatabase.LoadAssetAtPath<Mesh>(Folder263+"/Tree_"+rootSkin.name+".asset");
                var fixedRoot=Asset271("Ground_"+rootSkin.name,()=>Object.Instantiate(original));
                EditorUtility.CopySerialized(original,fixedRoot);var indices=fixedRoot.triangles;
                for(int i=0;i<indices.Length;i+=3){int swap=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=swap;}
                fixedRoot.triangles=indices;fixedRoot.RecalculateNormals();fixedRoot.RecalculateTangents();EditorUtility.SetDirty(fixedRoot);rootSkin.sharedMesh=fixedRoot;
            }
            var old = actor.transform.Find("Creature271"); if (old != null) Object.DestroyImmediate(old.gameObject);
            // Keep the previous meshes and original bone paths for recovery and existing combat diagnostics.
            foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.enabled = renderer.name.StartsWith("RootPart");
            var group = new GameObject("Creature271").transform; group.SetParent(actor.transform, false);
            var bark = new WoodVolume271(bones); var cut = new WoodVolume271(bones); var hollow = new WoodVolume271(bones);
            Vector3 P(float x, float y, float z) => new Vector3(x, y, z);
            Vector2 R(float x, float y) => new Vector2(x, y);
            void W(WoodVolume271 m, Vector3[] p, Vector2[] r, string[] b, int seed, int rings = 22) => m.Limb(p, r, b, seed, rings);
            void Fixed(WoodVolume271 m, Vector3[] p, Vector2[] r, string bone, int seed, int rings = 18) => W(m, p, r, Enumerable.Repeat(bone, p.Length).ToArray(), seed, rings);
            // Bent spine, split pelvis and an open anterior rib cage: the trunk is no longer a solid cone.
            W(bark, new[] { P(0,.1f,0), P(0,2,0), P(.25f,4.4f,-.6f), P(.4f,7,-.65f), P(-.1f,9,.2f) },
                new[] { R(1.8f,1.4f), R(1.25f,.95f), R(.95f,.65f), R(1.25f,.9f), R(.65f,.6f) }, new[] { "Root", "TrunkLower", "TrunkLower", "TrunkUpper", "Crown" }, 3, 48);
            for (int side = -1; side <= 1; side += 2)
            {
                W(bark, new[] { P(side*.35f,.7f,.3f), P(side*1.4f,2.4f,.65f), P(side*1.2f,4.5f,.1f), P(side*2.2f,6.8f,.5f), P(side*1.6f,8.3f,.7f) },
                    new[] { R(.6f,.65f), R(.8f,.5f), R(.35f,.45f), R(.95f,.7f), R(.32f,.36f) }, new[] { "Root", "TrunkLower", "TrunkLower", "TrunkUpper", "TrunkUpper" }, side + 12, 36);
                for (int j = 0; j < 5; j++)
                {
                    float y = 4.0f + j * .67f, width = 1.25f + j * .22f;
                    Fixed(bark, new[] { P(side*.5f,y+.25f,-.55f), P(side*width,y+.34f,.55f), P(side*(width-.15f),y-.05f,1.4f), P(side*(.32f+j*.055f),y-.45f,1.65f) },
                        new[] { R(.3f,.35f), R(.31f,.24f), R(.23f,.18f), R(.07f,.09f) }, j < 2 ? "TrunkLower" : "TrunkUpper", j + side * 4, 22);
                }
            }
            // Hollow heartwood visible between separated ribs; no glowing human face or skin.
            Fixed(hollow, new[] { P(.1f,3.5f,.5f), P(.1f,5.4f,.45f), P(.15f,7.3f,.25f) }, new[] { R(.5f,.25f), R(.95f,.35f), R(.5f,.2f) }, "TrunkUpper", 45);
            for (int i = 0; i < 6; i++)
                Fixed(cut, new[] { P(-.4f+i*.15f,4.2f,.91f), P(-.3f+i*.14f,5.2f,1.0f), P(.1f+i*.12f,6.6f,.67f) }, new[] { R(.02f,.035f), R(.06f,.05f), R(.012f,.02f) }, "TrunkUpper", i+32);
            // Two recognisable articulated limbs. Pivot positions and reach follow the existing attack rig.
            for (int k = 0; k < 2; k++)
            {
                Vector3 shoulder = k == 0 ? P(1.2f,7.3f,.35f) : P(-1.3f,7.5f,.2f);
                Vector3 elbow = k == 0 ? P(1,5,2) : P(-2,4.8f,1);
                Vector3 wrist = k == 0 ? P(2,3,5) : P(-6,2.8f,2);
                Vector3 hand = k == 0 ? P(2.5f,2.2f,6.3f) : P(-7.6f,2.25f,1.6f);
                string b = "BranchBase"+k, e = "BranchElbow"+k, t = "BranchTip"+k;
                W(bark, new[] { shoulder, elbow, Vector3.Lerp(elbow,wrist,.58f), wrist, hand },
                    new[] { R(k==0?1.12f:.86f,.85f), R(.72f,.58f), R(k==0?.9f:.64f,.57f), R(.43f,.5f), R(.7f,.36f) }, new[] { b,e,e,t,t }, 50+k, 40);
                // Deltoid flanges and exposed longitudinal sinews, not spherical joints.
                for (int q = 0; q < 4; q++)
                {
                    Vector3 offset = P((q-1.5f)*.27f,.12f,.38f);
                    W(cut, new[] { shoulder+offset, elbow+offset, wrist+offset*.65f }, new[] { R(.065f,.055f), R(.085f,.065f), R(.025f,.03f) }, new[] { b,e,t }, 61+q+k*8, 24);
                }
                Vector3 along = (hand-wrist).normalized, lateral = Vector3.Cross(along,Vector3.up).normalized;
                for (int q = 0; q < 4; q++)
                {
                    Vector3 knuckle = hand+lateral*((q-1.5f)*.38f);
                    float length = 1.0f + (q==1?.45f:q==2?.25f:0);
                    Fixed(bark, new[] { knuckle-along*.4f, knuckle+along*.25f, knuckle+along*length+Vector3.up*.12f, knuckle+along*(length+.25f)-Vector3.up*.52f },
                        new[] { R(.25f,.22f), R(.23f,.19f), R(.16f,.14f), R(.015f,.02f) }, t, q+k*10+70, 20);
                }
                Fixed(bark,new[]{hand-lateral*.65f-along*.5f,hand-lateral*1.25f,hand-lateral*1.35f+along*.8f},new[]{R(.28f,.22f),R(.2f,.17f),R(.01f,.02f)},t,84+k);
            }
            // Forward jutting split wooden skull. Eyes are deep recesses beneath a heavy brow.
            Fixed(bark, new[] { P(-.15f,8.6f,0), P(-.2f,10,.6f), P(.05f,10.2f,1.8f), P(.2f,9.6f,2.65f) }, new[] { R(.8f,.8f), R(1.1f,.85f), R(1.3f,.75f), R(.6f,.4f) }, "Crown", 90, 32);
            for (int side=-1;side<=1;side+=2)
            {
                Fixed(hollow,new[]{P(side*.63f,9.43f,1.75f),P(side*.68f,9.43f,2.05f),P(side*.7f,9.4f,2.2f)},new[]{R(.35f,.26f),R(.36f,.26f),R(.18f,.13f)},"Crown",92+side);
                Fixed(bark,new[]{P(side*1.18f,9.65f,1.55f),P(side*.9f,9.84f,2.32f),P(side*.38f,9.63f,2.84f)},new[]{R(.33f,.3f),R(.3f,.24f),R(.1f,.13f)},"Crown",96+side);
                Fixed(bark,new[]{P(side*1.13f,9.4f,1.55f),P(side*1.06f,8.7f,2.1f),P(side*.58f,8.6f,2.75f)},new[]{R(.3f,.32f),R(.27f,.24f),R(.08f,.12f)},"Crown",98+side);
                // Torn jaw rails leave a real open mouth, backed by a recessed cavity.
                Fixed(bark,new[]{P(side*.95f,9.05f,1.3f),P(side*.87f,8.1f,2.1f),P(side*.46f,7.98f,3.15f),P(0,8.12f,3.35f)},new[]{R(.28f,.32f),R(.36f,.22f),R(.2f,.19f),R(.14f,.17f)},"Crown",101+side);
            }
            Fixed(hollow,new[]{P(0,8.5f,1.25f),P(0,8.65f,1.85f),P(0,8.85f,2.35f)},new[]{R(.5f,.55f),R(.78f,.58f),R(.35f,.35f)},"Crown",103);
            Fixed(cut,new[]{P(.03f,10.0f,2.31f),P(.15f,9.33f,2.86f),P(.03f,8.9f,3.1f)},new[]{R(.21f,.18f),R(.3f,.22f),R(.035f,.05f)},"Crown",104);
            for(int i=0;i<7;i++)
            {
                float x=(i-3)*.23f;
                Fixed(cut,new[]{P(x,9.1f-Mathf.Abs(x)*.13f,2.56f),P(x*.92f,8.76f-(i%3)*.1f,2.8f)},new[]{R(.11f,.1f),R(.006f,.009f)},"Crown",110+i,10);
                Fixed(cut,new[]{P(x*.83f,8.03f,3.07f),P(x*.8f,8.43f+(i%2)*.13f,2.98f)},new[]{R(.095f,.085f),R(.008f,.008f)},"Crown",120+i,10);
            }
            // A sparse broken crown and dorsal blades, deliberately not a radial tree canopy.
            for(int side=-1;side<=1;side+=2)
            {
                Fixed(bark,new[]{P(side*.8f,10,.3f),P(side*1.65f,11.1f,-.05f),P(side*2.05f,side>0?11.9f:12.45f,-.4f),P(side*2.7f,side>0?12.2f:13.2f,-.2f)},new[]{R(.5f,.45f),R(.4f,.3f),R(.23f,.18f),R(side>0?.13f:.025f,side>0?.12f:.02f)},"Crown",140+side,26);
                Fixed(cut,new[]{P(side*1.75f,11.5f,-.1f),P(side*2.8f,12.0f,.05f),P(side*3.3f,12.7f,.25f)},new[]{R(.17f,.15f),R(.12f,.09f),R(.008f,.01f)},"Crown",144+side);
            }
            for(int i=0;i<8;i++)
            {
                float y=3+i*.76f;
                Fixed(bark,new[]{P(.2f,y,-.5f),P((i%2==0?-.3f:.4f),y+.55f,-1.6f),P((i%2==0?-.5f:.6f),y+1.3f,-2.0f)},new[]{R(.38f,.28f),R(.3f,.14f),R(.02f,.015f)},i<3?"TrunkLower":"TrunkUpper",150+i);
            }
            // Layered broken bark shingles add medium-scale relief across shoulders and pelvis.
            for(int i=0;i<26;i++)
            {
                float a=i*2.39996f, y=1.2f+(i%7)*.88f; var radial=P(Mathf.Cos(a),0,Mathf.Sin(a));
                Vector3 center=P(.15f,y,-.3f)+radial*(y>5?1.5f:1.15f);
                Fixed(bark,new[]{center-Vector3.up*.5f,center+radial*.25f,center+Vector3.up*.65f+radial*.4f},new[]{R(.13f,.07f),R(.33f,.15f),R(.018f,.025f)},y<4.3f?"TrunkLower":"TrunkUpper",180+i,16);
            }
            Material Mat(string name, Color color)
            {
                var m=Asset271(name,()=>new Material(reference.sharedMaterial)); m.CopyPropertiesFromMaterial(reference.sharedMaterial);
                var naturalBark=AssetDatabase.LoadAssetAtPath<Material>(Path.GetDirectoryName(scene.path).Replace('\\','/')+"/Detail261/Detail261_Cheongrim_SM_UlmusDavidiana_Summer_2_0_1.mat");
                if(name!="Heartwood271"&&naturalBark!=null)
                {
                    m.SetTexture("_BaseMap",naturalBark.GetTexture("_BaseMap"));m.SetTexture("_BumpMap",naturalBark.GetTexture("_BumpMap"));
                    m.SetFloat("_BumpScale",1.3f);
                }
                if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);
                if(m.HasProperty("_AmbientFloor"))m.SetFloat("_AmbientFloor",.68f);
                if(m.HasProperty("_WindAmplitude"))m.SetFloat("_WindAmplitude",0);
                EditorUtility.SetDirty(m);return m;
            }
            var materials=new[]{Mat("Bark271",new Color(.88f,.86f,.77f)),Mat("Heartwood271",new Color(1.0f,.97f,.8f)),Mat("Hollows271",new Color(.48f,.47f,.42f))};
            foreach(var rootSkin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.name.StartsWith("RootPart")))rootSkin.sharedMaterial=materials[0];
            var volumes=new[]{bark,cut,hollow};int vertexCount=0;
            for(int i=0;i<volumes.Length;i++)
            {
                var mesh=volumes[i].Bake(new[]{"Anatomy","ExposedWood","Cavities"}[i],poses);vertexCount+=mesh.vertexCount;
                var skin=new GameObject(mesh.name).AddComponent<SkinnedMeshRenderer>();skin.transform.SetParent(group,false);
                skin.sharedMesh=mesh;skin.sharedMaterial=materials[i];skin.bones=bones;skin.rootBone=reference.rootBone;
                skin.localBounds=new Bounds(Vector3.up*7,new Vector3(32,26,32));skin.updateWhenOffscreen=true;skin.forceMatrixRecalculationPerRender=true;
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            string result="Creature271: "+volumes.Sum(v=>v.Parts)+" modeled volumes / 3 new skinned renderers / "+vertexCount+" vertices; original 45 bones, attack clips, 7 grounding roots and combat data retained.";
            File.WriteAllText(Output271+"/implementation.txt",result);return result;
        }
    }
}
