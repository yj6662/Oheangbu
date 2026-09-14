using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>One scaled game clock and one transient material for all macro river surfaces.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class WorldMacroWaterClock : MonoBehaviour
    {
        public Renderer[] Surfaces;
        public Material Source;
        Material instance;
        Material sourceUsed;
        static readonly int EffectTime = Shader.PropertyToID("_EffectTime");
        public Material Instance => instance;
        public float AppliedTime { get; private set; }

        void OnEnable() => ApplyTime(Application.isPlaying ? Time.time : 0);
        void LateUpdate()
        {
            if (Application.isPlaying) ApplyTime(Time.time);
        }
        public void ApplyTime(float time)
        {
            if (Source == null || Surfaces == null) return;
            if (instance == null || sourceUsed != Source)
            {
                Release();
                sourceUsed = Source;
                instance = new Material(Source) { name = "Macro river (transient)", hideFlags = HideFlags.HideAndDontSave };
                foreach (var surface in Surfaces) if (surface != null) surface.sharedMaterial = instance;
            }
            AppliedTime = Mathf.Max(0, time);
            instance.SetFloat(EffectTime, AppliedTime);
        }
        public void RestoreAuthoredMaterial()
        {
            if (Surfaces != null)
                foreach (var surface in Surfaces)
                    if (surface != null && surface.sharedMaterial == instance) surface.sharedMaterial = sourceUsed;
        }
        public void Rebind()
        {
            if (instance != null && Surfaces != null)
                foreach (var surface in Surfaces) if (surface != null) surface.sharedMaterial = instance;
        }
        void OnDisable() => Release();
        void Release()
        {
            RestoreAuthoredMaterial();
            if (instance != null)
            {
                if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance);
            }
            instance = null;
            sourceUsed = null;
        }
    }
}
