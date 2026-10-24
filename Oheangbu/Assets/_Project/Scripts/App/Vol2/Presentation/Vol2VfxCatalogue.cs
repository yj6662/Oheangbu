using System;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The catalogue of one scope: 24 templates, 5 body looks, the ONE shared ink material. Loaded from Resources (the editor builder writes it); without the asset the
    /// code defaults are used in memory (<see cref="Vol2VfxDefaults"/>) so a scope never goes without. The peaks (20) and reactions (25) are added here by later slices.</summary>
    public sealed class Vol2VfxCatalogue : ScriptableObject
    {
        public const string ResourcePath = "Vol2Vfx/Vol2VfxCatalogue";
        public const string ShaderName = "Oheangbu/SpellVFX308/Vol2Ink";
        public Vol2VfxTemplate[] templates = new Vol2VfxTemplate[0];
        public Vol2BodyLook[] bodies = new Vol2BodyLook[0];
        public Material inkMaterial;

        public Vol2VfxTemplate Template(Vol2TemplateId id)
        {
            foreach (var t in templates) if (t != null && t.frame == id.Frame && t.verb == id.Verb) return t;
            return null;
        }

        public Vol2BodyLook Look(Body body)
        {
            foreach (var b in bodies) if (b != null && b.body == body) return b;
            return null;
        }

        public bool Complete(out string problem)
        {
            problem = "";
            if (templates == null || templates.Length != Vol2TemplateId.Count) { problem = "templates: " + (templates == null ? 0 : templates.Length) + " of " + Vol2TemplateId.Count; return false; }
            if (bodies == null || bodies.Length != 5) { problem = "bodies: " + (bodies == null ? 0 : bodies.Length) + " of 5"; return false; }
            if (inkMaterial == null) { problem = "no ink material"; return false; }
            foreach (var id in Vol2TemplateId.All()) if (Template(id) == null) { problem = "no template " + id; return false; }
            foreach (Body b in Enum.GetValues(typeof(Body))) if (Look(b) == null) { problem = "no body look " + b; return false; }
            return true;
        }

        /// <summary>The scope's catalogue: the asset under Resources if the builder wrote it, else an in-memory default (null when the shader is not there).</summary>
        public static Vol2VfxCatalogue Load()
        {
            var asset = Resources.Load<Vol2VfxCatalogue>(ResourcePath);
            if (asset != null && asset.Complete(out _)) return asset;
            var shader = Shader.Find(ShaderName);
            if (shader == null) return null;
            return Vol2VfxDefaults.CreateCatalogue(new Material(shader) { name = "Vol2Ink(runtime)", hideFlags = HideFlags.DontSave });
        }
    }
}
