using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class KtpContactBuild
    {
        public const string AssetPath = "Assets/_Project/Art/SpellVFX120/Resources/KTP_ContactProfile.asset";
        private const string Pack = "Assets/KoreanTraditionalPattern_Effect/";
        [Serializable] private class SourceRow
        {
            public string role, path, guid, sha256;
            public long localId;
            public int systems, capacity;
            public bool inPreview, shadersSupported;
        }
        [Serializable] private class Report { public string status; public SourceRow[] sources; }

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            var rows = new System.Collections.Generic.List<SourceRow>();
            var elements = new[] { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water };
            // Authored color variants from the same Preview scene, not runtime recoloring.
            var names = new[] { "Fly01-01", "Fly03-01", "Fly10-03", "Fly07-01", "Fly10-01" };
            var entries = new KtpContactProfile.Entry[elements.Length];
            for (int i = 0; i < elements.Length; i++)
                entries[i] = new KtpContactProfile.Entry { Element = elements[i], Source = Source(names[i], elements[i].ToString(), rows) };
            var hurt = Source("Fly03-01", "PlayerHit", rows);
            if (rows.Any(r => !r.inPreview || !r.shadersSupported)) throw new InvalidOperationException(JsonUtility.ToJson(new Report { sources = rows.ToArray() }, true));
            string folder = Path.GetDirectoryName(AssetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Project/Art/SpellVFX120", "Resources");
            var profile = AssetDatabase.LoadAssetAtPath<KtpContactProfile>(AssetPath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<KtpContactProfile>(); AssetDatabase.CreateAsset(profile, AssetPath); }
            profile.Parries = entries; profile.PlayerHit = hurt;
            EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
            foreach (var row in rows) if (Hash(row.path) != row.sha256) throw new InvalidOperationException("Source modified: " + row.path);
            string report = JsonUtility.ToJson(new Report { status = "BUILT_SOURCE_PRESERVED", sources = rows.ToArray() }, true);
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "ktp_contact_sources.json"), report);
            return report;
        }

        private static GameObject Source(string name, string role, System.Collections.Generic.List<SourceRow> rows)
        {
            string path = Pack + "Prefabs/Fly/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException(path);
            var source = prefab.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Explosion").gameObject;
            var systems = source.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0 || source.GetComponent<ParticleSystem>() == null || source.GetComponent<ParticleSystem>().main.loop)
                throw new InvalidOperationException("Expected finite root contact: " + path);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
            rows.Add(new SourceRow { role = role, path = path, guid = guid, localId = localId, sha256 = Hash(path),
                inPreview = File.ReadAllText(Pack + "Scenes/Preview.unity").Contains(guid), systems = systems.Length,
                capacity = systems.Sum(p => p.main.maxParticles),
                shadersSupported = source.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.isSupported)) });
            return source;
        }

        public static string Hash(string path)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }
    }
}
