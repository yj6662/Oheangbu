using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-DEPLOY-308 section 7 (strengthened by D308-10c): the struck enemy's hit reaction. The impact pass draws the
    // target's renderers AGAIN with a flat material; the enemy's own shader, materials and property blocks are never read for
    // writing or changed (so the riposte's property-block flash is unaffected). Dark bodies take the light grey, light bodies
    // take ink. Not emission. Presentation only: nothing is rewarded, no status is applied, no judgement reads it.
    // One confirmed hit plays four pieces (their timetable and rules are HitReaction308, pure):
    //   pop     the whole body in the flat value for Flicker.PopSeconds (one cel) - it reads on small and far enemies
    //   flicks  the remaining flat-value flicks (Count at Hz, OnSeconds each)
    //   stain   after the pop a flat-value blot stays round the hit point and shrinks from its rim (one-way: it never grows)
    //   knock   the silhouette drawn once more, pushed sideways and back: a thin band outside the body, once
    // Photosensitivity: a target covering a quarter of the screen or more flickers slower, each of its full-body flicks spends
    // a full-screen flip token, and its period is never shorter than the limiter's minimum interval. The stain and the knock
    // are small-area, one-way changes and spend nothing.
    public sealed class HitFlicker308 : MonoBehaviour
    {
        public const int MaxTargets = 6, MaxPerTarget = 32;
        private const float KnockDepthBias = -.02f;   // the pushed copy is hidden wherever the scene is not clearly behind it
        private static readonly int FlatColorId = Shader.PropertyToID("_FlatColor");
        private static readonly int SrgbId = Shader.PropertyToID("_SrgbTarget");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int DepthBiasId = Shader.PropertyToID("_DepthBias");
        private static readonly int KnockId = Shader.PropertyToID("_Knock");
        private static readonly int StainOnId = Shader.PropertyToID("_StainOn");
        private static readonly int StainPointId = Shader.PropertyToID("_StainPoint");
        private static readonly int StainId = Shader.PropertyToID("_Stain");
        private static readonly int StainSeedId = Shader.PropertyToID("_StainSeed");

        private struct Slot
        {
            public Object Target; public int Count, LastFlick; public double Start; public HitReactionPlan308 Plan;
            public bool Light, FlickGranted;
            public float Share;   // viewport share of this target when it was hit (small targets flickering together are summed)
        }

        private SpellDeploy308ProfileSO _profile;
        private SpellDeploy308ProfileSO.TierSet _tier;
        private Material _light, _dark;
        private readonly Material[] _stain = new Material[MaxTargets], _knock = new Material[MaxTargets];
        private Color _lightColor, _darkColor;
        private readonly Slot[] _slots = new Slot[MaxTargets];
        private readonly Renderer[] _renderers = new Renderer[MaxTargets * MaxPerTarget];
        private readonly int[] _subMeshes = new int[MaxTargets * MaxPerTarget];
        private readonly List<Renderer> _scratch = new List<Renderer>(64);
        private readonly List<Material> _materialScratch = new List<Material>(8);
        private readonly Vector3[] _corners = new Vector3[8];
        private double _until = double.NegativeInfinity;

        public int Notified { get; private set; }
        /// <summary>Hits that did not restart a small target's running flicks (photosensitivity).</summary>
        public int Held { get; private set; }
        public int Scheduled { get; private set; }
        public int DrawnFlicks { get; private set; }
        public int LastCount { get; private set; }
        public float LastHz { get; private set; }
        public float LastShare { get; private set; }
        public float LastPopSeconds { get; private set; }
        public float LastPeriod { get; private set; }
        public float LastStainSeconds { get; private set; }
        public float LastStainRadius { get; private set; }
        public float LastKnockSeconds { get; private set; }
        public float LastKnockMeters { get; private set; }
        /// <summary>The stain cover written on the last Fill (1 = the full radius, falling to 0): it only ever falls.</summary>
        public float LastStainCover { get; private set; }

        public void Configure(SpellDeploy308ProfileSO profile, DeployTier308 tier)
        {
            _profile = profile;
            if (profile == null) return;
            _tier = profile.Tier(tier);
            if (profile.FlatShader == null) return;
            if (_light == null)
            {
                _light = new Material(profile.FlatShader) { name = "InkFlat308 light", hideFlags = HideFlags.DontSave };
                _dark = new Material(profile.FlatShader) { name = "InkFlat308 dark", hideFlags = HideFlags.DontSave };
                for (int i = 0; i < MaxTargets; i++)
                {
                    _stain[i] = new Material(profile.FlatShader) { name = "InkFlat308 stain " + i, hideFlags = HideFlags.DontSave };
                    _knock[i] = new Material(profile.FlatShader) { name = "InkFlat308 knock " + i, hideFlags = HideFlags.DontSave };
                }
            }
            float l = Mathf.Min(profile.Flicker.LightValue, SpellDeploy308ProfileSO.InkCeiling), d = profile.Flicker.DarkValue;
            _lightColor = new Color(l, l * .985f, l * .95f, 1f); _darkColor = new Color(d, d * .93f, d * .86f, 1f);
            _light.SetColor(FlatColorId, _lightColor);
            _dark.SetColor(FlatColorId, _darkColor);
            float srgb = profile.Impact.TargetIsSrgbEncoded ? 1f : 0f;
            _light.SetFloat(SrgbId, srgb); _dark.SetFloat(SrgbId, srgb);
            for (int i = 0; i < MaxTargets; i++)
            {
                _stain[i].SetFloat(SrgbId, srgb); _stain[i].SetFloat(StainOnId, 1f);
                _knock[i].SetFloat(SrgbId, srgb); _knock[i].SetFloat(DepthBiasId, KnockDepthBias);
            }
        }

        public bool AnyActive(double now) => now <= _until;

        /// <summary>How many flat-value flicks (the pop is the first) a hit gets under the user's flash setting.</summary>
        public int CountFor(DeployFlash308 flash, bool big) =>
            _profile == null ? 0 : HitReaction308.CountFor(_profile.Flicker, _tier != null ? _tier.FlickerMax : _profile.Flicker.Count, flash, big);

        public bool Accepts(DamageSource source)
        {
            if (_profile == null) return false;
            var f = _profile.Flicker;
            return source == DamageSource.PlayerDirect || (f.IncludeSummon && source == DamageSource.Summon) || (f.IncludePersistent && source == DamageSource.PersistentSpell);
        }

        /// <summary>A confirmed spell hit on a living target. Returns the number of flicks scheduled.</summary>
        public int Notify(Component target, DamageSource source, Camera camera, DeployFlash308 flash, double now) =>
            Notify(target, source, camera, flash, now, false, default);

        /// <summary>A confirmed spell hit with the place it landed (the stain stays round it). Returns the number of flicks scheduled.</summary>
        public int Notify(Component target, DamageSource source, Camera camera, DeployFlash308 flash, double now, bool hasPoint, Vector3 hitPoint)
        {
            Notified++;
            if (target == null || _light == null || !Accepts(source)) return 0;
            int slot = -1;
            for (int i = 0; i < MaxTargets; i++) if (_slots[i].Target == target) { slot = i; break; }
            // photosensitivity: a small target whose flicks are still running is not restarted by another hit - a volley lands
            // every .1 s, and restarting would turn the data rate (Flicker.Hz) into the hit rate. A big target's flicks are held
            // by the flip limiter instead.
            if (slot >= 0 && !_slots[slot].Plan.Big && now < _slots[slot].Start + _slots[slot].Plan.Flicks * (double)_slots[slot].Plan.Period) { Held++; return 0; }
            if (slot < 0) for (int i = 0; i < MaxTargets; i++) if (_slots[i].Target == null || now > _slots[i].Start + _slots[i].Plan.End) { slot = i; break; }
            if (slot < 0) return 0;

            target.GetComponentsInChildren(false, _scratch);
            int first = slot * MaxPerTarget, count = 0;
            int cap = Mathf.Min(MaxPerTarget, _profile.Flicker.MaxRenderers);
            var bounds = new Bounds(); bool any = false; float luma = 0f; int lumaSamples = 0;
            for (int i = 0; i < _scratch.Count && count < cap; i++)
            {
                var r = _scratch[i];
                if (r == null || !r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                r.GetSharedMaterials(_materialScratch);
                if (_materialScratch.Count == 0) continue;
                _renderers[first + count] = r; _subMeshes[first + count] = _materialScratch.Count; count++;
                if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
                var m = _materialScratch[0];
                if (m != null)
                {
                    // read only: how dark the body is decides which flat value reads against it
                    Color c = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.gray;
                    luma += c.r * .2126f + c.g * .7152f + c.b * .0722f; lumaSamples++;
                }
            }
            _scratch.Clear(); _materialScratch.Clear();
            for (int i = count; i < MaxPerTarget; i++) _renderers[first + i] = null;
            if (count == 0) { _slots[slot] = default; return 0; }

            var f = _profile.Flicker;
            float share = any && camera != null ? ScreenShare(camera, bounds) : 0f;
            // photosensitivity: small targets that flicker at the same time are one flickering area (an area cast hits several
            // enemies on one frame). The target that takes their total to BigShare or more is treated as a big one: fewer flicks,
            // the limiter's period, a flip token each - so the area flickering at Flicker.Hz stays under BigShare.
            float together = share;
            for (int i = 0; i < MaxTargets; i++)
                if (i != slot && _slots[i].Target != null && !_slots[i].Plan.Big && now < _slots[i].Start + _slots[i].Plan.Flicks * (double)_slots[i].Plan.Period) together += _slots[i].Share;
            bool big = together >= f.BigShare;
            var plan = HitReaction308.Plan(f, _profile.Impact.MinInterval, _tier != null ? _tier.FlickerMax : f.Count, flash, big, bounds.size.y);
            LastCount = plan.Flicks; LastHz = big ? f.BigHz : f.Hz; LastShare = share;
            LastPopSeconds = plan.Pop; LastPeriod = plan.Period; LastStainSeconds = plan.Stain; LastStainRadius = plan.StainRadius; LastKnockSeconds = plan.Knock; LastKnockMeters = plan.KnockMeters;
            LastStainCover = plan.Stain > 0f ? 1f : 0f;
            if (plan.Flicks <= 0) { _slots[slot] = default; return 0; }

            bool light = lumaSamples == 0 || luma / lumaSamples < .45f;
            Color value = light ? _lightColor : _darkColor;
            if (plan.Stain > 0f)
            {
                Vector3 point = hasPoint ? hitPoint : bounds.center;
                _stain[slot].SetColor(FlatColorId, value);
                _stain[slot].SetVector(StainPointId, new Vector4(point.x, point.y, point.z, plan.StainRadius));
                _stain[slot].SetFloat(StainSeedId, (Notified & 255) * .37f);
                _stain[slot].SetFloat(StainId, 1f);
            }
            if (plan.Knock > 0f)
            {
                // sideways (alternating), a little up and back: the band shows outside the body on one side, once
                Vector3 away = camera != null ? bounds.center - camera.transform.position : Vector3.forward;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
                Vector3 right = camera != null ? camera.transform.right : Vector3.right;
                Vector3 push = (right * ((Notified & 1) == 0 ? .8f : -.8f) + Vector3.up * .25f + away * .55f).normalized * plan.KnockMeters;
                _knock[slot].SetColor(FlatColorId, value);
                _knock[slot].SetVector(KnockId, new Vector4(push.x, push.y, push.z, 0f));
            }
            _slots[slot] = new Slot { Target = target, Count = count, LastFlick = -1, Start = now, Plan = plan, Light = light, Share = share };
            _until = System.Math.Max(_until, now + plan.End);
            Scheduled += plan.Flicks;
            return plan.Flicks;
        }

        /// <summary>Share of the viewport covered by the bounds' screen rectangle.</summary>
        public float ScreenShare(Camera camera, Bounds bounds)
        {
            Vector3 c = bounds.center, e = bounds.extents;
            int k = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                _corners[k++] = camera.WorldToViewportPoint(c + new Vector3(e.x * x, e.y * y, e.z * z));
            float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f; bool front = false;
            for (int i = 0; i < 8; i++)
            {
                if (_corners[i].z <= 0f) continue;
                front = true;
                minX = Mathf.Min(minX, _corners[i].x); maxX = Mathf.Max(maxX, _corners[i].x);
                minY = Mathf.Min(minY, _corners[i].y); maxY = Mathf.Max(maxY, _corners[i].y);
            }
            if (!front) return 0f;
            return Mathf.Max(0f, Mathf.Clamp01(maxX) - Mathf.Clamp01(minX)) * Mathf.Max(0f, Mathf.Clamp01(maxY) - Mathf.Clamp01(minY));
        }

        /// <summary>Called by the impact director for the base camera's render: the renderers that are "on" right now.</summary>
        public void Fill(IImpactPass308 pass, double now, ImpactFrameDirector308 director)
        {
            for (int i = 0; i < MaxTargets; i++)
            {
                ref Slot slot = ref _slots[i];
                if (slot.Target == null || slot.Plan.Flicks <= 0) continue;
                double t = now - slot.Start;
                if (t < 0.0) continue;
                if (t >= slot.Plan.End) { slot.Target = null; continue; }
                int first = i * MaxPerTarget;

                // the flat-value flicks; the first one is the pop
                bool whole = false;
                if (HitReaction308.FlickOn(slot.Plan, t, out int flick))
                {
                    if (flick != slot.LastFlick)
                    {
                        slot.LastFlick = flick;
                        // a big target's flick is a screen-sized value change: it needs a flip token like an impact frame
                        slot.FlickGranted = !slot.Plan.Big || director == null || director.SpendFlipToken();
                        if (slot.FlickGranted) DrawnFlicks++;
                    }
                    if (slot.FlickGranted)
                    {
                        whole = true;
                        var flat = slot.Light ? _light : _dark;
                        for (int r = 0; r < slot.Count; r++) pass.AddFlicker(_renderers[first + r], flat, _subMeshes[first + r]);
                    }
                }
                // the stain: once the pop is over, round the hit point, shrinking (not drawn under a whole-body flick)
                if (!whole && HitReaction308.StainOn(slot.Plan, t, out float cover))
                {
                    LastStainCover = cover;
                    _stain[i].SetFloat(StainId, cover);
                    for (int r = 0; r < slot.Count; r++) pass.AddFlicker(_renderers[first + r], _stain[i], _subMeshes[first + r]);
                }
                // the knock: the pushed silhouette, from the hit for Knock seconds
                if (HitReaction308.KnockOn(slot.Plan, t))
                    for (int r = 0; r < slot.Count; r++) pass.AddFlicker(_renderers[first + r], _knock[i], _subMeshes[first + r]);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < MaxTargets; i++) _slots[i] = default;
            for (int i = 0; i < _renderers.Length; i++) _renderers[i] = null;
            _until = double.NegativeInfinity;
        }

        private void OnDisable() { Clear(); }

        private void OnDestroy() { ReleaseResources(); }

        /// <summary>Destroys the flat materials (also called by edit-mode tools, where OnDestroy does not run).</summary>
        public void ReleaseResources()
        {
            Kill(ref _light); Kill(ref _dark);
            for (int i = 0; i < MaxTargets; i++) { Kill(ref _stain[i]); Kill(ref _knock[i]); }
        }

        private static void Kill(ref Material m)
        {
            if (m == null) return;
            if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
            m = null;
        }
    }
}
