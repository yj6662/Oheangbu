using System;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.Spell;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 9: the production side of the vol.2 swing driver - the player's great brush as the driver's source, the combat wiring as its 갈무리 port, the
    /// MonoBehaviour that ticks it, and the one call that binds all of it in a Vol2 scope (Vol2DefenceInstaller.BindSeams). Nothing here exists in Legacy308.</summary>
    public sealed class Vol2RigSwingSource : IVol2SwingSource
    {
        readonly Vol2SwingTuning _tuning;
        SpellSwing308 _swing;
        WorldMacroPlayerGestureRig _rig;
        GreatBrush308Profile _profile;
        Transform _root, _tip, _grip, _gripL;
        float _nextSearch;

        public Vol2RigSwingSource(Vol2SwingTuning tuning) { _tuning = tuning ?? new Vol2SwingTuning(); }

        /// <summary>Why the source has nothing (diagnostics): empty when it resolved.</summary>
        public string Why { get; private set; } = "not asked yet";
        public SpellSwing308 Swing => _swing;
        public Transform TipSocket => _tip;

        bool Resolve()
        {
            if (_swing != null && _rig != null) return true;
            if (Time.unscaledTime < _nextSearch) return false;
            _nextSearch = Time.unscaledTime + _tuning.RigSearchSeconds;
            _swing = UnityEngine.Object.FindFirstObjectByType<SpellSwing308>();
            if (_swing == null) { Why = "no SpellSwing308 in the scene"; return false; }
            _rig = _swing.GetComponent<WorldMacroPlayerGestureRig>();
            if (_rig == null) { Why = "no WorldMacroPlayerGestureRig beside SpellSwing308"; _swing = null; return false; }
            Why = "";
            return true;
        }

        GreatBrush308Profile Profile()
        {
            if (_swing != null && _swing.Profile != null) return _swing.Profile;
            if (_profile == null) _profile = Resources.Load<GreatBrush308Profile>(GreatBrush308Profile.ResourcesPath);
            return _profile;
        }

        public bool TrySample(out Vol2SwingSample sample)
        {
            sample = default;
            if (!Resolve()) return false;
            var profile = Profile();
            if (profile == null) { Why = "no GreatBrush308Profile"; return false; }
            sample.Serial = _swing.Swings;
            string state = _swing.State;
            int index = string.IsNullOrEmpty(state) ? -1 : profile.Find(state);
            if (index < 0) return true;
            var entry = profile.States[index];
            // only a motion that has a strike in it carries a window; a chained hold (GuardHold) or an idle is not a swing
            sample.Swinging = _swing.Active && _swing.Swinging && entry.ClipApexSeconds > 0f && !entry.Loop;
            sample.ClipSeconds = _swing.ClipSeconds; sample.ApexSeconds = entry.ClipApexSeconds; sample.LengthSeconds = entry.ClipSeconds;
            return true;
        }

        public bool TryPose(out Vector3 sleeve, out Vector3 tip)
        {
            sleeve = tip = default;
            if (!Resolve()) return false;
            Transform root = _rig.WorldBrushRoot308;
            if (root == null) { Why = "the world brush is not bound"; return false; }
            if (_root != root || _grip == null)
            {
                _root = root;
                _tip = Find(root, GreatBrushBristles308.TipName);
                _grip = Find(root, GreatBrushBristles308.GripName);
                _gripL = Find(root, GreatBrushBristles308.LeftGripName);
            }
            if (_grip == null) { Why = "the brush has no GripSocket"; return false; }
            Vector3 grip = _grip.position;
            tip = _tip != null ? _tip.position : grip + _grip.up * _tuning.FallbackBrushLength;     // TE-S9s-3
            Vector3 shaft = tip - grip;
            shaft = shaft.sqrMagnitude > 1e-6f ? shaft.normalized : _grip.up;
            Vector3 rear = _gripL != null ? _gripL.position : grip;
            sleeve = rear - shaft * _tuning.SleeveBackMeters;
            return true;
        }

        public bool Begin()
        {
            if (!Resolve()) return false;
            var profile = Profile();
            if (profile == null) return false;
            int index = profile.Find(profile.Resolve(_tuning.BareSwingState));
            if (index < 0) return false;
            var entry = profile.States[index];
            float apex = entry.ClipApexSeconds;
            // toApex = the whole wind-up (the clip is shown from its start); the lock only feeds the body's aim fall-off, the swing rule is not involved
            return _swing.PlayStrike308(_tuning.BareSwingState, apex, apex + _tuning.ActiveAfterApex);
        }

        static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++) { var f = Find(root.GetChild(i), name); if (f != null) return f; }
            return null;
        }
    }

    /// <summary>The 갈무리 port on the real combat wiring: the first enemy of a swing, or the air.</summary>
    public sealed class Vol2WiringGatherPort : IVol2SwingGatherPort
    {
        readonly CombatLoopWiring _wiring;
        public Vol2GatherRecord Last { get; private set; }
        public int Calls { get; private set; }
        public Vol2WiringGatherPort(CombatLoopWiring wiring) { _wiring = wiring; }

        public void FirstHit(IVol2Target target, SwingId swing)
        {
            var enemy = target as Vol2EnemyTarget;
            Calls++;
            if (_wiring == null) return;
            // a body that is not an enemy (a fixture's fake) cannot be gathered from: the swing counts as a swing that met nobody
            Last = enemy != null && enemy.Vitals != null ? _wiring.ReportVol2SwingFirstHit(enemy.Vitals, swing) : _wiring.ReportVol2SwingMissed(swing);
        }

        public void Missed(SwingId swing)
        {
            Calls++;
            if (_wiring != null) Last = _wiring.ReportVol2SwingMissed(swing);
        }
    }

    /// <summary>Ticks the driver once per rendered frame after the gesture rig (order 1000) has put the brush in the hand, and routes the click to it: while this
    /// component is enabled the legacy lock-on pull of HarvestAction is switched off (TE-S9s-5).</summary>
    [DefaultExecutionOrder(1100)]
    public sealed class Vol2SwingDriverHost : MonoBehaviour
    {
        Vol2SwingDriver _driver;
        HarvestAction _harvest;

        public Vol2SwingDriver Driver => _driver;

        public void Bind(Vol2SwingDriver driver, HarvestAction harvest)
        {
            Unhook();
            _driver = driver; _harvest = harvest;
            if (isActiveAndEnabled) Hook();
        }

        void OnEnable() { Hook(); }
        void OnDisable() { Unhook(); }
        void OnDestroy() { Unhook(); }

        void Hook()
        {
            if (_harvest == null || _driver == null) return;
            _harvest.Pressed -= OnPressed; _harvest.Pressed += OnPressed;
        }

        void Unhook()
        {
            if (_harvest == null) return;
            _harvest.Pressed -= OnPressed;
            _harvest.Intercepted = false;
        }

        void OnPressed()
        {
            // the legacy pull is switched off only while there is a rig to swing with: a scene without the great brush keeps its old click
            try { if (_harvest != null && _driver != null) _harvest.Intercepted = _driver.IsAvailable; _driver?.TryBeginSwing(); } catch (Exception e) { Debug.LogException(e, this); }
        }

        void LateUpdate()
        {
            try { _driver?.Update(); } catch (Exception e) { Debug.LogException(e, this); }
        }
    }

    public static class Vol2SwingBinder
    {
        /// <summary>Builds the scope's swing driver and starts it. Vol2 shape runtimes only (null in Legacy308 and for a null runtime). With a scene host (the game, the
        /// lab) the driver is ticked and gets the click; a fixture runtime has no host and drives <see cref="Vol2SwingDriver.Update"/> itself.</summary>
        public static Vol2SwingDriver Bind(Vol2ShapeRuntime shapes, CombatLoopWiring wiring)
        {
            if (shapes == null || shapes.Coordinator == null) return null;
            var tuning = new Vol2SwingTuning();
            var driver = new Vol2SwingDriver(shapes.Coordinator, tuning)
            {
                Source = new Vol2RigSwingSource(tuning),
                Gather = wiring != null ? new Vol2WiringGatherPort(wiring) : null
            };
            shapes.Swing = driver;
            if (shapes.Host != null)
            {
                var host = shapes.Host.AddComponent<Vol2SwingDriverHost>();
                host.Bind(driver, wiring != null ? wiring.Harvest : null);
            }
            return driver;
        }
    }
}
