using System;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-DEPLOY-308 section 1: the scene-scoped host of the deploy layer (one per CombatLoopWiring, created by
    // CombatLoopWiring.BootDeploy308 when the profile's master switch is on; no global instance, no static state).
    //   hook A: effects reach it through SpellSequenceEffect.SetDeployHost (the adapter passes it on)
    //   hook B: the wiring's read-only re-broadcasts - casts the adapter skips (runtime-owned field / summon / ward / buff),
    //           the parry contact star, the CONFIRMED hit (EnemyDamageResolved: the struck enemy's hit reaction, and the hit
    //           splash of the live cast the hit belongs to) - and the adapter's misfire place
    // It owns the burst buffer pool, the residue field, the impact frame director, the hit flicker, the footprint emitter
    // and the HUD binder. Presentation only: it subscribes and asks (TargetGroggy), it never writes to combat.
    [DefaultExecutionOrder(-900)]   // LateUpdate after the brush adapter (-1000): a cast's own effect has announced itself by then
    public sealed class SpellDeployDirector308 : MonoBehaviour, ISpellDeployHost308, ISpellAirTailHost308
    {
        private const int MaxPending = 8, MaxHeld = 8, MaxOwn = 4, BuffKinds = 5, AuraKind = (int)Element.Fire;
        private struct Own { public InkDeployRuntime308 Runtime; public float Start; }
        private struct Pending { public SpellCast Cast; public Vector3 Origin, Forward; public int Frame; }
        private struct Began { public char Letter; public int Frame; }
        private struct Held { public int Owner; public float Until, Since; public char Letter; }

        private SpellDeploy308ProfileSO _profile;
        private CombatLoopWiring _wiring;
        private BrushStrokeFeedAdapter _adapter;
        private Camera _camera;
        private InkResidueField308 _field;
        private ImpactFrameDirector308 _impact;
        private HitFlicker308 _flicker;
        private FootprintEmitter308 _foot;
        private HudImpactBinder308 _hudBinder;
        private SpellPresentStage308 _present;   // #308 present add-on: what the spell presenter draws with (SPEC-SPELL-120-308 section 9)
        private UserSettingsService _settings;
        private DeployTier308 _tier;
        private bool _booted, _subscribed, _anyAttack, _wiringReady;
        private int _settingsRetry, _heldSerial;
        private Func<Transform, AreaImpactPlan, float, bool> _groggyProbe;

        private InkBurstBuffer308[] _buffers;
        private Mesh[] _meshes;
        private InkDeployRuntime308[] _owners;
        private int[] _rentOrder;
        private int _rentSerial;
        private readonly Pending[] _pending = new Pending[MaxPending];
        private readonly Began[] _began = new Began[MaxPending];
        private readonly Held[] _held = new Held[MaxHeld];
        private int _pendingCount, _beganNext;
        // ---- #308 forms2
        private int _generalSlots, _standingSlots;   // slots [0, general) = bursts, [general, general + standing) = standing bodies (O3)
        private bool[] _slotUsed;
        private uint[] _guardRevision;               // per slot: the guard's revision when a guard stroke rented it
        private readonly Own[] _own = new Own[MaxOwn];
        private readonly DeployDrop308[] _splash = new DeployDrop308[24];
        private Transform _player;
        private int _buffMask, _markTurn, _auraTurn;
        private float _nextWatch, _nextAura, _markTokens, _markTokenAt;

        public SpellDeploy308ProfileSO Profile => _profile;
        public InkResidueField308 Field => _field;
        public ImpactFrameDirector308 Impact => _impact;
        public HitFlicker308 Flicker => _flicker;
        public FootprintEmitter308 Foot => _foot;
        public HudImpactBinder308 HudBinder => _hudBinder;
        public SpellPresentStage308 Present => _present;
        public DeployTier308 Tier => _tier;
        public float CutPause => _impact != null ? _impact.CutPause : 0f;
        public Camera ViewCamera { get { if (_camera == null) _camera = Camera.main; return _camera; } }
        public int ActiveBursts { get { int n = 0; if (_owners != null) for (int i = 0; i < _generalSlots; i++) if (_owners[i] != null) n++; return n; } }
        public int PoolSize => _buffers != null ? _generalSlots : 0;
        public int ActiveStanding { get { int n = 0; if (_owners != null) for (int i = _generalSlots; i < _owners.Length; i++) if (_owners[i] != null) n++; return n; } }
        public int StandingSize => _buffers != null ? _standingSlots : 0;
        public int EvictedStanding { get; private set; }
        public int GuardContacts { get; private set; }
        public int SummonCurtains { get; private set; }
        public int SummonExits { get; private set; }
        public int BuffMarks { get; private set; }
        public int BuffEnds { get; private set; }
        public int AuraStamped { get; private set; }
        public int BuffMask => _buffMask;
        public int Evicted { get; private set; }
        public int HookBCasts { get; private set; }
        public int ParryStars { get; private set; }
        public int Misfires { get; private set; }
        /// <summary>D308-10c: confirmed hits handed to a live cast (it throws the hit splash), and confirmed hits no cast took.</summary>
        public int HitsRouted { get; private set; }
        public int HitsUnrouted { get; private set; }
        /// <summary>D308-10c: groggy questions asked by casts, and how many were answered yes.</summary>
        public int GroggyAsked { get; private set; }
        public int GroggyYes { get; private set; }

        public void Boot(SpellDeploy308ProfileSO profile, CombatLoopWiring wiring, BrushStrokeFeedAdapter adapter, HudController hud, Transform player,
            PlayerMotor motor, Func<float, float, bool> judgementNear, Func<Transform, AreaImpactPlan, float, bool> groggyProbe = null)
        {
            _profile = profile; _wiring = wiring; _adapter = adapter; _groggyProbe = groggyProbe; _player = player;
            if (profile == null) return;
            _tier = profile.TierForQuality(QualitySettings.names[QualitySettings.GetQualityLevel()]);
            var tier = profile.Tier(_tier);
            int bursts = Mathf.Clamp(tier.Bursts, 1, 8);
            // #308 forms2 O3: standing bodies have slots of their own behind the bursts'
            _generalSlots = bursts; _standingSlots = Mathf.Clamp(tier.StandingBursts, 0, 6);
            int slots = _generalSlots + _standingSlots;
            _buffers = new InkBurstBuffer308[slots]; _meshes = new Mesh[slots]; _owners = new InkDeployRuntime308[slots]; _rentOrder = new int[slots];
            _slotUsed = new bool[slots]; _guardRevision = new uint[slots];
            for (int i = 0; i < slots; i++)
            {
                _buffers[i] = new InkBurstBuffer308(i < _generalSlots ? tier.VertsPerBurst : tier.VertsPerStanding);
                _meshes[i] = new Mesh { name = "InkDeploy308_" + i, hideFlags = HideFlags.DontSave };
                _meshes[i].MarkDynamic();
            }
            _field = gameObject.AddComponent<InkResidueField308>(); _field.Configure(profile, _tier);
            _flicker = gameObject.AddComponent<HitFlicker308>(); _flicker.Configure(profile, _tier);
            _impact = gameObject.AddComponent<ImpactFrameDirector308>(); _impact.Configure(profile, _tier, judgementNear, () => ViewCamera, _flicker);
            if (player != null)
            {
                if (motor == null) motor = player.GetComponentInParent<PlayerMotor>() ?? player.GetComponentInChildren<PlayerMotor>(true);
                Transform body = motor != null ? motor.transform : player;
                _foot = gameObject.AddComponent<FootprintEmitter308>();
                _foot.Configure(profile, _field, body, motor, body.GetComponentInChildren<WorldMacroPlayerFootPlacement>(true),
                    body.GetComponentInParent<WorldMacroCombatWalker>() ?? body.GetComponentInChildren<WorldMacroCombatWalker>(true));
            }
            if (hud != null) { _hudBinder = gameObject.AddComponent<HudImpactBinder308>(); _hudBinder.Configure(hud, _impact, profile); }
            // #308 present add-on: the stage the spell presenter (ISpellPresenter) draws with. A failure here leaves the layer as it was.
            try { _present = gameObject.AddComponent<SpellPresentStage308>(); _present.Configure(profile, this, wiring); }
            catch (Exception e) { Debug.LogException(e, this); if (_present != null) Destroy(_present); _present = null; }
            _anyAttack = false;
            if (profile.Map != null && !string.IsNullOrEmpty(profile.EnabledLetters))
                foreach (char letter in profile.EnabledLetters)
                    if (profile.Map.TryGet(letter, out var row) && (row.Category == DeployCategory308.AttackSingle || row.Category == DeployCategory308.AttackArea)) { _anyAttack = true; break; }
            _booted = true;
            Subscribe();
            BindSettings();
            // Photosensitivity: until the user's own choice can be read (the settings service is not up yet, or this scene has
            // none) nothing flips the whole screen - the reduced form only, no HUD reaction, no camera breath.
            if (_settings == null) _impact.SetUserSettings(DeployFlash308.Reduced, false, true);
        }

        private void OnEnable() { if (_booted) Subscribe(); }

        private void Subscribe()
        {
            if (_subscribed || !isActiveAndEnabled) return;
            _subscribed = true;
            if (_wiring != null)
            {
                _wiring.CastAccepted += OnCastAccepted;
                _wiring.ParryResolved += OnParryResolved;
                _wiring.EnemyDamageResolved += OnEnemyDamage;
            }
            if (_adapter != null) { _adapter.DeployHost308 = this; _adapter.CastFailedAt308 += OnCastFailedAt; }
            if (_adapter != null) { _adapter.SummonPresentationStarted += OnSummonStarted; _adapter.SummonPresentationReleased += OnSummonReleased; }   // #308 forms2
            if (_foot != null) _foot.MarkSource = BuffMark;
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                _subscribed = false;
                if (_wiring != null)
                {
                    _wiring.CastAccepted -= OnCastAccepted;
                    _wiring.ParryResolved -= OnParryResolved;
                    _wiring.EnemyDamageResolved -= OnEnemyDamage;
                }
                if (_adapter != null) { if (ReferenceEquals(_adapter.DeployHost308, this)) _adapter.DeployHost308 = null; _adapter.CastFailedAt308 -= OnCastFailedAt; }
                if (_adapter != null) { _adapter.SummonPresentationStarted -= OnSummonStarted; _adapter.SummonPresentationReleased -= OnSummonReleased; }
                if (_foot != null) _foot.MarkSource = null;
            }
            for (int i = 0; i < MaxOwn; i++) if (_own[i].Runtime != null) _own[i].Runtime.Release();
            _buffMask = 0;
            if (_settings != null) { _settings.Changed -= OnSettingsChanged; _settings = null; }
            if (_owners != null) for (int i = 0; i < _owners.Length; i++) if (_owners[i] != null) _owners[i].Release();
            // marks held for a hook B cast start to dry now: nothing may stay wet without someone timing it
            for (int i = 0; i < MaxHeld; i++) { if (_held[i].Owner != 0 && _field != null) _field.ReleaseHeld(_held[i].Owner); _held[i] = default; }
            _pendingCount = 0;
        }

        private void OnDestroy()
        {
            if (_wiring != null) _wiring.UnhookDeploy308();   // the layer's listeners on the wiring leave with it
            if (_meshes == null) return;
            foreach (var mesh in _meshes) if (mesh != null) Destroy(mesh);
            _meshes = null;
        }

        // ---- user settings (read when they change, never per frame: UserSettingsService.Current clones) ----
        private void BindSettings()
        {
            if (_settings != null) return;
            var root = PlaytestUiRoot.Instance;
            if (root == null || root.Settings == null) return;
            _settings = root.Settings; _settings.Changed += OnSettingsChanged;
            OnSettingsChanged();
        }

        private void OnSettingsChanged()
        {
            if (_settings == null || _impact == null) return;
            var data = _settings.Current;
            _impact.SetUserSettings((DeployFlash308)Mathf.Clamp(data.ImpactFlash, 0, 2), data.HudImpactReact, data.ReducedMotion);
        }

        // ---- ISpellDeployHost308 ----
        public bool RentBurst(InkDeployRuntime308 owner, out InkBurstBuffer308 buffer, out Mesh mesh)
        {
            buffer = null; mesh = null;
            if (_buffers == null || owner == null) return false;
            // #308 forms2 O3: a burst only ever looks at the bursts' slots, a standing body only at the standing ones (the rule is
            // InkForms2Rules308.PickSlot: the first free slot of the range, else the one rented longest ago)
            InkForms2Rules308.SlotRange(owner.Standing, _generalSlots, _standingSlots, out int first, out int count);
            for (int i = 0; i < _owners.Length; i++) _slotUsed[i] = _owners[i] != null;
            int free = InkForms2Rules308.PickSlot(_slotUsed, _rentOrder, first, count, out bool evicts);
            if (free < 0) return false;
            if (evicts)
            {
                // the range is full: its oldest ends its hold and hands its buffer over (its residue is already in the field)
                var evicted = _owners[free];
                _owners[free] = null;
                if (evicted != null) evicted.Release();
                if (free >= _generalSlots) EvictedStanding++; else Evicted++;
            }
            _owners[free] = owner; _rentOrder[free] = ++_rentSerial;
            _guardRevision[free] = 0u;
            if (owner.Form == DeployForm308.Guard && _wiring != null)
            {
                try { _wiring.DeployGuardStands308(out _guardRevision[free]); } catch (Exception e) { Debug.LogException(e, this); }
            }
            buffer = _buffers[free]; mesh = _meshes[free];
            return true;
        }

        public void ReturnBurst(InkDeployRuntime308 owner, InkBurstBuffer308 buffer, Mesh mesh)
        {
            if (_owners == null) return;
            for (int i = 0; i < _owners.Length; i++) if (_buffers[i] == buffer && (_owners[i] == owner || _owners[i] == null)) { _owners[i] = null; return; }
        }

        public void StampResidue(in ResidueStamp308 stamp) { if (_field != null) _field.Stamp(stamp); }
        public void ReleaseHeld(int owner) { if (_field != null) _field.ReleaseHeld(owner); }
        public void SpawnAir(Vector3 worldPoint, Vector3 velocity, float size, int cell, float life, float gravityScale, float landY)
        { if (_field != null) _field.SpawnAir(worldPoint, velocity, size, cell, life, gravityScale, landY); }
        // pass 4b (Q6): ISpellAirTailHost308 - a tailed drop with its own ceiling on the drawn length (the presenter's thrown head)
        public void SpawnAir(Vector3 worldPoint, Vector3 velocity, float size, int cell, float life, float gravityScale, float landY, float tailMax)
        { if (_field != null) _field.SpawnAir(worldPoint, velocity, size, cell, life, gravityScale, landY, tailMax); }
        public void RequestImpact(in ImpactRequest308 request) { if (_impact != null) _impact.Request(request); }
        public void NotifyDeployBegan(char letter) { _began[_beganNext] = new Began { Letter = letter, Frame = Time.frameCount }; _beganNext = (_beganNext + 1) % MaxPending; }

        /// <summary>D308-10c: the one read-only question a cast may ask - is its judged target groggy right now (single: `target`;
        /// area: any target `plan` scheduled a hit on)? Answered by the wiring's probe; without one the answer is no.</summary>
        public bool TargetGroggy(Transform target, AreaImpactPlan plan)
        {
            GroggyAsked++;
            bool groggy = _groggyProbe != null && _profile != null && _groggyProbe(target, plan, _profile.Impact.GroggyKillGrace);
            if (groggy) GroggyYes++;
            return groggy;
        }

        /// <summary>D308-13b: does the deploy layer show this letter's enemy hit itself (the rule is InkDeployForms308.OwnsHitContact)?
        /// Only while the layer is up: the wiring then leaves out the KTP enemy-hit contact - the hit splash is the hit.</summary>
        public bool OwnsHitContact(char letter) => _booted && isActiveAndEnabled && _field != null && _field.Ready && InkDeployForms308.OwnsHitContact(_profile, letter);

        /// <summary>#308 present add-on: the wiring names the glyph of the hit it has just confirmed (one call per confirmed hit with an
        /// element, after EnemyDamageResolved and the hit hooks). The answer is the question it always asked - does the layer show
        /// this hit itself, so that the KTP contact stays out -, widened by the presenter stage to the glyphs it presents. Called
        /// from inside the damage path: a fault of the presentation stays here.</summary>
        public bool ConfirmedHitLetter(char letter)
        {
            bool owns = OwnsHitContact(letter);
            if (_present == null) return owns;
            try { return _present.ConfirmedHitLetter(letter, owns); }
            catch (Exception e) { Debug.LogException(e, this); return owns; }
        }

        // ---- hook B ----
        private void OnCastAccepted(SpellCast cast, Vector3 origin, Vector3 forward)
        {
            if (_profile == null || !_profile.IsEnabled(cast.Letter) || _pendingCount >= MaxPending) return;
            _pending[_pendingCount++] = new Pending { Cast = cast, Origin = origin, Forward = forward, Frame = Time.frameCount };
        }

        // Raised from inside the parry resolution (before the wiring's ink refund): a fault of this presentation stays here.
        private void OnParryResolved(ParryOutcome outcome, Element element, Vector3 point)
        {
            try { ShowParry(outcome, element, point); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private bool ParryEnabled(Element element)
        {
            var map = _profile.Map;
            if (map == null || map.Rows == null || string.IsNullOrEmpty(_profile.EnabledLetters)) return false;
            foreach (var row in map.Rows)
                if (row.Category == DeployCategory308.Parry && row.Element == element && _profile.IsEnabled(row.Char)) return true;
            return false;
        }

        // The wiring applied damage: a CONFIRMED hit (a planned hit whose target died first or was hidden never gets here).
        // It is raised from inside the damage path (ApplyConfirmedEnemyHit), so a fault of this presentation must stay here:
        // thrown onward it would cut that path short (the hit's sound, the rest of that frame's pending hits).
        private void OnEnemyDamage(EnemyDamageResult result)
        {
            try { ShowEnemyHit(result); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void ShowEnemyHit(EnemyDamageResult result)
        {
            if (result.Target == null || result.AppliedDamage <= 0f) return;
            bool routed = _anyAttack && ShowAttackHit(result);
            // #308 present add-on: the presenter stage hears every confirmed hit and whether a live cast took it (it waits for the
            // glyph, ConfirmedHitLetter, before it shows anything)
            if (_present != null) _present.NoteConfirmedHit(result, routed);
        }

        // true = the hit belongs to a live cast: it throws its splash, or it has already thrown every splash one cast may throw
        private bool ShowAttackHit(EnemyDamageResult result)
        {
            bool routed = false, capped = false;
            Transform target = result.Target.transform;
            // D308-10c hit splash: the hit goes to the live cast it belongs to - same element; a single cast's own judged target
            // before an area cast, the older cast first. That cast throws the splash. A hit no cast takes (a hostless EA
            // runtime, an evicted burst, a row that is not switched on) gets none.
            if (result.Attack.Source == DamageSource.PlayerDirect && result.Attack.Element.HasValue && _owners != null)
            {
                Element element = result.Attack.Element.Value;
                int pick = -1;
                for (int i = 0; i < _owners.Length; i++)
                {
                    var owner = _owners[i];
                    if (owner == null) continue;
                    if (!owner.AcceptsHit(target, element)) { capped |= SplashCapReached(owner, element); continue; }
                    if (pick < 0 || (_owners[pick].IsAreaCast != owner.IsAreaCast ? !owner.IsAreaCast : _rentOrder[i] < _rentOrder[pick])) pick = i;
                }
                if (pick >= 0 && _owners[pick].ConfirmHit(target)) { HitsRouted++; routed = true; } else HitsUnrouted++;
            }
            if (_flicker != null && _impact != null)
                _flicker.Notify(result.Target, result.Attack.Source, ViewCamera, _impact.Flash, Time.unscaledTimeAsDouble, true, target.position + Vector3.up * InkDeployRuntime308.ChestHeight);
            return routed || capped;
        }

        // #308 present add-on: an area cast that has thrown Hit.MaxPerCast splashes still owns the hits of its burst beat. The
        // presenter stage throws no free splash for them - the cap per cast is the layer's own rule (a volley, a crowd).
        private bool SplashCapReached(InkDeployRuntime308 owner, Element element)
        {
            if (!owner.IsAreaCast || owner.CastElement != element || owner.HitSplashes < _profile.Hit.MaxPerCast) return false;
            float burstEnd = owner.HoldEnd - _profile.Beats.HoldArea;
            return owner.CurrentCel <= Mathf.CeilToInt(burstEnd / Mathf.Max(.001f, owner.CelSeconds)) + 1;
        }

        private void OnCastFailedAt(Vector3 point)
        {
            if (_profile == null || _field == null || string.IsNullOrEmpty(_profile.EnabledLetters)) return;
            // a misfire: the smear alone, drying where the letter was
            _field.SpawnAir(point, Vector3.zero, _profile.Stroke.IgniteRadius * 3f, InkBurstMeshBuilder308.CellIgnite, .5f, 0f, 0f);
            Misfires++;
        }

        private void LateUpdate()
        {
            if (!_booted) return;
            if (_settings == null && (++_settingsRetry & 15) == 0) BindSettings();
            // the wiring's own Awake (its target list) may run after the container built this layer: its listeners are laid then
            if (!_wiringReady) _wiringReady = _wiring == null || _wiring.EnsureDeployHooks308();
            float now = Time.time;
            for (int i = 0; i < MaxHeld; i++)
                if (_held[i].Owner != 0 && now >= _held[i].Until) { _field.ReleaseHeld(_held[i].Owner); _held[i] = default; }
            // #308 forms2: the layer's own deploys (a summon's curtain), and the read-only watch
            SampleOwn(now);
            Watch(now);
            if (_pendingCount == 0) return;
            for (int i = 0; i < _pendingCount; i++)
            {
                var p = _pending[i];
                bool began = false;
                for (int k = 0; k < MaxPending; k++) if (_began[k].Letter == p.Cast.Letter && _began[k].Frame >= p.Frame) { began = true; break; }
                if (!began) HookB(p);
            }
            _pendingCount = 0;
        }

        // casts whose presentation is owned elsewhere get the ignition smear and their residue only
        private void HookB(in Pending pending)
        {
            if (_field == null || _profile.Map == null || !_profile.Map.TryGet(pending.Cast.Letter, out var row)) return;
            if (row.Category == DeployCategory308.AttackSingle || row.Category == DeployCategory308.AttackArea || row.Category == DeployCategory308.Parry) return;
            var category = _profile.CategoryOf(row.Category);
            var element = _profile.ElementOf(row.Element);
            var res = _profile.Residue;
            Vector3 forward = new Vector3(pending.Forward.x, 0f, pending.Forward.z);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            Vector3 anchor = pending.Origin + (row.Category == DeployCategory308.Summon ? forward * _profile.Stroke.SummonForward : Vector3.zero);
            float radius = row.Category == DeployCategory308.Ward ? 3f : row.Category == DeployCategory308.Field ? Mathf.Max(1.5f, pending.Cast.Area.Radius) : .9f;
            bool held = category.HeldResidue;
            int owner = 0;
            if (held)
            {
                // hook B ids count up from int.MinValue so they never meet a runtime instance id
                owner = int.MinValue + (++_heldSerial & 0xFFFFF);
                bool timed = false;
                // #308 forms2: a field's marks stay wet while its platform / bridge exists (Watch asks the wiring); the timer is
                // only the ceiling then. A field nobody can be asked about keeps the fallback time.
                bool asked = false, alive = false;
                if (row.Category == DeployCategory308.Field && _wiring != null)
                {
                    try { alive = _wiring.DeployFieldAlive308(pending.Cast.Letter, out asked); } catch (Exception e) { Debug.LogException(e, this); asked = false; }
                }
                float until = Time.time + (asked && alive ? Mathf.Max(res.FieldHoldFallback, _profile.FieldMark.MaxHold) : res.FieldHoldFallback);
                for (int i = 0; i < MaxHeld; i++) if (_held[i].Owner == 0) { _held[i] = new Held { Owner = owner, Until = until, Since = Time.time, Letter = asked && alive ? pending.Cast.Letter : default }; timed = true; break; }
                if (!timed) { held = false; owner = 0; }   // no timer slot left: an untimed hold would never dry, so it dries like any residue
            }
            float smear = _profile.Stroke.IgniteRadius * 2.4f;
            _field.Stamp(new ResidueStamp308 { Point = pending.Origin + Vector3.up * .5f, Forward = forward, Width = smear, Length = smear,
                Cell = InkBurstMeshBuilder308.CellIgnite, Opacity = res.Opacity, Life = _profile.SpellLife });
            var rng = new DeployRng308(pending.Cast.Letter * 7919 + pending.Frame);
            int drops = Mathf.RoundToInt(category.ResidueDrops * Mathf.Max(.5f, element.DropMul));
            for (int i = 0; i < drops; i++)
            {
                float a = rng.Next() * Mathf.PI * 2f, r = radius * (row.Category == DeployCategory308.Ward ? 1f : Mathf.Sqrt(rng.Next()));
                float pick = rng.Next();
                float size = (pick < .5f ? res.DropSmall : pick < .85f ? res.DropMid : res.DropLarge) * element.DropSizeMul * 2f;
                int cell = pick < .5f ? InkBurstMeshBuilder308.CellDrop : pick < .85f ? InkBurstMeshBuilder308.CellCluster : InkBurstMeshBuilder308.CellPuddle;
                if (cell == InkBurstMeshBuilder308.CellCluster) size *= 2.2f;
                _field.Stamp(new ResidueStamp308 { Point = anchor + new Vector3(Mathf.Cos(a) * r, .5f, Mathf.Sin(a) * r), Forward = forward, Width = size, Length = size,
                    Cell = cell, Opacity = res.Opacity, Life = held ? _profile.FieldTail : _profile.SpellLife, Held = held, Owner = owner });
            }
            HookBCasts++;
        }

        // ---- #308 forms2 (Tools/Unity/Stage308_forms2/DESIGN.md): standing slots, the guard stroke's contacts, the summon's
        // curtain, buff marks and field marks. Everything here listens or asks; every question goes through a read-only probe
        // of the wiring, and a fault stays here.

        /// <summary>S1: an effect an EA runtime builds itself gets this host before its Begin (so that its residue, its hit
        /// splash and its impact frame are not dropped and it is drawn with the tier's counts). A field assignment: it never throws.</summary>
        public void HostEffect(SpellSequenceEffect effect, float grade01 = -1f)
        {
            try { if (effect != null && _booted && isActiveAndEnabled && _profile != null) effect.SetDeployHost(this, grade01 >= 0f ? Mathf.Clamp01(grade01) : _profile.Comet.EaGrade01); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        /// <summary>Does the layer show the parry of this element itself (the guard stroke and its contacts)? True for an enabled
        /// parry row whose catalogue body is retired. The wiring then leaves out the KTP contact and the burst quads - never the
        /// ink refund or the reticle.</summary>
        public bool OwnsParry(Element element)
        {
            if (!_booted || !isActiveAndEnabled || _profile == null || _field == null || !_field.Ready || !_profile.Guard.Enabled) return false;
            var map = _profile.Map;
            if (map == null || map.Rows == null) return false;
            foreach (var row in map.Rows)
                if (row.Category == DeployCategory308.Parry && row.Element == element && row.LegacyBody == DeployLegacyBody308.Retire && _profile.IsEnabled(row.Char)) return true;
            return false;
        }

        /// <summary>Does the layer draw this summon's entrance and exit itself (so the combat summon starts no formation seal and no
        /// dissolve debris)? The model, its clock and its sounds are not part of the question.</summary>
        public bool OwnsSummonEntrance(char letter)
        {
            if (!_booted || !isActiveAndEnabled || _profile == null || _field == null || !_field.Ready || !_profile.Summon.OwnEntrance || !_profile.IsEnabled(letter)) return false;
            return _profile.Map != null && _profile.Map.TryGet(letter, out var row) && row.Category == DeployCategory308.Summon;
        }

        private void ShowParry(ParryOutcome outcome, Element element, Vector3 point)
        {
            if (_profile == null || _field == null || outcome == ParryOutcome.None) return;
            if (!OwnsParry(element))
            {
                // the guard body is the catalogue's: the contact star alone, as before
                if ((outcome != ParryOutcome.Success && outcome != ParryOutcome.Half) || !ParryEnabled(element)) return;
                _field.SpawnAir(point, Vector3.zero, outcome == ParryOutcome.Success ? 1.1f : .6f, InkBurstMeshBuilder308.CellStarB, .15f, 0f, 0f);
                ParryStars++;
                return;
            }
            var g = _profile.Guard;
            GuardContact308 contact = outcome == ParryOutcome.Success ? GuardContact308.Success : outcome == ParryOutcome.Half ? GuardContact308.Half
                : outcome == ParryOutcome.Block ? GuardContact308.Block : GuardContact308.Fail;
            switch (contact)
            {
                case GuardContact308.Success: _field.SpawnAir(point, Vector3.zero, g.SuccessStar, InkBurstMeshBuilder308.CellStarB, g.StarSeconds, 0f, 0f); ParryStars++; break;
                case GuardContact308.Half: _field.SpawnAir(point, Vector3.zero, g.HalfStar, InkBurstMeshBuilder308.CellStarB, g.StarSeconds, 0f, 0f); ParryStars++; break;
                case GuardContact308.Block: _field.SpawnAir(point, Vector3.zero, g.BlockStar, InkBurstMeshBuilder308.CellStar, g.BlockStarSeconds, 0f, 0f); break;
                default: _field.SpawnAir(point, Vector3.zero, g.FailBlot, InkBurstMeshBuilder308.CellIgnite, g.FailBlotSeconds, 0f, 0f); break;
            }
            Camera camera = ViewCamera;
            Vector3 away = camera != null ? point - camera.transform.position : Vector3.forward;   // the attacker stands beyond the contact point
            int drops = InkForms2Rules308.GuardSplash(_profile, _tier, contact, point, away, Time.frameCount * 31 + (int)contact, _splash);
            for (int i = 0; i < drops; i++)
                _field.SpawnAir(_splash[i].Local, _splash[i].Velocity, _splash[i].Size, _splash[i].Cell, _splash[i].Life, 1f, point.y - InkDeployRuntime308.ChestHeight);
            GuardContacts++;
            if (_owners == null) return;
            // a parry consumes the guard on the rule side: the stroke that shows it is cut
            for (int i = _generalSlots; i < _owners.Length; i++)
                if (_owners[i] != null && _owners[i].Form == DeployForm308.Guard && _owners[i].CastElement == element) _owners[i].NoteGuardContact(contact);
        }

        private void OnSummonStarted(char letter, Vector3 point)
        {
            try
            {
                if (!OwnsSummonEntrance(letter)) return;
                // the cast's own effect (a scene without combat summons) has drawn the curtain already
                for (int k = 0; k < MaxPending; k++) if (_began[k].Letter == letter && _began[k].Frame == Time.frameCount) return;
                if (!_profile.Map.TryGet(letter, out var row)) return;
                Camera camera = ViewCamera;
                Vector3 toward = camera != null ? point - camera.transform.position : Vector3.forward;
                toward.y = 0f;
                toward = toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector3.forward;
                var cast = new DeployCast308
                {
                    Profile = _profile, Row = row, Origin = point, FallbackPoint = point + toward * 2f, Grade01 = _profile.Comet.EaGrade01, Seed = letter * 7919 + Time.frameCount,
                    Tier = _tier, Anchor = point, HasAnchor = true, NewForms = true,
                };
                if (SpawnOwn(cast)) SummonCurtains++;
            }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private void OnSummonReleased(char letter, Vector3 point)
        {
            try
            {
                if (!OwnsSummonEntrance(letter)) return;
                var su = _profile.Summon;
                _field.Stamp(new ResidueStamp308 { Point = point + Vector3.up * .5f, Forward = Vector3.forward, Width = su.ExitPuddle, Length = su.ExitPuddle,
                    Cell = InkBurstMeshBuilder308.CellPuddle, Opacity = _profile.Residue.Opacity, Life = _profile.SpellLife });
                int drops = _tier == DeployTier308.Mobile ? su.ExitDropsMobile : su.ExitDrops;
                var rng = new DeployRng308(letter * 131 + Time.frameCount);
                for (int i = 0; i < drops; i++)
                {
                    float a = (i + rng.Next()) / Mathf.Max(1, drops) * Mathf.PI * 2f;
                    Vector3 from = point + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rng.Range(su.ExitRadiusMin, Mathf.Max(su.ExitRadiusMin, su.ExitRadiusMax))
                        + Vector3.up * rng.Range(su.ExitHeightMin, Mathf.Max(su.ExitHeightMin, su.ExitHeightMax));
                    _field.SpawnAir(from, Vector3.zero, _profile.Residue.DropMid * su.ExitDropSizeMul, InkBurstMeshBuilder308.CellDropTailed, su.ExitDropSeconds, 1f, point.y);
                }
                SummonExits++;
            }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        // the layer's own deploys are sampled in this component's LateUpdate: a fault of one of them takes that one down and
        // nothing else (the watch and the hook B queue behind it still run this frame, and the next)
        private void SampleOwn(float now)
        {
            for (int i = 0; i < MaxOwn; i++)
            {
                if (_own[i].Runtime == null || !_own[i].Runtime.Configured) continue;
                try { _own[i].Runtime.Sample(now - _own[i].Start); }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                    try { _own[i].Runtime.Release(); } catch (Exception inner) { Debug.LogException(inner, this); _own[i].Runtime = null; }
                }
            }
        }

        // a deploy the layer starts by itself (the curtain at a combat summon's place): a few pooled holders, sampled here
        private bool SpawnOwn(in DeployCast308 cast)
        {
            int slot = -1;
            for (int i = 0; i < MaxOwn; i++) if (_own[i].Runtime == null || !_own[i].Runtime.Configured) { slot = i; break; }
            if (slot < 0) return false;
            if (_own[slot].Runtime == null)
            {
                var holder = new GameObject("InkDeploy308_Own" + slot) { hideFlags = HideFlags.DontSave };
                holder.transform.SetParent(transform, false);
                _own[slot].Runtime = holder.AddComponent<InkDeployRuntime308>();
            }
            _own[slot].Start = Time.time;
            return _own[slot].Runtime.Configure(cast, this);
        }

        /// <summary>S6: the footprint emitter asks, for the step it has just printed, whether a buff mark goes beside it
        /// (`index` 0, 1, ... until false). One mark per running buff in turn, the tier's number per step and per second at most.</summary>
        public bool BuffMark(int index, out int cell, out float size)
        {
            cell = 0; size = 0f;
            if (_profile == null || _buffMask == 0) return false;
            int running = 0;
            for (int i = 0; i < BuffKinds; i++) if ((_buffMask >> i & 1) != 0) running++;
            if (index >= InkForms2Rules308.FootMarksForStep(_profile, _tier, running)) return false;
            var tier = _profile.Tier(_tier);
            float now = Time.time;
            _markTokens = Mathf.Min(Mathf.Max(1, tier.FootMarksPerStep), _markTokens + Mathf.Max(0f, now - _markTokenAt) * tier.FootMarksPerSecond);
            _markTokenAt = now;
            if (_markTokens < 1f) return false;
            _markTokens -= 1f;
            for (int k = 0; k < BuffKinds; k++) { _markTurn = (_markTurn + 1) % BuffKinds; if ((_buffMask >> _markTurn & 1) != 0) break; }
            cell = _profile.BuffMarkCell(_markTurn); size = _profile.Buff.FootMarkSize;
            BuffMarks++;
            return true;
        }

        // asked a few times per second: is the guard a stroke shows still standing, which buffs run, do held field marks still have their field
        private void Watch(float now)
        {
            if (now < _nextWatch || _wiring == null || _profile == null) return;
            _nextWatch = now + 1f / Mathf.Clamp(_profile.Buff.WatchHz, 1f, 30f);
            try
            {
                if (_owners != null)
                {
                    bool stands = _wiring.DeployGuardStands308(out uint revision);
                    for (int i = _generalSlots; i < _owners.Length; i++)
                        if (_owners[i] != null && _owners[i].Form == DeployForm308.Guard && (!stands || revision != _guardRevision[i])) _owners[i].EndGuard();
                }
                // only the buffs the layer shows itself (their letter switched on, their catalogue body retired by the map):
                // a buff the catalogue still shows gets no mark, no aura ring and no end drops beside its own body
                int mask = InkForms2Rules308.BuffMaskShown(_profile, _wiring.DeployBuffMask308());
                int ended = _buffMask & ~mask;
                _buffMask = mask;
                if (ended != 0 && _field != null) BuffEnded();
                if ((mask >> AuraKind & 1) != 0 && now >= _nextAura && _field != null)
                {
                    _nextAura = now + Mathf.Max(.1f, _profile.Buff.AuraInterval);
                    AuraStamps(_wiring.DeployAuraRadius308());
                }
                for (int i = 0; i < MaxHeld; i++)
                {
                    if (_held[i].Owner == 0 || _held[i].Letter == default || now < _held[i].Since + _profile.FieldMark.WatchGrace) continue;
                    if (_wiring.DeployFieldAlive308(_held[i].Letter, out bool known) || !known) continue;
                    _field.ReleaseHeld(_held[i].Owner); _held[i] = default;   // the platform / bridge is gone: its marks start to dry (FieldTail)
                }
            }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        // the fire aura's reach, left as a dotted ring: a few drops on it each time the rule measures its damage
        private void AuraStamps(float radius)
        {
            if (_player == null || radius <= 0f) return;
            int stamps = _profile.Tier(_tier).AuraStamps;
            float size = _profile.Residue.DropMid * _profile.Buff.AuraSizeMul;
            for (int i = 0; i < stamps; i++)
            {
                float a = (_auraTurn * _profile.Buff.AuraTurnDeg + i * 360f / Mathf.Max(1, stamps)) * Mathf.Deg2Rad;
                _field.Stamp(new ResidueStamp308 { Point = _player.position + new Vector3(Mathf.Cos(a) * radius, .5f, Mathf.Sin(a) * radius), Forward = Vector3.forward, Width = size, Length = size,
                    Cell = InkBurstMeshBuilder308.CellDrop, Opacity = _profile.Residue.Opacity, Life = _profile.BuffAuraLife });
            }
            _auraTurn++; AuraStamped += stamps;
        }

        // a buff ran out: tailed drops are tossed up inside the lower view and fall out of it, and a ring is left at the feet
        private int _shedSeed = 308;   // forms4 (P8): which buff end this is - the seed of its drops' differences

        private void BuffEnded()
        {
            var b = _profile.Buff;
            Camera camera = ViewCamera;
            Vector3 feet = _player != null ? _player.position : transform.position;
            if (camera != null)
            {
                for (int i = 0; i < b.ShedDrops; i++)
                {
                    float k = b.ShedDrops <= 1 ? 0f : i / (float)(b.ShedDrops - 1) - .5f;
                    InkForms2Rules308.BuffShed(_profile, camera.transform.position, camera.transform.forward, k, out Vector3 from, out Vector3 velocity);
                    float vary = InkDeployForms308.BuffShedVary(_profile, _shedSeed, i, ref from, ref velocity);   // forms4 (P8): no three copies
                    _field.SpawnAir(from, velocity, _profile.Residue.DropMid * b.ShedSizeMul * vary, InkBurstMeshBuilder308.CellDropTailed, b.ShedSeconds, 1f, feet.y, b.ShedTailMax);   // pass 4c: drawn at most ShedTailMax sizes long
                }
            }
            _shedSeed++;   // the next end is another one (counted, not random)
            if (b.EndRing > 0f)
                _field.Stamp(new ResidueStamp308 { Point = feet + Vector3.up * .5f, Forward = Vector3.forward, Width = b.EndRing, Length = b.EndRing, Cell = InkBurstMeshBuilder308.CellRing,
                    Opacity = _profile.Residue.Opacity, Life = _profile.SpellLife });
            BuffEnds++;
        }

        /// <summary>One line for probes and the status command.</summary>
        public string Describe()
        {
            int spell = 0, foot = 0, air = 0;
            if (_field != null) _field.CountAlive(out spell, out foot, out air);
            return "tier=" + _tier + " bursts=" + ActiveBursts + "/" + PoolSize + " evicted=" + Evicted + " standing=" + ActiveStanding + "/" + StandingSize + " evictedStanding=" + EvictedStanding
                + " forms2(guard=" + GuardContacts + " curtain=" + SummonCurtains + "/" + SummonExits + " buffs=" + _buffMask + " marks=" + BuffMarks + " aura=" + AuraStamped + " ends=" + BuffEnds + ") residue=" + spell + " foot=" + foot + " air=" + air
                + " queued=" + (_field != null ? _field.Queued : 0) + " impact(full=" + (_impact != null ? _impact.FullGranted : 0) + " local=" + (_impact != null ? _impact.LocalGranted : 0)
                + " gate=" + (_impact != null ? _impact.SuppressedByGate : 0) + " limiter=" + (_impact != null ? _impact.SuppressedByLimiter : 0)
                + " pass=" + (_impact != null && _impact.PassAvailable) + ") groggy=" + GroggyYes + "/" + GroggyAsked + " hits(routed=" + HitsRouted + " unrouted=" + HitsUnrouted
                + ") flicker=" + (_flicker != null ? _flicker.Scheduled : 0) + " hookB=" + HookBCasts
                + " parryStars=" + ParryStars + " misfires=" + Misfires + " footprints=" + (_foot != null ? _foot.Emitted : 0) + " settings=" + (_settings != null)
                + (_present != null ? " | present(" + _present.Describe() + ")" : "");
        }
    }
}
