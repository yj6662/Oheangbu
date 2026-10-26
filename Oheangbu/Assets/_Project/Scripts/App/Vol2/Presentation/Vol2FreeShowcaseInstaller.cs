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
                Wrap(model, new Vol2LegacyVerbRecipe(Frame.Path, Verb.Leave).Kind, p.transform, ('각', () => new Vol2FsGak()));
                // batch 2 (2026-10-26): A0 거 너 머 서 어 (the guard on the brush, its parry reaction) · R0 구 누 무 수 우 (the instant ring)
                Wrap(model, Vol2Recipe.AttachOnce, p.transform, ('거', () => new Vol2FsGeo()), ('너', () => new Vol2FsNeo()), ('머', () => new Vol2FsMeo()), ('서', () => new Vol2FsSeo()), ('어', () => new Vol2FsEo()));
                Wrap(model, Vol2Recipe.RingOnce, p.transform, ('구', () => new Vol2FsGu()), ('누', () => new Vol2FsNu()), ('무', () => new Vol2FsMu()), ('수', () => new Vol2FsSu()), ('우', () => new Vol2FsU()));
            }
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
            foreach (var r in _recipes) r.ClearAll();
            _kit?.Dispose();
        }
    }
}
