using System.Collections.Generic;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.Drawing;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App.World
{
    // Reads actual result events only. Mixing and envelopes never postpone a cast, hit or ink gain.
    [DefaultExecutionOrder(2200)]
    public sealed class WorldMacroPlaytestAudio : MonoBehaviour
    {
        public const int VoiceLimit = 12;
        [SerializeField] private WorldMacroPlaytestAudioProfileSO _profile;
        [SerializeField] private WorldMacroPlaytestSession _session;
        [SerializeField] private Oheangbu.App.Prologue.PrologueSession _prologue;
        [SerializeField] private CombatLoopWiring _wiring;
        [SerializeField] private DrawingInputController _drawing;
        [SerializeField] private BrushStrokeFeedAdapter _brushAdapter;
        [SerializeField] private PlayerVitals _playerVitals;
        [SerializeField] private HarvestAction _harvest;
        [SerializeField, Min(.01f)] private float _brushReleaseSeconds = .08f;
        [SerializeField, Range(0f, 1f)] private float _summonElementGain = .35f;
        private AudioSource[] _voices = new AudioSource[VoiceLimit];
        private WorldMacroAudioVoicePool _pool;
        private readonly Dictionary<WorldMacroPlaytestAudioProfileSO.Cue, double> _nextCueAt = new Dictionary<WorldMacroPlaytestAudioProfileSO.Cue, double>();
        private bool _hooked, _applicationPaused, _timePaused;
        private bool _focused = true;
        private float _runtimeMasterGain = 1f, _runtimeGameplayGain = 1f;
        private int _brushEvents, _acceptedCastEvents, _enemyHitEvents, _playerHitEvents, _parryEvents, _harvestEvents, _interactionEvents, _summonAppearEvents, _summonReleaseEvents, _cuePlays, _cooldownDrops;
        private string _lastEvent = "none", _lastCue = "none";
        public WorldMacroPlaytestAudioProfileSO Profile => _profile;
        public int VoiceCount => _pool == null ? 0 : VoiceLimit;
        public int ActiveVoiceCount { get { int n=0; foreach(var voice in _voices) if(voice!=null&&voice.isPlaying)n++; return n; } }
        public float RuntimeMasterGain => _runtimeMasterGain;
        public float RuntimeGameplayGain => _runtimeGameplayGain;
        public string RuntimeCounters => "brush=" + _brushEvents + "; acceptedCast=" + _acceptedCastEvents
            + "; enemyHit=" + _enemyHitEvents + "; playerHit=" + _playerHitEvents + "; parry=" + _parryEvents
            + "; harvest=" + _harvestEvents + "; interaction=" + _interactionEvents + "; summonAppear=" + _summonAppearEvents
            + "; summonRelease=" + _summonReleaseEvents + "; cuePlays=" + _cuePlays + "; cooldownDrops=" + _cooldownDrops
            + "; concurrencyDrops=" + (_pool?.ConcurrencyDrops ?? 0) + "; fadedVoiceSteals=" + (_pool?.FadedSteals ?? 0)
            + "; activeVoices=" + ActiveVoiceCount + "; harvestPulling=" + (_harvest != null && _harvest.State == HarvestState.Pulling) + "; lastEvent=" + _lastEvent + "; lastCue=" + _lastCue;
        public void ConfigureProfile(WorldMacroPlaytestAudioProfileSO profile) { StopAll(); _profile = profile; ApplyCurrentSettings(); }
        private bool UsesMixer => _profile != null && _profile.Mix != null && _profile.Mix.IsReady;
        public void ApplyVolumeSettings(float masterVolume, float gameplayVolume)
        {
            _runtimeMasterGain = Mathf.Clamp01(masterVolume); _runtimeGameplayGain = Mathf.Clamp01(gameplayVolume);
            if (_pool != null) _pool.ExternalGain = UsesMixer ? 1f : _runtimeMasterGain * _runtimeGameplayGain;
        }
        private void ApplyCurrentSettings()
        {
            var settings = FindFirstObjectByType<UserSettingsService>();
            if(settings == null) { ApplyVolumeSettings(_runtimeMasterGain,_runtimeGameplayGain); return; }
            var current = settings.Current;
            if (UsesMixer) _profile.Mix.ApplyUserVolumes(current.MasterVolume,current.GameplayVolume,current.UiVolume);
            ApplyVolumeSettings(current.MasterVolume,current.GameplayVolume);
        }
        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            _focused = Application.isFocused; EnsureVoices(); ApplyCurrentSettings(); Hook();
        }
        private void OnDisable() { Unhook(); StopAll(); }
        private void OnDestroy() { Unhook(); _pool?.Dispose(); _pool=null; }
        private void EnsureVoices()
        {
            if (_pool != null) return;
            _pool = new WorldMacroAudioVoicePool(transform,VoiceLimit,2,false,"PlaytestAudioVoice_"); _voices = _pool.Sources;
        }
        private void Update()
        {
            _profile?.Mix?.FlushPending();
            bool pausedNow = Time.timeScale <= .0001f;
            if (pausedNow && !_timePaused) StopForSuspension();
            _timePaused = pausedNow;
            _pool?.Tick();
        }
        // A suspended device may stop its DSP clock, so it cannot finish an envelope.
        private void OnApplicationPause(bool paused) { _applicationPaused=paused; if(paused)StopAll(); }
        private void OnApplicationFocus(bool focused) { _focused=focused; if(!focused)StopForSuspension(); }
        private void StopForSuspension() { _pool?.StopAll(false); _nextCueAt.Clear(); }
        private bool CanPlay => isActiveAndEnabled && !_applicationPaused && _focused && Time.timeScale > .0001f && _profile != null && _pool != null;
        private void OnStrokeStarted()
        {
            _brushEvents++; _lastEvent="brush-start";
            if(!CanPlay || _profile.BrushStroke == null) return;
            if(_pool.Play(_profile.BrushStroke,transform.position,1f,UsesMixer?_profile.Mix.Sfx:null,0))
            { _cuePlays++; _lastCue=_profile.BrushStroke.Clip != null ? _profile.BrushStroke.Clip.name : "none"; }
        }
        private void OnStrokeEnded() { _pool?.ReleaseSlot(0,_brushReleaseSeconds); }
        private void Hook()
        {
            if (_hooked) return;
            if (_wiring != null)
            {
                _wiring.CastAccepted += OnCastAccepted;
                _wiring.EnemyHitResolved += OnEnemyHit;
                _wiring.ParryResolved += OnParry;
            }
            if (_drawing != null)
            {
                _drawing.StrokeStarted += OnStrokeStarted;
                _drawing.StrokeEnded += OnStrokeEnded;
                _drawing.LetterInterrupted += OnStrokeEnded;
                _drawing.ModeExited += OnStrokeEnded;
            }
            if (_brushAdapter != null)
            {
                _brushAdapter.SummonPresentationStarted += OnSummonStarted;
                _brushAdapter.SummonPresentationReleased += OnSummonReleased;
            }
            if (_playerVitals != null) _playerVitals.Damaged += OnPlayerDamaged;
            if (_harvest != null) { _harvest.PullStarted += OnHarvestPullStarted; _harvest.ChunkExtracted += OnHarvested; }
            if (_session != null) _session.InteractionResolved += OnInteractionResolved;
            if (_prologue != null) _prologue.InteractionPresented += OnJourneyInteraction;
            _hooked = true;
        }

        private void Unhook()
        {
            if (!_hooked) return;
            if (_wiring != null)
            {
                _wiring.CastAccepted -= OnCastAccepted;
                _wiring.EnemyHitResolved -= OnEnemyHit;
                _wiring.ParryResolved -= OnParry;
            }
            if (_drawing != null)
            {
                _drawing.StrokeStarted -= OnStrokeStarted;
                _drawing.StrokeEnded -= OnStrokeEnded;
                _drawing.LetterInterrupted -= OnStrokeEnded;
                _drawing.ModeExited -= OnStrokeEnded;
            }
            if (_brushAdapter != null)
            {
                _brushAdapter.SummonPresentationStarted -= OnSummonStarted;
                _brushAdapter.SummonPresentationReleased -= OnSummonReleased;
            }
            if (_playerVitals != null) _playerVitals.Damaged -= OnPlayerDamaged;
            if (_harvest != null) { _harvest.PullStarted -= OnHarvestPullStarted; _harvest.ChunkExtracted -= OnHarvested; }
            if (_session != null) _session.InteractionResolved -= OnInteractionResolved;
            if (_prologue != null) _prologue.InteractionPresented -= OnJourneyInteraction;
            _hooked = false;
        }

        private void OnCastAccepted(SpellCast cast, Vector3 origin, Vector3 forward)
        {
            _acceptedCastEvents++;
            _lastEvent = "accepted-cast";
            if (!CanPlay) return;
            WorldMacroPlaytestAudioProfileSO.Cue cue = cast.Kind == SpellKind.Summon
                ? SummonCastCue(cast.Letter) : _profile.CastFor(cast.Element);
            Play(cue, origin, cast.Kind == SpellKind.Summon ? _summonElementGain : 1f);
        }

        private WorldMacroPlaytestAudioProfileSO.Cue SummonCastCue(char letter)
        {
            switch (letter)
            {
                case '곰': return _profile.CastWood;
                case '놈': return _profile.CastFire;
                case '몸': return _profile.CastEarth;
                case '솜': return _profile.CastMetal;
                case '옴': return _profile.CastWater;
                default: return null;
            }
        }

        private void OnEnemyHit(Vector3 point, Oheangbu.Core.Domain.Element element)
        {
            _enemyHitEvents++;
            _lastEvent = "enemy-hit";
            if (!CanPlay) return;
            Play(_profile.Impact, point);
        }

        private void OnPlayerDamaged(float damage)
        {
            if (damage <= 0f) return;
            _playerHitEvents++;
            _lastEvent = "player-hit";
            if (!CanPlay) return;
            Play(_profile.PlayerHit, transform.position);
        }

        private void OnParry(ParryOutcome outcome, Oheangbu.Core.Domain.Element element, Vector3 point)
        {
            _parryEvents++;
            _lastEvent = "parry-impact";
            if (!CanPlay) return;
            if (outcome == ParryOutcome.Success || outcome == ParryOutcome.Half || outcome == ParryOutcome.Block)
                Play(outcome==ParryOutcome.Block&&_profile.ExtendedPalette!=null?_profile.ExtendedPalette.Find("block"):_profile.Parry, point);
        }

        // D306 덩어리 뽑기: 시작에 HarvestStart, 끊을 때 HarvestEnd — 반복 소리 없음. 취소는 소리를 더하지 않는다.
        // [LEGACY] WorldMacroPlaytestAudioProfileSO.HarvestLoop(#132 홀드 갈무리의 반복음)은 프로필에 남기되 읽지 않는다.
        private void OnHarvestPullStarted(EnemyVitals target, Vector3 point)
        {
            _lastEvent = "harvest-pull";
            if (!CanPlay) return;
            Play(_profile.HarvestStart, point);
        }

        private void OnHarvested(EnemyVitals target, Vector3 point, float received)
        {
            _harvestEvents++;
            _lastEvent = "harvest-extracted";
            if (!CanPlay) return;
            Play(_profile.HarvestEnd != null && _profile.HarvestEnd.Clip != null ? _profile.HarvestEnd : _profile.Harvest, point);
        }

        private void OnJourneyInteraction(PrologueInteractionKind kind,Vector3 point,Oheangbu.App.Prologue.PrologueSession.InteractionResult result)
        {
            if(result==Oheangbu.App.Prologue.PrologueSession.InteractionResult.Success){OnInteractionResolved(kind,point);return;}
            _interactionEvents++;_lastEvent=result==Oheangbu.App.Prologue.PrologueSession.InteractionResult.SaveFailed?"save-failed":"interaction-waiting";
            if(CanPlay)Play(result==Oheangbu.App.Prologue.PrologueSession.InteractionResult.SaveFailed?_profile.SaveFailed:_profile.Waiting,point);
        }
        private void OnInteractionResolved(PrologueInteractionKind kind, Vector3 point)
        {
            _interactionEvents++;
            _lastEvent = kind == PrologueInteractionKind.Rest ? "rest" : "interaction";
            if (!CanPlay) return;
            var special=_profile.ExtendedPalette==null?null:_profile.ExtendedPalette.Find(kind==PrologueInteractionKind.Evidence?"clue":"");
            Play(special??(kind == PrologueInteractionKind.Rest ? _profile.Rest : _profile.Interact), point);
        }

        public void PresentVehicleCall(Vector3 point,bool answered){
            if(!CanPlay)return;
            var cue=_profile.ExtendedPalette!=null?_profile.ExtendedPalette.Find(answered?"vehicle_call":"vehicle_call_fail"):null;
            Play(cue??(answered?_profile.SummonAppear:_profile.Interact),point,answered?.5f:.35f);
        }
        private void OnSummonStarted(char letter, Vector3 point)
        {
            _summonAppearEvents++;
            _lastEvent = "summon-appearance";
            if (!CanPlay) return;
            Play(_profile.SummonAppear, point);
        }

        private void OnSummonReleased(char letter, Vector3 point)
        {
            _summonReleaseEvents++;
            _lastEvent = "summon-release";
            if (!CanPlay) return;
            Play(_profile.SummonRelease, point);
        }

        private void Play(WorldMacroPlaytestAudioProfileSO.Cue cue, Vector3 point, float gain = 1f)
        {
            if (!CanPlay || cue == null || cue.Clip == null || gain <= 0f) return;
            double now=AudioSettings.dspTime;
            if(_nextCueAt.TryGetValue(cue,out double next)&&now<next) { _cooldownDrops++; return; }
            _nextCueAt[cue]=now+Mathf.Max(.015f,cue.Cooldown);
            bool harvest = ReferenceEquals(cue,_profile.HarvestStart)||ReferenceEquals(cue,_profile.HarvestEnd);
            if(_pool.Play(cue,point,gain,UsesMixer?(harvest?_profile.Mix.Harvest:_profile.Mix.Sfx):null))
            { _cuePlays++; _lastCue=cue.Clip.name; }
        }
        private void StopAll() { _pool?.StopAll(true); _nextCueAt.Clear(); }
    }
}
