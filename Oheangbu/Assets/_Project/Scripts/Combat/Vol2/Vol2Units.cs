using System;
using Oheangbu.Core.Domain;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.Combat
{
    // S5-b slice 2 (D308-149, design s5b_design_v1 sections 5 and 9): the unit boundary between the vol.2 rules and the old combat
    // objects. The rules speak in ink points (ink.capacity = 100), 작용량 (damage units, 무받침 ㅏ 화 = 1) and enums of their own;
    // the old objects speak in pool fractions, enemy HP and Core.Domain.Element. Every conversion goes through this file.

    /// <summary>Explicit Element / Body mapping (design section 4: no enum integer casting).</summary>
    public static class Vol2Mapping
    {
        public static Body ToBody(Element element)
        {
            switch (element)
            {
                case Element.Wood: return Body.Wood;
                case Element.Fire: return Body.Fire;
                case Element.Earth: return Body.Earth;
                case Element.Metal: return Body.Metal;
                case Element.Water: return Body.Water;
                default: throw new ArgumentOutOfRangeException(nameof(element));
            }
        }
        public static Element ToElement(Body body)
        {
            switch (body)
            {
                case Body.Wood: return Element.Wood;
                case Body.Fire: return Element.Fire;
                case Body.Earth: return Element.Earth;
                case Body.Metal: return Element.Metal;
                case Body.Water: return Element.Water;
                default: throw new ArgumentOutOfRangeException(nameof(body));
            }
        }
        public static Body? ToBody(Element? element) => element.HasValue ? ToBody(element.Value) : (Body?)null;
    }

    /// <summary>The numbers of the boundary. HpPerUnit is NOT grounded in any decision (design section 8 asks for the conversion
    /// to be fixed; none exists): it is a TEST placeholder, always passed explicitly, always written into the per-hit log.</summary>
    public static class Vol2Units
    {
        /// <summary>ink.capacity (E-1): 100 ink points are one full base pool.</summary>
        public const decimal InkCapacity = 100m;
        /// <summary>TEST placeholder: enemy HP per damage unit (a plain 무받침 ㅏ 화 hit = 1 unit = 10 HP, about one old ranged hit). Undecided.</summary>
        public const float DefaultHpPerUnit = 10f;
        /// <summary>HP floor of the non-lethal swing, in real HP (DefenceNumbersV3Dto.HpFloor is 1, in the rules' own unit).</summary>
        public const float FloorHp = 1f;

        /// <summary>The rules compare (Hp - HpFloor) with a damage in the SAME unit (NoInkSwingV3). Real HP is shifted so that the
        /// floor stays exactly one real HP: ruleHp = (hp - floorHp) / hpPerUnit + HpFloor(units).</summary>
        public static decimal RuleHp(float hp, float hpPerUnit, decimal floorUnits)
        {
            if (hpPerUnit <= 0f || float.IsNaN(hpPerUnit) || float.IsInfinity(hpPerUnit)) throw new ArgumentOutOfRangeException(nameof(hpPerUnit));
            if (float.IsNaN(hp) || hp <= 0f) return 0m;
            return (decimal)((hp - FloorHp) / hpPerUnit) + floorUnits;
        }
    }

    /// <summary>The player's ink in ink points. Pool fractions are 0..1 of TotalCapacity; a spend of x points is x/100 of the BASE pool
    /// (InkPool.TrySpend divides by TotalCapacity itself), so points = Value x TotalCapacity x 100. Null pool = no ink accounting
    /// (a bare wiring in an edit-mode check): everything is free and nothing is gained.</summary>
    public sealed class Vol2InkAccount
    {
        readonly InkPool _pool;
        public Vol2InkAccount(InkPool pool) { _pool = pool; }
        public bool Available => _pool != null;
        public decimal Units => _pool == null ? 0m : Math.Round((decimal)_pool.Value * (decimal)_pool.TotalCapacity * Vol2Units.InkCapacity, 3);
        public decimal CapacityUnits => _pool == null ? 0m : Math.Round((decimal)_pool.TotalCapacity * Vol2Units.InkCapacity, 3);
        static float Fraction(decimal points) => (float)(points / Vol2Units.InkCapacity);

        /// <summary>All-or-nothing spend. A free account (no pool) pays anything.</summary>
        public bool TrySpend(decimal points)
        {
            if (points < 0m) throw new ArgumentOutOfRangeException(nameof(points));
            if (_pool == null || points == 0m) return true;
            return _pool.TrySpend(Fraction(points));
        }
        /// <summary>Takes what is there, up to points; returns what was actually taken (a debt that finds an empty pool takes 0).</summary>
        public decimal SpendClamped(decimal points)
        {
            if (points < 0m) throw new ArgumentOutOfRangeException(nameof(points));
            if (_pool == null || points == 0m) return 0m;
            decimal before = Units;
            _pool.SpendClamped(Fraction(points));
            return Math.Max(0m, before - Units);
        }
        /// <summary>Income through InkPool.Gain (HUD feedback included); returns the points actually received (a full pool receives 0).</summary>
        public decimal Gain(decimal points)
        {
            if (points < 0m) throw new ArgumentOutOfRangeException(nameof(points));
            if (_pool == null || points == 0m) return 0m;
            decimal before = Units;
            _pool.Gain(Fraction(points));
            return Math.Max(0m, Units - before);
        }
        /// <summary>Lab / reset only: refill to the given share of the pool.</summary>
        public void Refill(float fraction01 = 1f) { if (_pool != null) _pool.Restore(fraction01); }
    }
}
