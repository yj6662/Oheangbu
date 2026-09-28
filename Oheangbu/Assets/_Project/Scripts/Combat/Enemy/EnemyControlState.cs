using System;
using System.Collections.Generic;

namespace Oheangbu.Combat
{
    // Timed control is independent of HP, stagger and weak-point damage bonuses.
    public sealed class EnemyControlState
    {
        struct Effect { public float Until, Speed; public bool BlocksActions; }
        readonly Dictionary<long,Effect> effects=new Dictionary<long,Effect>();
        readonly List<long> expired=new List<long>();
        public void Apply(long owner, float until, float speed, bool blocksActions)
        {
            if(owner<=0||!float.IsFinite(until)||!float.IsFinite(speed)||speed<0||speed>1)throw new ArgumentOutOfRangeException();
            effects[owner]=new Effect{Until=until,Speed=speed,BlocksActions=blocksActions};
        }
        void Expire(float now)
        {
            expired.Clear();foreach(var pair in effects)if(pair.Value.Until<=now)expired.Add(pair.Key);
            foreach(long id in expired)effects.Remove(id);
        }
        public float MovementScale(float now)
        {Expire(now);float scale=1;foreach(var effect in effects.Values)scale=Math.Min(scale,effect.Speed);return scale;}
        public bool BlocksActions(float now)
        {Expire(now);foreach(var effect in effects.Values)if(effect.BlocksActions)return true;return false;}
        public void Remove(long owner)=>effects.Remove(owner);
        public void Clear()=>effects.Clear();
    }
}
