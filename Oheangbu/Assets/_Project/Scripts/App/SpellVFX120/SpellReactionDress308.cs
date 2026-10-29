using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // #308 D308-128 21 / D308-132 step 6: puts a reaction look (a built PF_V308z_react_* prefab instance) in place: where it
    // stands, which way it runs, how big it is and for how long. The presenter calls it on its new instance; the edit-mode checks call
    // it on a prefab loaded in an isolated scene, so what is measured is what is shown. Presentation only.
    //   - the root is turned so +Z is the way the wave ran (the steam flows along it);
    //   - scale: the radius all round, or (radius, 1, radius) for a look whose heights are in metres (Entry.KeepHeight); a look
    //     born at another radius than it ends at (the wildfire: x1.5) is grown by Vfx308ReactGrow over .4 s;
    //   - time: the body lives `life` minus the longest particle it can leave, so nothing is left once the rule's product is gone;
    //     the "Tongue" / "Fuel" layers run once over that time on the 0.4 run -> 0.3 burst -> monotone decay beat (RunBurstDecay);
    //   - a flat quad layer ("Wet", "Crack") whose look is sized in metres scales its quads by the radius itself.
    public static class SpellReactionDress308
    {
        public const float GrowSeconds = .4f, RunSeconds = .4f, BurstSeconds = .3f, MinBody = .3f, Lift = .02f;

        // The emission over a run of `life` seconds, as a share of the layer's rate: the fire runs along the branches for .4 s
        // (rising), bursts for .3 s (peak), then only dies down (never rises again).
        public static AnimationCurve RunBurstDecay(float life)
        {
            life = Mathf.Max(life, RunSeconds + BurstSeconds + .1f);
            float run = RunSeconds / life, burst = (RunSeconds + BurstSeconds) / life;
            return new AnimationCurve(new Keyframe(0f, .3f), new Keyframe(run, 1f), new Keyframe((run + burst) * .5f, 1.8f), new Keyframe(burst, 1.1f), new Keyframe(1f, .2f));
        }

        public static Vector3 ScaleFor(in SpellReactionLook308SO.Entry entry, Vector3 prefabScale, float radius)
        {
            radius = Mathf.Max(.05f, radius);
            return entry.KeepHeight ? new Vector3(prefabScale.x * radius, prefabScale.y, prefabScale.z * radius) : prefabScale * radius;
        }

        // Dresses `instance` (already created, any scene). Returns its Vfx308ZoneBody, null when the prefab has none.
        public static Vfx308ZoneBody Dress(GameObject instance, in SpellReactionLook308SO.Entry entry, Vector3 prefabScale, in SpellFxCueArgs args)
        {
            if (instance == null) return null;
            var body = instance.GetComponent<Vfx308ZoneBody>();
            if (body == null) return null;
            Vector3 flat = args.Direction; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            instance.transform.SetPositionAndRotation(args.Point + Vector3.up * Lift, Quaternion.LookRotation(flat.normalized, Vector3.up));

            float radius = Mathf.Max(.05f, args.Value), from = args.FromRadius > 0f ? Mathf.Min(args.FromRadius, radius) : radius;
            Vector3 to = ScaleFor(entry, prefabScale, radius), start = ScaleFor(entry, prefabScale, from);
            var grow = instance.GetComponent<Vfx308ReactGrow>();
            if (from < radius - 1e-4f)
            {
                if (grow == null) grow = instance.AddComponent<Vfx308ReactGrow>();
                grow.From = start; grow.To = to; grow.Seconds = GrowSeconds;
                instance.transform.localScale = start;
            }
            else
            {
                if (grow != null) { if (Application.isPlaying) Object.Destroy(grow); else Object.DestroyImmediate(grow); }
                instance.transform.localScale = to;
            }

            // a body ends when the rule's product does: what a still-emitting layer can leave behind after it stops is part of
            // the life (one-shot layers - the pour, the wet track, the old ring - are all gone inside it)
            float tail = 0f, left = Mathf.Max(MinBody, args.Life);
            foreach (var ps in body.Body) if (ps != null && ps.emission.rateOverTimeMultiplier > 0f) tail = Mathf.Max(tail, ps.main.startLifetime.constantMax);
            body.Delay = 0f;
            body.Life = Mathf.Max(MinBody, left - Mathf.Min(tail, left - MinBody));
            body.DryFade = Mathf.Max(.2f, left - body.Life + .1f);   // a dried body is gone at the product's end (its End layer plays inside this)
            body.LateAfter = 0f; body.LateBefore = 0f;   // monotone: the beat is in the curves, no late pale-out step

            foreach (var ps in body.Body)
            {
                if (ps == null) continue;
                var main = ps.main;
                string name = ps.name;
                if (name.StartsWith("Tongue", System.StringComparison.Ordinal) || name.StartsWith("Fuel", System.StringComparison.Ordinal))
                {
                    main.loop = false; main.duration = body.Life;
                    var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(em.rateOverTimeMultiplier, RunBurstDecay(body.Life));
                }
                else if (name == "Wisps" || name == "Ash")
                {
                    // the smoke and the ash burn down with the fire: the patch's catch, then a third (EmberPatchLook308.BurnRate)
                    main.loop = false; main.duration = body.Life;
                    var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(em.rateOverTimeMultiplier, EmberPatchLook308.BurnRate(body.Life));
                }
                else if (entry.KeepHeight && (name.StartsWith("Wet", System.StringComparison.Ordinal) || name.StartsWith("Crack", System.StringComparison.Ordinal)))
                {
                    if (main.startSize3D) { main.startSizeXMultiplier *= radius; main.startSizeYMultiplier *= radius; }
                    else main.startSizeMultiplier *= radius;
                }
            }
            return body;
        }
    }
}
