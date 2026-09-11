using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120BambooGuardBuilder
    {
        const string Folder = "Assets/_Project/Art/SpellVFX120/BambooGuard";
        [Serializable] sealed class Report
        {
            public string status = "AUTHORED_AWAITING_USER_VISUAL_REVIEW", glyph = "거", beforeJson, afterJson, snapshot;
            public string textureSource, barkMaterialSource, contactPrefabSource, technicalCheck;
            public string art = "AWAITING_USER_REVIEW", gameplay = "Existing guard clock and successful Wood parry presentation callback wired; actual combat playthrough unverified";
            public int bodyVertices, borderVertices, bodyTris, borderTris, totalBodyTris, renderers = 2;
        }

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required");
            string root = Vfx120Editor.AssetRoot;
            var p = AssetDatabase.LoadAssetAtPath<Vfx120Profile>(root + "/Profiles/007_AC70.asset");
            var r = new Report {
                textureSource = "Assets/KoreanTraditionalPattern_Effect/Textures/TraditionalTexture/Pattern_33.png",
                barkMaterialSource = root + "/Botanical/Materials/M_Bamboo_0_Bark.mat",
                contactPrefabSource = root + "/Traditional/KTP_Impact_LeafBurst.prefab" };
            var bark = AssetDatabase.LoadAssetAtPath<Material>(r.barkMaterialSource);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(r.textureSource);
            var contact = AssetDatabase.LoadAssetAtPath<GameObject>(r.contactPrefabSource);
            if (p == null || p.Glyph != "거" || bark == null || texture == null || contact == null)
                throw new InvalidOperationException("Missing 거 or reused KTP/bamboo sources");
            r.beforeJson = JsonUtility.ToJson(p);
            string priorReport = Path.Combine(Vfx120Editor.Output, "bamboo_guard_007_build.json");
            if (Vfx120Effect.IsBambooGuard(p) && File.Exists(priorReport))
                r.beforeJson = JsonUtility.FromJson<Report>(File.ReadAllText(priorReport)).beforeJson;
            Directory.CreateDirectory(Path.Combine(Vfx120Editor.Output, "WoodDetailOriginals"));
            r.snapshot = Path.Combine(Vfx120Editor.Output, "WoodDetailOriginals/007_AC70.asset.txt");
            if (!File.Exists(r.snapshot)) File.Copy(AssetDatabase.GetAssetPath(p), r.snapshot);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(root, "BambooGuard");
            Mesh body = Body(); Mesh border = Border();
            r.bodyVertices = body.vertexCount; r.borderVertices = border.vertexCount;
            r.bodyTris = body.triangles.Length / 3; r.borderTris = border.triangles.Length / 3;
            r.totalBodyTris = r.bodyTris + r.borderTris;
            p.BodyMesh = Store(body, Folder + "/VFX120_BambooGuard_007.asset");
            p.AccentMesh = Store(border, Folder + "/VFX120_BambooGuardBorder_007.asset");
            var mat = new Material(bark) { name = "M_BambooGuard_007" };
            mat.SetFloat("_TintStrength", .28f); mat.SetFloat("_Saturation", .55f);
            mat.SetColor("_Tint", new Color(.40f,.47f,.32f,1));
            p.BodyMaterial = Store(mat, Folder + "/M_BambooGuard_007.mat");
            var pattern = new Material(p.PatternMaterial) { name = "M_BambooGuardBorder_007" };
            pattern.SetTexture("_BaseMap", texture); pattern.SetFloat("_Pattern", 1);
            p.PatternMaterial = Store(pattern, Folder + "/M_BambooGuardBorder_007.mat");
            p.NativeFieldPrefab = null; p.NativeReplaceBody = false;
            p.NativeImpactPrefab = contact; p.NativeScale = .32f; p.NativeImpactScale = .42f;
            p.Count = 1; p.PartScale = Vector3.one; p.RibbonCount = 0;
            EditorUtility.SetDirty(p); AssetDatabase.SaveAssets();
            r.afterJson = JsonUtility.ToJson(p);
            r.technicalCheck = Check(p);
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "bamboo_guard_007_build.json"), JsonUtility.ToJson(r, true));
            return r.status + "; " + r.technicalCheck + "; bodyTris=" + r.totalBodyTris;
        }

        static T Store<T>(T value, string path) where T : UnityEngine.Object
        {
            T old = AssetDatabase.LoadAssetAtPath<T>(path);
            if (old == null) { AssetDatabase.CreateAsset(value, path); return value; }
            EditorUtility.CopySerialized(value, old); UnityEngine.Object.DestroyImmediate(value);
            EditorUtility.SetDirty(old); return old;
        }
        static Vector3 Fan(float angle, float radius, float depth = 0)
        {
            float a = angle * Mathf.Deg2Rad;
            float x = Mathf.Sin(a) * radius;
            return new Vector3(x, -.6f + Mathf.Cos(a) * radius, .83f - .22f*x*x + depth);
        }
        static Mesh Body()
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int rib = 0; rib < 7; rib++)
            {
                float angle = Mathf.Lerp(-68, 68, rib / 6f);
                var path = new Vector3[19]; var widths = new float[19];
                for (int j = 0; j < path.Length; j++)
                {
                    float t = j / 18f;
                    path[j] = Fan(angle, .16f + t * .98f);
                    // Raised nodes along the small cane; no leaf/card geometry is used.
                    widths[j] = Mathf.Lerp(.016f,.010f,t) * (j % 6 == 0 ? 1.32f : 1f);
                }
                Tube(path, widths, vertices, uv, tris);
            }
            foreach (float radius in new[] { .42f, 1.12f })
            {
                var path = new Vector3[29]; var widths = new float[29];
                for (int j=0;j<path.Length;j++) { path[j]=Fan(Mathf.Lerp(-70,70,j/28f),radius,-.015f); widths[j]=.010f; }
                Tube(path,widths,vertices,uv,tris);
            }
            return MeshOf("VFX120_BambooGuard_007", vertices, uv, tris);
        }
        internal static void Tube(Vector3[] path, float[] widths, List<Vector3> v, List<Vector2> uv, List<int> tri)
        {
            const int sides=6; int start=v.Count;
            for(int j=0;j<path.Length;j++)
            {
                Vector3 direction=(path[Mathf.Min(j+1,path.Length-1)]-path[Mathf.Max(0,j-1)]).normalized;
                Vector3 side=Vector3.Cross(direction,Vector3.forward).normalized;
                Vector3 back=Vector3.Cross(direction,side).normalized;
                for(int k=0;k<sides;k++)
                {
                    float a=k*Mathf.PI*2/sides;
                    v.Add(path[j]+widths[j]*(side*Mathf.Cos(a)+back*Mathf.Sin(a)));
                    uv.Add(new Vector2(k/(float)sides,j/(float)(path.Length-1)));
                    if(j>0)
                    {
                        int n=start+j*sides+k, p=start+(j-1)*sides+k;
                        int pn=start+(j-1)*sides+(k+1)%sides, nn=start+j*sides+(k+1)%sides;
                        tri.Add(p);tri.Add(pn);tri.Add(n);tri.Add(pn);tri.Add(nn);tri.Add(n);
                    }
                }
            }
            for(int k=1;k<sides-1;k++)
            {
                tri.Add(start);tri.Add(start+k+1);tri.Add(start+k);
                int end=start+(path.Length-1)*sides;tri.Add(end);tri.Add(end+k);tri.Add(end+k+1);
            }
        }
        static Mesh Border()
        {
            var v=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            // Only the upper open annulus carries the KTP alpha motif; centre remains empty.
            for(int j=0;j<=64;j++) for(int side=0;side<2;side++)
            {
                float angle=Mathf.Lerp(-72,72,j/64f);float radius=side==0?.93f:1.145f;
                Vector3 p=Fan(angle,radius,-.021f);v.Add(p);
                uv.Add(new Vector2(.5f+p.x/2.35f,.5f+(p.y+.6f)/2.35f));
                if(j>0 && side==1)
                {int b=v.Count-4;tris.Add(b);tris.Add(b+2);tris.Add(b+1);tris.Add(b+1);tris.Add(b+2);tris.Add(b+3);}
            }
            return MeshOf("VFX120_BambooGuardBorder_007",v,uv,tris);
        }
        internal static Mesh MeshOf(string name,List<Vector3> v,List<Vector2> uv,List<int> tris)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);
            var colors=new Color[v.Count];for(int i=0;i<colors.Length;i++) colors[i]=Color.white;
            mesh.colors=colors;mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
        static string Check(Vfx120Profile profile)
        {
            if(!Vfx120Effect.IsBambooGuard(profile)) throw new InvalidOperationException("Guard not prepared");
            var rest=profile.BodyMesh.vertices;int checkedVertices=0;
            foreach(float age in new[]{0f,.05f,.2f,.36f,.43f,.84f,2.1f})
            {
                float bend=Vfx120Effect.GuardFlex(age,.36f);
                if((age<=.36f || age>=.84f) && Mathf.Abs(bend)>.00001f) throw new InvalidOperationException("Flex outside confirmation interval");
                if(Vfx120Effect.GuardFlex(age,-1)!=0) throw new InvalidOperationException("False flex without parry");
                foreach(var vertex in rest)
                {
                    var p=Vfx120Effect.GuardVertex(vertex,age,bend,.25f);
                    if(!Vfx120InterceptionMotion.Finite(p) || Mathf.Abs(p.x)>1.3f || Mathf.Abs(p.y)>.8f || p.z<.2f || p.z>1.2f)
                        throw new InvalidOperationException("Unbounded guard vertex");
                    checkedVertices++;
                }
            }
            return "PASS_FINITE_GEOMETRY_AND_CONFIRMATION_WINDOW verticesChecked="+checkedVertices;
        }
    }
}
