// PURE308
namespace Oheangbu.App
{
    // The caster's own heat (a self stack): how many casts are counted and when the last one was accepted.
    public struct VolleyHeat308
    {
        public int Stacks;          // 0 = cold
        public float LastCastAt;    // clock of the last counted cast
    }

    // "Heated barrel" (SPEC-SPELL-120-308 WP-02, handler volley.heat): every cast of this handler made inside the window
    // raises the power of its next cast by one step, up to a cap; once the window passes without a cast the heat is gone.
    // Only the handler's own casts count (Spec section 15 default): enemies, hits and other glyphs neither add nor reset.
    public static class VolleyHeatRule308
    {
        // The stacks that still count at "now": all of them inside the window (the edge included), none after it.
        public static int Stacks(in VolleyHeat308 heat, float now, float window)
        {
            return heat.Stacks > 0 && window > 0f && now - heat.LastCastAt <= window ? heat.Stacks : 0;
        }

        // Power multiplier of a cast made at "now": 1 when cold, one more step for every live stack.
        public static float Multiplier(in VolleyHeat308 heat, float now, float window, float step)
        {
            return 1f + (step > 0f ? step : 0f) * Stacks(heat, now, window);
        }

        // The heat after a cast was accepted at "now": one stack on top of the live ones, never above the cap.
        public static VolleyHeat308 AfterCast(in VolleyHeat308 heat, float now, float window, int max)
        {
            int next = Stacks(heat, now, window) + 1;
            if (next > max) next = max;
            if (next < 0) next = 0;
            return new VolleyHeat308 { Stacks = next, LastCastAt = now };
        }
    }
}
