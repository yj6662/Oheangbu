using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World
{
    [DisallowMultipleComponent]
    public sealed class WorldMacroUiAudioVoices : MonoBehaviour
    {
        private WorldMacroAudioVoicePool _pool;
        private WorldMacroAudioMixProfileSO _mix;
        private readonly Dictionary<AudioClip, WorldMacroPlaytestAudioProfileSO.Cue> _cues = new Dictionary<AudioClip, WorldMacroPlaytestAudioProfileSO.Cue>();
        private readonly Dictionary<AudioClip, double> _next = new Dictionary<AudioClip, double>();
        public void ApplySettings(WorldMacroAudioMixProfileSO mix, float master, float gameplay, float ui)
        {
            _mix = mix;
            if(_mix != null) _mix.ApplyUserVolumes(master,gameplay,ui);
            EnsurePool(); _pool.ExternalGain = _mix != null && _mix.IsReady ? 1f : Mathf.Clamp01(master)*Mathf.Clamp01(ui);
        }
        public bool Play(AudioClip clip, float gain)
        {
            if(clip==null || !isActiveAndEnabled) return false;
            double now=AudioSettings.dspTime;
            if(_next.TryGetValue(clip,out double next)&&now<next) return false;
            _next[clip]=now+.09;
            if(!_cues.TryGetValue(clip,out var cue))
            { cue=new WorldMacroPlaytestAudioProfileSO.Cue { Clip=clip, Volume=1f, MaxConcurrent=2, AttackSeconds=.012f, ReleaseSeconds=.06f };_cues.Add(clip,cue); }
            EnsurePool(); return _pool.Play(cue,transform.position,gain,_mix!=null&&_mix.IsReady?_mix.Ui:null);
        }
        private void EnsurePool() { if(_pool==null)_pool=new WorldMacroAudioVoicePool(transform,4,0,true,"PlaytestUiVoice_"); }
        private void Update() { _mix?.FlushPending(); _pool?.Tick(); }
        private void OnApplicationFocus(bool focused) { if(!focused)_pool?.StopAll(false); }
        private void OnDisable() { _pool?.StopAll(true);_next.Clear(); }
        private void OnDestroy() { _pool?.Dispose();_pool=null; }
    }
}
