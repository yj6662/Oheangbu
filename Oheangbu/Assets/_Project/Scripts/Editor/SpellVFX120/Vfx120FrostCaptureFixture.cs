using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // Capture-only projectile proxy. Never used by gameplay or an authoritative target.
    public static class Vfx120FrostCaptureFixture
    {
        public static void Create(out Transform target, out GameObject root)
        {
            root = new GameObject("VFX120 Review-only intercepted projectile") { hideFlags = HideFlags.DontSave };
            root.transform.position = new Vector3(0, 0, 4); target = root.transform;
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere); body.name = "Diagnostic projectile proxy";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.up * 1.1f;
            body.transform.localScale = new Vector3(.075f, .075f, .18f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            var material = AssetDatabase.LoadAssetAtPath<Material>(Vfx120Editor.AssetRoot + "/Materials/M_Body_Shard.mat");
            var renderer = body.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            var props = new MaterialPropertyBlock(); props.SetColor("_BaseColor", new Color(.20f, .24f, .23f, 1));
            renderer.SetPropertyBlock(props);
        }
        public static void Sample(GameObject root, float age, float hitAt, float groundY)
        {
            if (root == null || root.transform.childCount == 0) return;
            var body = root.transform.GetChild(0);
            float sinceHit = Mathf.Max(0, age - hitAt);
            float approach = .75f * (1 - Mathf.Clamp01(age / Mathf.Max(.05f, hitAt)));
            float drop = Vfx120FrostCordMotion.Drop(sinceHit, 1.1f, groundY);
            body.localPosition = new Vector3(0, 1.1f - drop, approach);
            body.localRotation = Quaternion.Euler(sinceHit > .24f ? (sinceHit - .24f) * 20 : 0, 0, 0);
            body.gameObject.SetActive(age < hitAt + 1.08f);
        }
    }
}
