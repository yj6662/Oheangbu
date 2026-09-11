using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Explicit demonstration data and proxies; never an enemy/projectile finder.
    public sealed class Vfx120InterceptionReviewFixture : MonoBehaviour
    {
        Vfx120InterceptionMotion.Target[] plans;
        Transform[] proxies;
        public static Vfx120InterceptionMotion.Target[] CreatePlans(float groundY)
        {
            Vector3[] points = { new Vector3(-1f,.15f,3.2f), new Vector3(-.6f,.65f,3.8f),
                new Vector3(0,.4f,4.2f), new Vector3(.55f,.7f,3.4f),
                new Vector3(1f,.18f,4f), new Vector3(.15f,1f,4.7f) };
            var result = new Vfx120InterceptionMotion.Target[points.Length];
            for (int i = 0; i < points.Length; i++) result[i] = new Vfx120InterceptionMotion.Target
            { Point = points[i], LaunchAt = i * .02f, ArrivalAt = .4f + i * .025f,
                HitAt = .4f + i * .025f, HitConfirmed = true, GroundY = groundY };
            return result;
        }
        public static GameObject Create(Vfx120Effect effect)
        {
            if (!effect.PreviewControlled || !effect.DemonstrationCues) return null;
            var root = new GameObject("VFX120 Review-only six intercepted projectiles") { hideFlags = HideFlags.DontSave };
            root.transform.SetPositionAndRotation(effect.transform.position, effect.transform.rotation);
            var fixture = root.AddComponent<Vfx120InterceptionReviewFixture>();
            fixture.plans = CreatePlans(effect.TargetGroundWorldY - effect.transform.position.y);
            fixture.proxies = new Transform[fixture.plans.Length];
            for (int i = 0; i < fixture.proxies.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "Review projectile " + (i + 1);
                go.transform.SetParent(root.transform, false); go.transform.localScale = new Vector3(.075f,.075f,.18f);
                var collider = go.GetComponent<Collider>(); collider.enabled = false;
                if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
                var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = effect.Profile.BodyMaterial;
                var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", new Color(.20f,.24f,.23f,1)); renderer.SetPropertyBlock(block);
                fixture.proxies[i] = go.transform;
            }
            effect.SetInterceptionTargets(fixture.plans); fixture.Sample(0); return root;
        }
        public void Sample(float age)
        {
            for (int i = 0; i < proxies.Length; i++)
            {
                var t = plans[i]; Vector3 direction = t.Point.normalized;
                float before = 1 - Mathf.Clamp01(age / t.ArrivalAt), after = Mathf.Max(0, age - t.HitAt);
                proxies[i].localPosition = t.Point + direction * (.8f * before)
                    - Vector3.up * Vfx120FrostCordMotion.Drop(after, t.Point.y, t.GroundY);
                proxies[i].localRotation = Quaternion.LookRotation(-direction) * Quaternion.Euler(Mathf.Max(0,after-.24f)*20,0,0);
                proxies[i].gameObject.SetActive(age < t.HitAt + 1.08f);
            }
        }
    }
}
