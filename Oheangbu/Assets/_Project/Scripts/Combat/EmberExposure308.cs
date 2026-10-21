// PURE308

namespace Oheangbu.Combat
{
    // #308 D308-125 적용 5 (debate decision, TEST): what burning ground does to the one player standing on it, frame by frame.
    // Standing in a patch for Dwell seconds scorches the player for ScorchLife seconds (standing on renews only the time; the
    // tick and damage stay those it started with). Leaving every patch for more than Grace seconds starts the dwell over; a
    // shorter step out keeps it (the time outside does not count). Patch and scorch damage fall on one shared clock, Tick
    // seconds apart, running while the player is in a patch, scorched or inside the grace; the first tick is one Tick after the
    // clock starts and a late frame never pays out missed ticks. Warded (운's ring): the scorch is washed off, none attaches
    // and the dwell stays at zero; the patch itself still burns. Only the rule: patches, the ward and HP live engine-side
    // (CombatLoopWiring.Ember308). Advance is called every frame.
    public sealed class EmberExposure308
    {
        public readonly struct Numbers
        {
            public readonly float Tick, Damage, Dwell, Grace, ScorchLife, ScorchDamage;
            public Numbers(float tick, float damage, float dwell, float grace, float scorchLife, float scorchDamage)
            { Tick = tick; Damage = damage; Dwell = dwell; Grace = grace; ScorchLife = scorchLife; ScorchDamage = scorchDamage; }
            public bool Valid => Ok(Tick) && Tick > 0f && Ok(Damage) && Ok(Dwell) && Ok(Grace) && Ok(ScorchLife) && Ok(ScorchDamage);
            static bool Ok(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f;
        }

        public readonly struct Step
        {
            public readonly float PatchDamage, ScorchDamage;
            public readonly bool Started, Washed, Ended;
            public Step(float patch, float scorch, bool started, bool washed, bool ended)
            { PatchDamage = patch; ScorchDamage = scorch; Started = started; Washed = washed; Ended = ended; }
        }

        float _last = float.NaN, _dwell, _awaySince = float.NaN, _clock = float.NaN, _scorchUntil = float.NaN;
        float _grace, _tick, _scorchTick, _scorchDamage;
        bool _wasCounting;

        public float Dwell => _dwell;
        public float ScorchUntil => _scorchUntil;
        public bool Scorched(float now) => !float.IsNaN(_scorchUntil) && now < _scorchUntil;
        // The next tick of the shared clock; NaN = the clock is stopped.
        public float NextTick => _clock;

        // inside = the player's feet are in at least one live patch this frame; numbers = that patch's.
        public Step Advance(float now, bool inside, Numbers numbers, bool warded)
        {
            if (float.IsNaN(now) || float.IsInfinity(now)) return default;
            if (inside && !numbers.Valid) inside = false;
            float dt = float.IsNaN(_last) ? 0f : System.Math.Max(0f, now - _last);
            _last = now;
            if (inside) { _grace = numbers.Grace; _tick = numbers.Tick; }

            bool started = false, washed = false, ended = false;
            bool scorched = !float.IsNaN(_scorchUntil);
            if (scorched && now >= _scorchUntil) { _scorchUntil = float.NaN; scorched = false; ended = true; }
            if (scorched && warded) { _scorchUntil = float.NaN; scorched = false; washed = true; }

            bool counting = inside && !warded;
            if (warded) { _dwell = 0f; _awaySince = float.NaN; }
            else if (inside) { if (_wasCounting) _dwell += dt; _awaySince = float.NaN; }
            else if (_dwell > 0f)
            {
                if (float.IsNaN(_awaySince)) _awaySince = now;
                if (now - _awaySince > _grace) { _dwell = 0f; _awaySince = float.NaN; }
            }
            _wasCounting = counting;

            if (counting && _dwell >= numbers.Dwell && numbers.ScorchLife > 0f)
            {
                if (!scorched) { started = true; _scorchTick = numbers.Tick; _scorchDamage = numbers.ScorchDamage; }
                _scorchUntil = now + numbers.ScorchLife;
                scorched = true;
            }

            bool running = inside || scorched || _dwell > 0f;
            if (!running) { _clock = float.NaN; return new Step(0f, 0f, started, washed, ended); }
            float tick = inside ? numbers.Tick : scorched ? _scorchTick : _tick;
            if (float.IsNaN(_clock)) { _clock = now + tick; return new Step(0f, 0f, started, washed, ended); }
            if (now < _clock) return new Step(0f, 0f, started, washed, ended);
            _clock += tick;
            if (_clock <= now) _clock = now + tick;
            return new Step(inside ? numbers.Damage : 0f, scorched ? _scorchDamage : 0f, started, washed, ended);
        }

        // The lethal guard spared the player: the scorch goes out and the dwell starts over (a player still in the fire has
        // Dwell seconds to step out before it catches again). The clock keeps running.
        public bool EndScorch()
        {
            bool had = !float.IsNaN(_scorchUntil);
            _scorchUntil = float.NaN; _dwell = 0f; _awaySince = float.NaN;
            return had;
        }

        // Death, a rest, the wiring going away: nothing carries over.
        public void Clear()
        {
            _last = float.NaN; _dwell = 0f; _awaySince = float.NaN; _clock = float.NaN; _scorchUntil = float.NaN;
            _grace = 0f; _tick = 0f; _scorchTick = 0f; _scorchDamage = 0f; _wasCounting = false;
        }
    }
}
