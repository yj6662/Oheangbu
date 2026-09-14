using UnityEngine;
using UnityEngine.Audio;

namespace Oheangbu.App.World
{
    [CreateAssetMenu(menuName = "Oheangbu/Playtest/Audio Mix")]
    public sealed class WorldMacroAudioMixProfileSO : ScriptableObject
    {
        public AudioMixer Mixer;
        public AudioMixerGroup Sfx, Ui, Harvest;
        [Range(-18f, 0f)] public float MasterHeadroomDb = -6f;
        public bool IsReady => Mixer != null && Sfx != null && Ui != null && Harvest != null;
        private bool pending;
        private Vector3 requested = Vector3.one;
        public static float VolumeDb(float value) => value <= .0001f ? -80f : 20f * Mathf.Log10(Mathf.Clamp01(value));
        public void ApplyUserVolumes(float master, float gameplay, float ui)
        {
            requested = new Vector3(master, gameplay, ui); pending = true;
        }
        // AudioMixer.SetFloat is deferred out of Awake/OnEnable until the audio graph is initialized.
        public void FlushPending()
        {
            if (!pending) return;
            if (!IsReady) return;
            bool a = Mixer.SetFloat("MasterVolumeDb", Mathf.Max(-80f, VolumeDb(requested.x) + MasterHeadroomDb));
            bool b = Mixer.SetFloat("SfxVolumeDb", VolumeDb(requested.y));
            bool c = Mixer.SetFloat("UiVolumeDb", VolumeDb(requested.z));
            pending = !(a && b && c);
        }
    }
}
