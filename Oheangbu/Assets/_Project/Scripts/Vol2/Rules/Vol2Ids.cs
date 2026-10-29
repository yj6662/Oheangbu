using System;
using System.Collections.Generic;
namespace Oheangbu.Vol2.Rules
{
    // S5-b slice 1 (design s5b_design_v1 section 4): the identifiers of the vol.2 runtime. Plain long values wrapped in distinct
    // types so a CastId can never be passed where an AttackId is wanted. 0 is "none". Issued by one Vol2IdIssuer per combat scope
    // (an instance, never a static: Enter Play Mode keeps statics between runs).
    public readonly struct CastId : IEquatable<CastId>
    {
        public long Value { get; }
        public CastId(long value) { Value = value; }
        public bool IsValid => Value > 0;
        public bool Equals(CastId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CastId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => "C" + Value;
        public static bool operator ==(CastId a, CastId b) => a.Value == b.Value;
        public static bool operator !=(CastId a, CastId b) => a.Value != b.Value;
    }
    public readonly struct AttackId : IEquatable<AttackId>
    {
        public long Value { get; }
        public AttackId(long value) { Value = value; }
        public bool IsValid => Value > 0;
        public bool Equals(AttackId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AttackId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => "A" + Value;
        public static bool operator ==(AttackId a, AttackId b) => a.Value == b.Value;
        public static bool operator !=(AttackId a, AttackId b) => a.Value != b.Value;
    }
    public readonly struct SwingId : IEquatable<SwingId>
    {
        public long Value { get; }
        public SwingId(long value) { Value = value; }
        public bool IsValid => Value > 0;
        public bool Equals(SwingId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SwingId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => "S" + Value;
        public static bool operator ==(SwingId a, SwingId b) => a.Value == b.Value;
        public static bool operator !=(SwingId a, SwingId b) => a.Value != b.Value;
    }
    public readonly struct MaterialId : IEquatable<MaterialId>
    {
        public long Value { get; }
        public MaterialId(long value) { Value = value; }
        public bool IsValid => Value > 0;
        public bool Equals(MaterialId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is MaterialId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => "M" + Value;
        public static bool operator ==(MaterialId a, MaterialId b) => a.Value == b.Value;
        public static bool operator !=(MaterialId a, MaterialId b) => a.Value != b.Value;
    }

    /// <summary>Four independent counters (cast / attack / swing / material), each starting at 1. One instance per combat scope.
    /// A material id is its own number: it never reuses a pooled object id (design section 4).</summary>
    public sealed class Vol2IdIssuer
    {
        long _cast, _attack, _swing, _material;
        public int ScopeId { get; }
        public Vol2IdIssuer(int scopeId = 1) { ScopeId = scopeId; }
        public CastId NextCast() => new CastId(checked(++_cast));
        public AttackId NextAttack() => new AttackId(checked(++_attack));
        public SwingId NextSwing() => new SwingId(checked(++_swing));
        public MaterialId NextMaterial() => new MaterialId(checked(++_material));
        public long CastsIssued => _cast;
        public long AttacksIssued => _attack;
        public long SwingsIssued => _swing;
        public long MaterialsIssued => _material;
    }

    public enum CommandRuleKind { Cast = 1, Shape = 2, Body = 3, Damage = 4, Status = 5, Material = 6, Defence = 7, Resource = 8, Presentation = 9 }

    /// <summary>Scope + rule kind + event id + command ordinal (design section 4). Two commands born of the same event (a damage, a slow
    /// and a material) differ in ordinal, so they never erase each other as duplicates; replaying the SAME event with the same
    /// ordinal is the duplicate the <see cref="CommandLedger"/> refuses.</summary>
    public readonly struct CommandKey : IEquatable<CommandKey>
    {
        public int ScopeId { get; }
        public CommandRuleKind Rule { get; }
        public long EventId { get; }
        public int Ordinal { get; }
        public CommandKey(int scopeId, CommandRuleKind rule, long eventId, int ordinal) { ScopeId = scopeId; Rule = rule; EventId = eventId; Ordinal = ordinal; }
        public bool Equals(CommandKey other) => ScopeId == other.ScopeId && Rule == other.Rule && EventId == other.EventId && Ordinal == other.Ordinal;
        public override bool Equals(object obj) => obj is CommandKey other && Equals(other);
        public override int GetHashCode() { unchecked { int h = ScopeId; h = h * 397 ^ (int)Rule; h = h * 397 ^ EventId.GetHashCode(); return h * 397 ^ Ordinal; } }
        public override string ToString() => ScopeId + "/" + Rule + "/" + EventId + "/" + Ordinal;
        public static bool operator ==(CommandKey a, CommandKey b) => a.Equals(b);
        public static bool operator !=(CommandKey a, CommandKey b) => !a.Equals(b);
    }

    /// <summary>The set of commands already begun in a scope. TryBegin is true exactly once per key.</summary>
    public sealed class CommandLedger
    {
        readonly HashSet<CommandKey> _seen = new HashSet<CommandKey>();
        public int Count => _seen.Count;
        public bool Contains(CommandKey key) => _seen.Contains(key);
        public bool TryBegin(CommandKey key) => _seen.Add(key);
        public void Clear() => _seen.Clear();
    }
}
