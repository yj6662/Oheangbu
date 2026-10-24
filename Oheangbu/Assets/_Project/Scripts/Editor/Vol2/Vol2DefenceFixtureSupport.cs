using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>Shared pieces of the slice 2 edit-mode fixtures (Vol2DamageFixture, Vol2DefenceFixture). Everything is built in memory on
    /// objects the fixture creates and destroys (HideAndDontSave); no scene is opened or saved, no asset is written, no Play.</summary>
    [Serializable]
    public sealed class Vol2FixtureReport { public int pass, fail; public List<string> lines = new List<string>(); }

    public sealed class Vol2FakeContext : IVol2CombatContext
    {
        public bool Engaged { get; set; }
        public long EncounterId { get; set; }
        public long BossPhaseId { get; set; }
        public bool WeakWindowOpen { get; set; }
        public long WindowId { get; set; }
    }

    public sealed class Vol2FakeAttackElement : IEnemyAttackElement308
    {
        public Oheangbu.Core.Domain.Element Element;
        public bool TryAttackElement(out Oheangbu.Core.Domain.Element element) { element = Element; return true; }
    }

    /// <summary>One in-memory defence rig: the real rulebook, a hand-driven clock, the real adapter with the kit's payment gate, a real ink
    /// pool, a real player and a real ParryJudge carrying the kit's vol.2 resolver.</summary>
    public sealed class Vol2DefenceRig
    {
        public Vol2Rulebook Book;
        public ManualVol2Clock Clock;
        public Vol2IdIssuer Ids;
        public Vol2GlyphAdapter Adapter;
        public InkPool Ink;
        public PlayerVitals Player;
        public Vol2FakeContext Context;
        public Vol2DefenceKit Kit;
        public ParryJudge Judge;
        public int Accepted, Refused;
        public Vol2CastDecision Last;

        public Vol2DefenceRig(List<Object> made, Vol2Rulebook book, float hpFraction = 0.5f, float hpPerUnit = 10f, bool withPayment = true, Vol2SessionLedger session = null)
        {
            Book = book;
            Clock = new ManualVol2Clock(1000000);
            Ids = new Vol2IdIssuer(1);
            Adapter = new Vol2GlyphAdapter(book.Numbers, null, Clock, Ids);
            Adapter.Decided += d => { Last = d; if (d.Accepted) Accepted++; else Refused++; };
            Ink = new InkPool(null, null);
            var go = new GameObject("Vol2DefenceRig_Player") { hideFlags = HideFlags.HideAndDontSave };
            made.Add(go);
            Player = go.AddComponent<PlayerVitals>();
            Player.Restore(hpFraction);
            Context = new Vol2FakeContext();
            Kit = new Vol2DefenceKit(book.Numbers, Clock, Ink, Player, session, Context, hpPerUnit);
            if (withPayment) Adapter.Payment = Kit.TryPay;
            Judge = new ParryJudge(null) { Vol2Resolver = Kit.Parry };
        }

        public Vol2CastDecision Cast(char letter) => Adapter.Receive(letter);
        public void Advance(long us) => Clock.Advance(us);
        public decimal InkUnits => Kit.Ink.Units;
        public float PlayerHp => Player.Hp01 * Player.MaxHp;
    }

    public static class Vol2DefenceFixtureSupport
    {
        public static void Check(Vol2FixtureReport r, string id, bool ok, string detail)
        {
            if (ok) r.pass++; else r.fail++;
            r.lines.Add((ok ? "PASS " : "FAIL ") + id + " " + detail);
        }

        public static void SetField(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new MissingFieldException(target.GetType().Name, field);
            f.SetValue(target, value);
        }

        public static object Call(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new MissingMethodException(target.GetType().Name, method);
            return m.Invoke(target, args);
        }

        /// <summary>A living enemy (Restore gives LifeRevision 1 and full HP). maxHp 0 = the default 60.</summary>
        public static EnemyVitals NewEnemy(List<Object> made, string name, float maxHp = 60f, bool boss = false, float defence = 0f, Transform parent = null)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            if (parent != null) go.transform.SetParent(parent, false);
            made.Add(go);
            var v = go.AddComponent<EnemyVitals>();
            if (maxHp != 60f || boss || defence > 0f)
            {
                var profile = ScriptableObject.CreateInstance<EnemyVitalsProfileSO>();
                profile.hideFlags = HideFlags.HideAndDontSave; made.Add(profile);
                SetField(profile, "_maxHp", maxHp);
                SetField(profile, "_isBoss", boss);
                SetField(profile, "_defence", defence);
                SetField(v, "_profile", profile);
            }
            v.Restore();
            return v;
        }

        public static bool Near(double a, double b, double eps = 1e-3) => Math.Abs(a - b) <= eps;
        public static bool Near(decimal a, decimal b, decimal eps = 0.001m) => Math.Abs(a - b) <= eps;
    }
}
