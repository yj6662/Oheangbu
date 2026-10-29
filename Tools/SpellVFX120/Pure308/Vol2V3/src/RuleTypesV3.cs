using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Oheangbu.Vol2.Rules
{
    public enum Body { Wood, Fire, Earth, Metal, Water }
    public enum Frame { Path, Attachment, Fan, Ring }
    public enum Verb { Once, Leave, Spread, Seed, Split, Return }
    public enum SpellKind { Bare, Normal, Peak }
    public enum EffectKind { Instant, Second, Residue, Spread, ReturnSpread, Activation, Strip, Rear, Return }
    public enum ContactKind { Touch, Plant, Activate, Return, Shared, Transfer, Reflect }
    public enum TriggerKind { None, OwnerAttack, DamageReceived, EnterFan, CrossRing, Parry, Dodge }

    public readonly struct Glyph
    {
        public char Letter { get; }
        public Body Body { get; }
        public Frame Frame { get; }
        public Verb Verb { get; }
        public SpellKind Kind => Verb == Verb.Once ? SpellKind.Bare : (int)Body + 1 == (int)Verb ? SpellKind.Peak : SpellKind.Normal;
        public bool IsParry => Frame == Frame.Attachment && Verb == Verb.Once;
        public Glyph(char letter, Body body, Frame frame, Verb verb) { Letter = letter; Body = body; Frame = frame; Verb = verb; }
    }

    public sealed class EffectTerm
    {
        public EffectKind Kind { get; }
        public decimal Amount { get; }
        public int Count { get; }
        public decimal Total => Amount * Count;
        public bool Shared => Kind == EffectKind.Residue || Kind == EffectKind.Spread || Kind == EffectKind.ReturnSpread || Kind == EffectKind.Strip;
        public string Name => Kind switch { EffectKind.Instant => "즉시", EffectKind.Second => "둘째 접촉", EffectKind.Residue => "재료", EffectKind.Spread => "번짐", EffectKind.ReturnSpread => "되번짐", EffectKind.Activation => "작동", EffectKind.Strip => "벗김", EffectKind.Rear => "뒤쪽 가름", _ => "귀환" };
        public EffectTerm(EffectKind kind, decimal amount, int count = 1) { Kind = kind; Amount = amount; Count = count; }
    }

    public sealed class Contact
    {
        public long EventId { get; }
        public long CastId { get; }
        public long TargetId { get; }
        public long LifeRevision { get; }
        public long MaterialId { get; }
        public long SwingId { get; }
        public long NowUs { get; }
        public ContactKind Kind { get; }
        public TriggerKind Trigger { get; }
        public EffectKind Effect { get; }
        public decimal Amount { get; }
        public Contact(long eventId, long castId, long targetId, long nowUs, long swingId = 0,
            ContactKind kind = ContactKind.Touch, EffectKind effect = EffectKind.Residue, decimal amount = 0,
            TriggerKind trigger = TriggerKind.None, long lifeRevision = 0, long materialId = 0)
        { EventId = eventId; CastId = castId; TargetId = targetId; NowUs = nowUs; SwingId = swingId;
          Kind = kind; Effect = effect; Amount = amount; Trigger = trigger; LifeRevision = lifeRevision; MaterialId = materialId; }
    }

    public sealed class ContactProfile
    {
        public int Targets { get; }
        public int Contacts { get; }
        public bool Returned { get; }
        public bool Shared { get; }
        public bool Activated { get; }
        public ContactProfile(int targets = 1, int contacts = 2, bool returned = true, bool shared = true, bool activated = true)
        { Targets = targets; Contacts = contacts; Returned = returned; Shared = shared; Activated = activated; }
    }

    public static class RuleTypesV3
    {
        public static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values) => new List<T>(values).AsReadOnly();
        public static long Microseconds(decimal seconds) => checked((long)(seconds * 1000000m));
        public static string FrameKey(Frame f) => f switch { Frame.Path => "a", Frame.Attachment => "eo", Frame.Fan => "o", _ => "u" };
        public static string VerbKey(Verb v) => v switch { Verb.Once => "none", Verb.Leave => "g", Verb.Spread => "n", Verb.Seed => "m", Verb.Split => "s", _ => "ng" };
        public static string BodyKey(Body b) => b.ToString().ToLowerInvariant();
    }
}
