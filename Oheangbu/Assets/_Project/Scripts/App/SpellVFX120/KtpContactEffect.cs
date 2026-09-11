using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.SpellVFX120
{
    // Plays the vendor's complete contact subtree without tinting, budgeting or fading it.
    public sealed class KtpContactEffect : MonoBehaviour
    {
        public GameObject Content { get; private set; }
        public GameObject Source { get; private set; }
        public bool PreviewControlled { get; private set; }
        public int LiveParticles
        {
            get { int n = 0; foreach (var ps in _systems) if (ps != null) n += ps.particleCount; return n; }
        }
        private ParticleSystem[] _systems;
        private ParticleSystem _root;
        private float _startedAt;
        private float _releaseAt;
        private bool _released;

        public static KtpContactEffect Spawn(GameObject source, Vector3 point, Quaternion rotation,
            float scale, Scene scene, bool preview = false)
        {
            if (source == null || !float.IsFinite(scale) || scale <= 0 || !Finite(point)
                || !scene.IsValid() || !scene.isLoaded) return null;
            // Preflight before Instantiate: contact sources have particles only.
            if (source.GetComponent<ParticleSystem>() == null
                || source.GetComponentsInChildren<MonoBehaviour>(true).Length != 0
                || source.GetComponentsInChildren<Animator>(true).Length != 0
                || source.GetComponentsInChildren<Collider>(true).Length != 0
                || source.GetComponentsInChildren<Rigidbody>(true).Length != 0) return null;
            if (source.GetComponent<ParticleSystem>().main.loop) return null;

            var host = new GameObject("KTP_Contact_" + source.name);
            host.SetActive(false);
            SceneManager.MoveGameObjectToScene(host, scene);
            host.transform.SetPositionAndRotation(point, rotation);
            host.transform.localScale = Vector3.one * scale;
            var effect = host.AddComponent<KtpContactEffect>();
            effect.Source = source;
            effect.PreviewControlled = preview;
            effect.Content = Instantiate(source, host.transform, false);
            effect.Content.name = source.name;
            // The source's offset is the Preview scene's projectile endpoint.
            effect.Content.transform.localPosition = Vector3.zero;
            effect._root = effect.Content.GetComponent<ParticleSystem>();
            var rootMain = effect._root.main;
            effect._releaseAt = (rootMain.duration + rootMain.startDelay.constantMax) / Mathf.Max(.001f, rootMain.simulationSpeed);
            effect._systems = effect.Content.GetComponentsInChildren<ParticleSystem>(true);
            effect._root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var ps in effect._systems)
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (preview) { ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(31 + System.Array.IndexOf(effect._systems, ps)); }
            }
            effect._startedAt = Time.time;
            host.SetActive(true);
            if (preview) effect.Sample(0);
            else effect._root.Play(true);
            return effect;
        }

        // Deterministic time seeking for review only; runtime uses Unity's native clock.
        public void Sample(float age)
        {
            if (!PreviewControlled || _root == null) return;
            float at = Mathf.Max(0, age);
            _root.Simulate(Mathf.Min(at, _releaseAt), true, true, false);
            if (at >= _releaseAt)
            {
                _root.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                _root.Simulate(at - _releaseAt, true, false, false);
            }
        }

        private void Update()
        {
            if (PreviewControlled || _root == null || Time.time <= _startedAt) return;
            // Some vendor contacts contain a looping flare. End its emission at the
            // authored root cycle, then let every remaining particle finish its own curve.
            if (!_released && Time.time - _startedAt >= _releaseAt)
            {
                _released = true;
                _root.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (!_root.IsAlive(true)) Destroy(gameObject);
        }

        public void ApplyElementColor(Color pigment, float brightness = 1f)
        {
            foreach(var ps in _systems){var m=ps.main;m.startColor=Vfx120TraditionalMotif.Neutral(m.startColor);var c=ps.colorOverLifetime;if(c.enabled)c.color=Vfx120TraditionalMotif.Neutral(c.color);var b=ps.colorBySpeed;if(b.enabled)b.color=Vfx120TraditionalMotif.Neutral(b.color);}
            float peak=Mathf.Max(.001f,Mathf.Max(pigment.r,Mathf.Max(pigment.g,pigment.b)));
            foreach(var renderer in Content.GetComponentsInChildren<Renderer>(true))
            {
                var mats=renderer.sharedMaterials;for(int i=0;i<mats.Length;i++){var mat=mats[i];if(mat==null)continue;var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,i);
                    bool pattern=renderer.name.IndexOf("pattern",System.StringComparison.OrdinalIgnoreCase)>=0;
                    if(brightness!=1f&&pattern&&mat.HasProperty("_Emission"))block.SetFloat("_Emission",1f);
                    var shader = mat.shader;
                    for (int p=0;p<shader.GetPropertyCount();p++)
                    {
                        if(shader.GetPropertyType(p)!=UnityEngine.Rendering.ShaderPropertyType.Color)continue;
                        string name=shader.GetPropertyName(p);
                        if(brightness==1f && name!="_BaseColor" && name!="_Color" && name!="_TintColor" && name!="_EmissionColor")continue;
                        int prop=shader.GetPropertyNameId(p);Color c=mat.GetColor(prop);
                        float multiplier=brightness!=1f && renderer.name.IndexOf("pattern",System.StringComparison.OrdinalIgnoreCase)<0 ? .25f : brightness;
                        if(brightness!=1f&&pattern)multiplier*=peak;
                        float v=Mathf.Max(c.r,Mathf.Max(c.g,c.b)) * multiplier;
                        block.SetColor(prop,new Color(v*pigment.r/peak,v*pigment.g/peak,v*pigment.b/peak,c.a));
                    }
                    renderer.SetPropertyBlock(block,i);
                }
            }
        }
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
