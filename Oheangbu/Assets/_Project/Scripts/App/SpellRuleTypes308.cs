// PURE308
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // Shared value types of the pure rule files (<Name>Rule308.cs). A rule never touches a Unity object: the effect class
    // turns EnemyVitals into snapshots, calls the rule, and executes the orders it gets back (SPEC-SPELL-120-308 section 4).

    // One enemy as a rule sees it. Id = the index in ISpellCastHost.Targets at snapshot time.
    public readonly struct SpellActorSnap
    {
        public readonly int Id;
        public readonly Vector3 Position;
        public readonly bool Alive;
        public readonly uint Life;        // EnemyVitals.LifeRevision: an order for another life is dropped
        public readonly float Hp01;
        public readonly bool IsBoss;

        public SpellActorSnap(int id, Vector3 position, bool alive, uint life, float hp01, bool isBoss)
        { Id = id; Position = position; Alive = alive; Life = life; Hp01 = hp01; IsBoss = isBoss; }
    }

    // One judgement a rule asks for: who, when (the wiring's scaled clock), how much, which element.
    public readonly struct SpellHitOrder
    {
        public readonly int TargetId;
        public readonly float At;
        public readonly float Power;
        public readonly Element Element;

        public SpellHitOrder(int targetId, float at, float power, Element element)
        { TargetId = targetId; At = at; Power = power; Element = element; }
    }
}
