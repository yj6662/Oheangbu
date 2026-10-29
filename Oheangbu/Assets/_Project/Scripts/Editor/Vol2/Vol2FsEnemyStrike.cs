using System;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEditor;
using UnityEngine;
using Element = Oheangbu.Core.Domain.Element;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-150 V12 free showcase batch 2 (LAB-ONLY, editor capture): a SCRIPTED enemy blow on the ㅓ guard, so the parry reaction is shown from a real verdict.
    /// While armed (free:strike:on, Play only), every ㅓ cast that opens its parry window gets one melee blow from the capture's nearest dummy bear: the bear's own
    /// authored Attack clip (EnemyRigMotion298.Attack, sampled so its contact phase AttackPeak01 lands on the blow) and, at window start + 0.22 s, the blow itself
    /// sent through the REAL defence path - the attack's telegraph cue is recorded on the vol.2 defence kit (as CombatLoopWiring.OnVol2Telegraph does for an
    /// enemy controller; 0.9 s, melee arc) and the wiring's ParryJudge.ResolveImpact is asked, which asks the vol.2 parry rules (Vol2ParryService / ParryRulesV3).
    /// A Success is relayed by the wiring (ParryResolved) and the scope (Vol2VfxModel.Parried) exactly as in the game. The element is the one the cast's body
    /// overcomes (a 정답 parry). No damage is dealt to anyone (the blow is parried; the player's vitals are never called). The enemy's brain stays off (the capture
    /// switches it off), so the strike's timing is scripted - this is the only lab stand-in. Disarmed when Play ends.</summary>
    public static class Vol2FsEnemyStrike
    {
        sealed class Strike { public Vol2CastLog Log; public EnemyVitals Vitals; public Oheangbu.App.World.EnemyRigMotion298 Rig; public long AtUs; public Element Element; public bool Done; }

        static bool _armed; static Vol2CastCoordinator _hooked; static Strike _strike;
        static int _strikes, _success, _correct; static string _last = "";
        public const double LeadSeconds = 0.22;

        public static string Arm(bool on)
        {
            if (on && !EditorApplication.isPlaying) return "REFUSED free:strike: not in Play";
            if (on == _armed) return Status();
            _armed = on;
            if (on) { EditorApplication.update += Tick; EditorApplication.playModeStateChanged += OnPlayMode; _strikes = 0; _success = 0; _correct = 0; _last = ""; }
            else Disarm();
            return Status();
        }

        public static string Status() => "strike armed=" + _armed + " strikes=" + _strikes + " success=" + _success + " correct(posture>0)=" + _correct + (_last != "" ? " last=" + _last : "");

        static void OnPlayMode(PlayModeStateChange s) { if (s == PlayModeStateChange.ExitingPlayMode) Disarm(); }

        static void Disarm()
        {
            _armed = false;
            EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= OnPlayMode;
            if (_hooked != null) _hooked.CastBegan -= OnBegan;
            _hooked = null; _strike = null;
        }

        static Element Overcome(Body b)
        {
            switch (b)
            {
                case Body.Wood: return Element.Earth;     // 목 -> 토
                case Body.Fire: return Element.Metal;     // 화 -> 금
                case Body.Earth: return Element.Water;    // 토 -> 수
                case Body.Metal: return Element.Wood;     // 금 -> 목
                default: return Element.Fire;             // 수 -> 화
            }
        }

        static void Tick()
        {
            if (!_armed) return;
            try
            {
                if (!EditorApplication.isPlaying) { Disarm(); return; }
                var p = Object.FindFirstObjectByType<Vol2VfxPresenter>();
                var coord = p != null ? p.Coordinator : null;
                if (coord != _hooked)
                {
                    if (_hooked != null) _hooked.CastBegan -= OnBegan;
                    _hooked = coord;
                    if (_hooked != null) _hooked.CastBegan += OnBegan;
                }
                if (_strike == null || _hooked == null) return;
                var s = _strike;
                if (s.Vitals == null || s.Log.Status == "Aborted") { _strike = null; return; }
                long now = _hooked.Services.Clock.NowUs;
                // the bear's own attack motion, its contact phase on the blow
                if (s.Rig != null && s.Rig.Attack != null && s.Rig.Animator != null)
                {
                    float len = s.Rig.Attack.length;
                    float at = s.Rig.AttackPeak01 * len + (float)((now - s.AtUs) / 1000000.0);
                    if (at <= len + 0.05f) s.Rig.Attack.SampleAnimation(s.Rig.Animator.gameObject, Mathf.Clamp(at, 0f, len));
                }
                if (!s.Done && now >= s.AtUs) { s.Done = true; Hit(s); }
                if (s.Done && now > s.AtUs + 2000000) _strike = null;
            }
            catch (Exception e) { _last = "EXCEPTION " + e.GetType().Name + " " + e.Message; }
        }

        static void OnBegan(Vol2CastLog log)
        {
            if (!_armed || log == null || log.Glyph.Frame != Frame.Attachment || !log.ParryWindowOpened || log.Spec == null) return;
            EnemyVitals best = null; float bd = 4f;
            foreach (var v in Object.FindObjectsByType<EnemyVitals>(FindObjectsSortMode.None))
            {
                if (v == null || !v.isActiveAndEnabled || !v.name.StartsWith("Vol2VfxDummy", StringComparison.Ordinal)) continue;
                Vector3 d = v.transform.position - log.Spec.Origin; d.y = 0f;
                if (d.magnitude < bd) { bd = d.magnitude; best = v; }
            }
            if (best == null) { _last = "no dummy within 4 m for " + log.Glyph.Letter; return; }
            var rig = best.GetComponentInChildren<Oheangbu.App.World.EnemyRigMotion298>(true) ?? best.GetComponentInParent<Oheangbu.App.World.EnemyRigMotion298>(true);
            _strike = new Strike { Log = log, Vitals = best, Rig = rig, AtUs = log.WindowStartUs + (long)(LeadSeconds * 1000000.0), Element = Overcome(log.Glyph.Body) };
            _last = log.Glyph.Letter + ": blow scheduled at window +" + LeadSeconds.ToString("0.00") + " s (" + _strike.Element + ", rig " + (rig != null && rig.Attack != null ? rig.Attack.name : "none") + ")";
        }

        static void Hit(Strike s)
        {
            var wiring = Object.FindFirstObjectByType<CombatLoopWiring>();
            var judge = wiring != null ? typeof(CombatLoopWiring).GetField("_judge", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(wiring) as ParryJudge : null;
            var kit = wiring != null ? wiring.Vol2Defence : null;
            if (judge == null || kit == null) { _last = "no judge / defence kit (judge=" + (judge != null) + " kit=" + (kit != null) + ")"; return; }
            var attack = AttackProvenance.CreateMelee(s.Vitals, DamageSource.Enemy, s.Element);
            Vector3 origin = s.Log.Spec.Origin;
            kit.Telegraphs.Record(new EnemyAttackCue(attack, EnemyAttackDelivery.MeleeArc, origin, 0.9f, 1f));
            // the impact meets the guard in front of the caster, toward the bear, at chest height (as the capture's lab-simulated parry placed it)
            Vector3 to = s.Vitals.transform.position - origin; to.y = 0f;
            Vector3 point = origin + Vector3.up * 1.15f + (to.sqrMagnitude > 1e-4f ? to.normalized : s.Log.Spec.Forward) * 0.55f;
            var outcome = judge.ResolveImpact(s.Element, Time.time, point, attack);
            _strikes++; if (outcome == ParryOutcome.Success) _success++; if (kit.Parry.Last.Posture > 0m) _correct++;
            _last = s.Log.Glyph.Letter + ": " + s.Element + " blow -> " + outcome + " (vol.2 parry " + (kit.Parry.Last.Kind ?? "") + " " + (kit.Parry.Last.Reason ?? "") + " posture=" + kit.Parry.Last.Posture + ")";      // posture 40 = 정답 parry
        }
    }
}
