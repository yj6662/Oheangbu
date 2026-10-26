using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App
{
    /// <summary>D308-150 V10-V11 free showcase switch. Auto = ON in the spell lab scene only (Vol2LabRequestFile.LabScenePath), OFF everywhere else; On / Off force it.
    /// The editor command (Vol2FreeShowcaseCommands free:on|off|auto) sets it; it is reset to Auto when Play starts (domain reload is off: SubsystemRegistration).</summary>
    public enum Vol2FreeShowcaseMode { Auto = 0, On = 1, Off = 2 }

    /// <summary>Puts the showcase recipes of 나 (P0) / 고 (F0) / 각 (P1) in front of the legacy bridge's recipes of every vol.2 view of the scene, while the switch is on.
    /// No live file is changed for it: the installer finds the views (Vol2VfxPresenter) itself and swaps the registry entries (Vol2VfxRecipeRegistry.Set, the
    /// capture / fixture hook); switched off, the showcase recipes pass every cast to the bridge recipe they wrap. One installer per scene, made by the bootstrap
    /// below only when the lab scene is loaded (or the switch is forced On); it owns the kit and destroys it with itself. Not a singleton: nothing static points at it.</summary>
    public sealed class Vol2FreeShowcaseInstaller : MonoBehaviour
    {
        static Vol2FreeShowcaseMode _mode;
        public static Vol2FreeShowcaseMode Mode { get => _mode; set => _mode = value; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _mode = Vol2FreeShowcaseMode.Auto; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (_mode == Vol2FreeShowcaseMode.Off) return;
            if (_mode == Vol2FreeShowcaseMode.Auto && !LabLoaded()) return;
            if (FindFirstObjectByType<Vol2FreeShowcaseInstaller>() != null) return;
            new GameObject("Vol2FreeShowcaseInstaller").AddComponent<Vol2FreeShowcaseInstaller>();
        }

        public static bool LabLoaded()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).path == Vol2LabRequestFile.LabScenePath) return true;
            return false;
        }

        /// <summary>Is the showcase look on right now (read per cast by the recipes)?</summary>
        public bool On => _mode == Vol2FreeShowcaseMode.On || (_mode == Vol2FreeShowcaseMode.Auto && LabLoaded());

        Vol2FsKit _kit;
        readonly List<Vol2FreeShowcaseRecipe> _recipes = new List<Vol2FreeShowcaseRecipe>();
        readonly HashSet<Vol2VfxModel> _done = new HashSet<Vol2VfxModel>();
        float _next;
        public int Installed => _done.Count;
        public IReadOnlyList<Vol2FreeShowcaseRecipe> Recipes => _recipes;
        public Vol2FsKit Kit => _kit;

        void Awake() { _kit = new Vol2FsKit(); if (!_kit.Ok) Debug.LogWarning("[Vol2FreeShowcase] " + _kit.Problem + ": the bridge looks stay"); }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            foreach (var p in FindObjectsByType<Vol2VfxPresenter>(FindObjectsSortMode.None))
            {
                var model = p != null ? p.Model : null;
                if (model == null || _done.Contains(model)) continue;
                _done.Add(model);
                // batch 0 (a61dce39): 나 고 각; batch 1 (2026-10-26): P0 가 마 사 아 · F0 노 모 소 오 - each glyph its own class
                Wrap(model, Vol2Recipe.PathOneHit, p.transform, ('나', () => new Vol2FsNa()), ('가', () => new Vol2FsGa()), ('마', () => new Vol2FsMa()), ('사', () => new Vol2FsSa()), ('아', () => new Vol2FsA()));
                Wrap(model, Vol2Recipe.FanOnce, p.transform, ('고', () => new Vol2FsGo()), ('노', () => new Vol2FsNo()), ('모', () => new Vol2FsMo()), ('소', () => new Vol2FsSo()), ('오', () => new Vol2FsO()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Path, Verb.Leave).Kind, p.transform, ('각', () => new Vol2FsGak()), ('낙', () => new Vol2FsNak()), ('막', () => new Vol2FsMak()), ('삭', () => new Vol2FsSak()), ('악', () => new Vol2FsAk()));
                // batch 2 (2026-10-26): A0 거 너 머 서 어 (the guard on the brush, its parry reaction) · R0 구 누 무 수 우 (the instant ring)
                Wrap(model, Vol2Recipe.AttachOnce, p.transform, ('거', () => new Vol2FsGeo()), ('너', () => new Vol2FsNeo()), ('머', () => new Vol2FsMeo()), ('서', () => new Vol2FsSeo()), ('어', () => new Vol2FsEo()));
                Wrap(model, Vol2Recipe.RingOnce, p.transform, ('구', () => new Vol2FsGu()), ('누', () => new Vol2FsNu()), ('무', () => new Vol2FsMu()), ('수', () => new Vol2FsSu()), ('우', () => new Vol2FsU()));
                // batch 3 (2026-10-26): 남긴다 - P1 낙 막 삭 악 (with 각) · A1 넉 먹 석 억 · F1 곡 녹 목 속 옥 · R1 국 눅 묵 숙 욱 (걱 = the recovery, below)
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Attachment, Verb.Leave).Kind, p.transform, ('넉', () => new Vol2FsNeok()), ('먹', () => new Vol2FsMeok()), ('석', () => new Vol2FsSeok()), ('억', () => new Vol2FsEok()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Fan, Verb.Leave).Kind, p.transform, ('곡', () => new Vol2FsGok()), ('녹', () => new Vol2FsNok()), ('목', () => new Vol2FsMok()), ('속', () => new Vol2FsSok()), ('옥', () => new Vol2FsOk()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Ring, Verb.Leave).Kind, p.transform, ('국', () => new Vol2FsGuk()), ('눅', () => new Vol2FsNuk()), ('묵', () => new Vol2FsMuk()), ('숙', () => new Vol2FsSuk()), ('욱', () => new Vol2FsUk()));
                // batch 4 (2026-10-26): 번진다 - P2 간 난 만 산 안 · A2 건 넌 먼 선 언 · F2 곤 논 몬 손 온 · R2 군 눈 문 순 운 (Vol2FsSpread: hops / marks / 넌's carrier / 눈's burns)
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Path, Verb.Spread).Kind, p.transform, ('간', () => new Vol2FsGan()), ('난', () => new Vol2FsNan()), ('만', () => new Vol2FsMan()), ('산', () => new Vol2FsSan()), ('안', () => new Vol2FsAn()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Attachment, Verb.Spread).Kind, p.transform, ('건', () => new Vol2FsGeon()), ('넌', () => new Vol2FsNeon()), ('먼', () => new Vol2FsMeon()), ('선', () => new Vol2FsSeon()), ('언', () => new Vol2FsEon()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Fan, Verb.Spread).Kind, p.transform, ('곤', () => new Vol2FsGon()), ('논', () => new Vol2FsNon()), ('몬', () => new Vol2FsMon()), ('손', () => new Vol2FsSon()), ('온', () => new Vol2FsOn()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Ring, Verb.Spread).Kind, p.transform, ('군', () => new Vol2FsGun()), ('눈', () => new Vol2FsNun()), ('문', () => new Vol2FsMun()), ('순', () => new Vol2FsSun()), ('운', () => new Vol2FsUn()));
                // batch 5 (2026-10-27): 심는다 - P3 감 남 맘 삼 암 · A3 검 넘 멈 섬 엄 · F3 곰 놈 몸 솜 옴 · R3 굼 눔 뭄 숨 움 (Vol2FsSeed: plant / wait / trigger / exits, 몸's rubble, 뭄's ground)
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Path, Verb.Seed).Kind, p.transform, ('감', () => new Vol2FsGam()), ('남', () => new Vol2FsNam()), ('맘', () => new Vol2FsMam()), ('삼', () => new Vol2FsSam()), ('암', () => new Vol2FsAm()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Attachment, Verb.Seed).Kind, p.transform, ('검', () => new Vol2FsGeom()), ('넘', () => new Vol2FsNeom()), ('멈', () => new Vol2FsMeom()), ('섬', () => new Vol2FsSeom()), ('엄', () => new Vol2FsEom()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Fan, Verb.Seed).Kind, p.transform, ('곰', () => new Vol2FsGom()), ('놈', () => new Vol2FsNom()), ('몸', () => new Vol2FsMom()), ('솜', () => new Vol2FsSom()), ('옴', () => new Vol2FsOm()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Ring, Verb.Seed).Kind, p.transform, ('굼', () => new Vol2FsGum()), ('눔', () => new Vol2FsNum()), ('뭄', () => new Vol2FsMum()), ('숨', () => new Vol2FsSum()), ('움', () => new Vol2FsUm()));
                // batch 6 (2026-10-27): 가른다 - P4 갓 낫 맛 삿 앗 · A4 것 넛 멋 섯 엇 · F4 곳 놋 못 솟 옷 · R4 굿 눗 뭇 숫 웃 (Vol2FsCut: the executor's splits / strip book, 솟's metal)
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Path, Verb.Split).Kind, p.transform, ('갓', () => new Vol2FsGat()), ('낫', () => new Vol2FsNat()), ('맛', () => new Vol2FsMatt()), ('삿', () => new Vol2FsSat()), ('앗', () => new Vol2FsAt()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Attachment, Verb.Split).Kind, p.transform, ('것', () => new Vol2FsGeot()), ('넛', () => new Vol2FsNeot()), ('멋', () => new Vol2FsMeot()), ('섯', () => new Vol2FsSeot()), ('엇', () => new Vol2FsEot()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Fan, Verb.Split).Kind, p.transform, ('곳', () => new Vol2FsGot()), ('놋', () => new Vol2FsNot()), ('못', () => new Vol2FsMot()), ('솟', () => new Vol2FsSot()), ('옷', () => new Vol2FsOt()));
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Ring, Verb.Split).Kind, p.transform, ('굿', () => new Vol2FsGut()), ('눗', () => new Vol2FsNut()), ('뭇', () => new Vol2FsMut()), ('숫', () => new Vol2FsSut()), ('웃', () => new Vol2FsUt()));
                if (_view == null) _view = p;
            }
            BindRecovery();
        }

        void LateUpdate()
        {
            // batch 3: 걱 ticks every frame on the scope clock (the recovery has no cast log, so no vol.2 instance carries it)
            if (_geok == null || _view == null || _view.Coordinator == null) return;
            long now = _view.Coordinator.Services.Clock.NowUs;
            try { _geok.Tick(now); if (_geok.Done(now)) { _geok.Dispose(); _geok = null; } }
            catch (System.Exception e) { Debug.LogException(e); _geok?.Dispose(); _geok = null; }
        }

        // ---------------------------------------------------------------- batch 3: 걱, the recovery (the defence kit's Vol2RecoveryExecutor: records, never answered)
        Vol2VfxPresenter _view;
        Vol2RecoveryExecutor _recovery; Vol2DefenceKit _defence; System.Action<Vol2RecoveryRecord> _onRecovery; int _recoverySearches;
        Vol2FsGeok _geok;
        public int GeokShown { get; private set; }
        public string GeokNote => _geok != null ? _geok.Note : _geokLast;
        string _geokLast = "";

        void BindRecovery()
        {
            if (_view != null && _view.Legacy != null) _view.Legacy.RecoveryShownElsewhere = On && _kit != null && _kit.Ok;
            if (_recovery != null || _recoverySearches >= 40) return;
            _recoverySearches++;
            var wiring = FindFirstObjectByType<CombatLoopWiring>();
            var kit = wiring != null ? wiring.Vol2Defence : null;
            if (kit == null || kit.Recovery == null) return;
            _defence = kit; _recovery = kit.Recovery;
            _recovery.Recorded += _onRecovery = OnRecovery;
        }

        void OnRecovery(Vol2RecoveryRecord rec)
        {
            try
            {
                if (_view == null || _view.Coordinator == null || !On || _kit == null || !_kit.Ok) return;
                long now = _view.Coordinator.Services.Clock.NowUs;
                if (rec.Kind == "Begin" && rec.Accepted && rec.Active)
                {
                    if (_geok != null) { _geok.End("Replaced", now); _geokLast = _geok.Note; }
                    var st = _defence != null && _defence.Session != null ? _defence.Session.Recovery : null;
                    long start = st != null && st.Active ? st.StartedUs : now, life = st != null ? st.Numbers.HealingLifeUs : 3000000;
                    var caster = _view.Coordinator.Services.Shapes != null ? _view.Coordinator.Services.Shapes.Caster : null;
                    var model = _view.Model;
                    var g = new Vol2FsGeok(_kit, _view.transform, () => caster != null ? caster.Position : _view.transform.position,
                        () => caster != null ? caster.AimDirection : Vector3.forward, model != null ? model.BrushTip : null, model != null ? model.BrushGrip : null);
                    g.Begin(start, life, now, (float)rec.HpReserved);
                    if (_geok != null) _geok.Dispose();
                    _geok = g; GeokShown++;
                    return;
                }
                if (_geok != null) _geok.Record(rec, now);
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        void Wrap(Vol2VfxModel model, Vol2Recipe kind, Transform parent, params (char Glyph, System.Func<Vol2FsRun> Make)[] glyphs)
        {
            if (!model.Recipes.TryGet(kind, out var inner) || inner is Vol2FreeShowcaseRecipe) return;
            var makers = new Dictionary<char, System.Func<Vol2FsRun>>();
            var order = new System.Text.StringBuilder();
            foreach (var g in glyphs) { makers[g.Glyph] = g.Make; order.Append(g.Glyph); }
            var r = new Vol2FreeShowcaseRecipe(inner, makers, order.ToString(), () => _kit, () => On, parent);
            model.Recipes.Set(r);
            _recipes.Add(r);
        }

        void OnDestroy()
        {
            if (_recovery != null && _onRecovery != null) _recovery.Recorded -= _onRecovery;
            _recovery = null; _onRecovery = null;
            if (_view != null && _view.Legacy != null) _view.Legacy.RecoveryShownElsewhere = false;
            _geok?.Dispose(); _geok = null;
            foreach (var r in _recipes) r.ClearAll();
            _kit?.Dispose();
        }
    }
}
