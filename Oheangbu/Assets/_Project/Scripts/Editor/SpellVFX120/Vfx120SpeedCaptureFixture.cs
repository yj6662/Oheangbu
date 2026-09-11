using System;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // A capture-only moving anchor. Its authored speed illustrates attachment,
    // not a buff calculation, an attack or a comparison of real projectile speeds.
    public static class Vfx120SpeedCaptureFixture
    {
        public static GameObject Create()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Vfx120Editor.AssetRoot + "/Meshes/MetalDetails/VFX120_FrostNeedle_078.asset");
            var material = AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot + "/Materials/M_Body_Shard.mat");
            if (mesh == null || material == null) throw new InvalidOperationException("Missing diagnostic projectile mesh/material");
            var root = new GameObject("VFX120 Review-only speed attachment") { hideFlags = HideFlags.DontSave };
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = material;
            root.transform.position = new Vector3(.4f, 1.08f, .9f);
            return root;
        }
        public static void Sample(GameObject root, Vfx120Effect effect, float age)
        {
            const float launch = .4f;
            float travel = Mathf.InverseLerp(launch, Mathf.Max(launch + .1f, effect.Life - .2f), age);
            root.transform.position = new Vector3(.4f, 1.08f, .9f + travel * 2.8f);
            root.GetComponent<Renderer>().enabled = age >= launch && age < effect.Life - .08f;
            effect.SetProjectileVisualAnchor(age >= launch ? root.transform : null, launch);
        }
    }
}
