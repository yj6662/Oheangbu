using UnityEngine;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#308 the bottom-left HUD cluster (SPEC-HUD-LIQUID-308, D308-11): the HP vessel (HP_BrushStroke, cinnabar), the ink
    /// vessel (Ink_BrushBar, ink) and three action marks (Mark_Dodge, Mark_Jump, Mark_Vehicle) under "Vessels308", which carries
    /// its own Canvas so a sloshing liquid never rebuilds the rest of the HUD. Built by HudController when the profile is on;
    /// HudController forwards values (SetHp01 / SetInk01 ...) and calls Tick from its LateUpdate.
    /// D308-11b: (1) the liquid is calm while the player stands still and moves only in answer to what the player really did,
    /// in proportion (AddAction: measured speed changes, fast view turning, take-off, landing; SetHp01 / SetInk01: damage,
    /// ink spent, ink poured; KickInk: the jolt of a hit; an impact frame that really fires). Nothing here runs on a clock of
    /// its own and the motion of the camera is no input. (2) each mark carries the key that triggers it (SetKeys: cells picked from the
    /// real bindings), drawn by the mark's own graphic - five graphics as before, no text object, never a sentence.
    /// Game values stay immediate (HudController.Hp01 / Ink01); this only decides how the surface reaches them.
    /// Time: unscaled (the liquid keeps moving in the drawing slow-down) and frozen while Time.timeScale is 0 (profile switch).
    /// The tick path allocates nothing: states are structs, payloads are Vector4, no strings, no LINQ, no closures.
    /// Read-only towards the game: nothing here feeds recognition, power or judgement. No static state.</summary>
    [DisallowMultipleComponent]
    public sealed class HudVessels308 : MonoBehaviour
    {
        HudLiquid308ProfileSO profile;
        UiStyle304SO style;
        WorldMacroHudSkinProfileSO skin;
        Canvas ownCanvas, rootCanvas;
        RectTransform frame;
        InkVesselGraphic308 hp, ink, markDodge, markJump, markVehicle;
        LiquidParams308 hpParams, inkParams;
        LiquidState308 hpState, inkState;
        HudActionState308 action;
        HudMarkView308 viewDodge, viewJump, viewVehicle;
        float wetDodge = 1f, wetJump = 1f, wetVehicle = 1f;
        ImpactGate308 gate;
        ActionSample308 pending;                              // what the player did since the last Tick (zero = nothing)
        HudKeys308 keys = HudKeys308.Empty;                   // key glyph cells of the three marks (-1 = none)
        float lowInkThreshold = -1f, inkCost01;
        float userScale = 1f;
        bool reducedMotion, hidden, previewing, built;

        public HudLiquid308ProfileSO Profile => profile;
        public InkVesselGraphic308 Hp => hp;
        public InkVesselGraphic308 Ink => ink;
        public InkVesselGraphic308 MarkDodge => markDodge;
        public InkVesselGraphic308 MarkJump => markJump;
        public InkVesselGraphic308 MarkVehicle => markVehicle;
        public LiquidState308 HpState => hpState;
        public LiquidState308 InkState => inkState;
        public HudMarkView308 DodgeView => viewDodge;
        public HudMarkView308 JumpView => viewJump;
        public HudMarkView308 VehicleView => viewVehicle;
        public HudKeys308 Keys => keys;
        public Canvas OwnCanvas => ownCanvas;
        public bool Previewing => previewing;
        public bool ReducedMotion => reducedMotion;
        /// <summary>Both liquids rest and no mark is changing: Tick writes nothing, the canvas is not rebuilt.</summary>
        public bool Still => hpState.Still && inkState.Still;
        public float Scale => frame != null ? frame.localScale.x : 1f;
        public float LowInkThreshold => lowInkThreshold >= 0f || profile == null ? lowInkThreshold : profile.Low.LowInkFallback;
        public float InkCostPreview01 => inkCost01;

        /// <summary>Builds the cluster under `canvas` (HUD_Canvas). Coordinates are mockup px of the 1920 x 1080 frame anchored to
        /// the profile's corner; the frame scales about the safe corner (ClusterScale x the user's UI scale).</summary>
        public static HudVessels308 Create(HudLiquid308ProfileSO profile, UiStyle304SO style, HudTokens304 tokens, Transform canvas,
            WorldMacroHudSkinProfileSO skin, float hp01, float ink01)
        {
            if (profile == null || canvas == null) return null;
            style = style != null ? style : UiStyle304SO.Fallback;
            var l = profile.Layout;
            var pivot = new Vector2(l.Pivot.x / UiPageFit304.Width, 1f - l.Pivot.y / UiPageFit304.Height);
            var rect = V.RectAnchored("Vessels308", canvas, 0f, 0f, UiPageFit304.Width, UiPageFit304.Height, l.Anchor, pivot);
            var self = rect.gameObject.AddComponent<HudVessels308>();
            self.profile = profile; self.style = style; self.skin = skin; self.frame = rect;
            self.ownCanvas = rect.gameObject.AddComponent<Canvas>();
            InkVesselGraphic308.EnsureChannels(self.ownCanvas);
            self.rootCanvas = self.ownCanvas.rootCanvas;

            var atlas = profile.Atlas;
            float outset = atlas.RefRect.y > 0f ? atlas.MarginPx / atlas.RefRect.y : 0f;
            // fallback quad span in rect-height shares (quad uv -> rect uv)
            float q = atlas.QuadRef.y, m = atlas.MarginPx;
            var span = new Vector2((atlas.FloorUv * q - m) / Mathf.Max(1f, q - 2f * m), (atlas.FullUv * q - m) / Mathf.Max(1f, q - 2f * m));
            self.hp = Element(profile, rect, "HP_BrushStroke", l.Hp, style.Cinnabar, outset, span);
            self.ink = Element(profile, rect, "Ink_BrushBar", l.Ink, style.Ink, outset, span);     // drawn after HP: in front where they touch
            // a mark quad is its rect + the atlas cell's margin on every side: the paper rim spills past the 44 px rect, uncut
            float markOutset = atlas.MarkRefPx > 0f ? atlas.MarkMarginPx / atlas.MarkRefPx : 0f;
            self.markDodge = Element(profile, rect, "Mark_Dodge", MarkRect(l, l.DodgeDeg), style.Ink, markOutset, span);
            self.markJump = Element(profile, rect, "Mark_Jump", MarkRect(l, l.JumpDeg), style.Ink, markOutset, span);
            self.markVehicle = Element(profile, rect, "Mark_Vehicle", MarkRect(l, l.VehicleDeg), style.Ink, markOutset, span);
            // D308-11b: where each mark draws its key glyph (the lid's lower-left edge). Which key it is comes later (SetKeys)
            var k = profile.Marks;
            self.markDodge.ConfigureKey(KeyOffset(l, k, l.DodgeDeg), k.KeySize, l.MarkSize);
            self.markJump.ConfigureKey(KeyOffset(l, k, l.JumpDeg), k.KeySize, l.MarkSize);
            self.markVehicle.ConfigureKey(KeyOffset(l, k, l.VehicleDeg), k.KeySize, l.MarkSize);

            float received = tokens != null ? tokens.ReceivedSeconds : .45f;
            self.hpParams = profile.Hp.Resolve(profile, style, received);
            self.inkParams = profile.Ink.Resolve(profile, style, received);
            LiquidSim308.Reset(ref self.hpState, hp01);
            LiquidSim308.Reset(ref self.inkState, ink01);
            self.gate.Clear();
            self.built = true;
            self.ApplyScale();
            self.Snap();
            return self;
        }

        static Rect MarkRect(LayoutSpec308 l, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            float cx = l.ArcCentre.x + l.ArcRadius * Mathf.Cos(a), cy = l.ArcCentre.y - l.ArcRadius * Mathf.Sin(a);
            return new Rect(cx - l.MarkSize * .5f, cy - l.MarkSize * .5f, l.MarkSize, l.MarkSize);
        }

        // The keycap has straight edges and a 1 px hairline, and the marks sit on an arc at fractional px: the keycap's rect is put
        // on whole design px (= screen px at 1080p and scale 1), at most half a px from Marks.KeyOffset. Returns the offset of
        // the keycap centre from the mark centre (design px, y down).
        static Vector2 KeyOffset(LayoutSpec308 l, MarkSpec308 k, float degrees)
        {
            Vector2 c = MarkRect(l, degrees).center;
            float x = Mathf.Round(c.x + k.KeyOffset.x - k.KeySize * .5f), y = Mathf.Round(c.y + k.KeyOffset.y - k.KeySize * .5f);
            return new Vector2(x + k.KeySize * .5f - c.x, y + k.KeySize * .5f - c.y);
        }

        static InkVesselGraphic308 Element(HudLiquid308ProfileSO profile, Transform parent, string name, Rect at, Color color, float outset, Vector2 span)
        {
            var r = V.Rect(name, parent, at.x, at.y, at.width, at.height);
            var g = r.gameObject.AddComponent<InkVesselGraphic308>();
            g.raycastTarget = false;
            if (profile.Material != null) g.material = profile.Material;
            g.color = color;
            g.Configure(profile.AtlasTexture, outset, span);
            return g;
        }

        // ------------------------------------------------------------------ values (called by HudController)
        public void SetHp01(float value) { SetValue(ref hpState, in hpParams, value); }
        public void SetInk01(float value) { SetValue(ref inkState, in inkParams, value); }

        void SetValue(ref LiquidState308 s, in LiquidParams308 p, float value)
        {
            if (!built) return;
            // while the HUD is not on screen nothing is replayed later: the surface simply is the value
            if (hidden || !isActiveAndEnabled) LiquidSim308.Reset(ref s, value);
            else LiquidSim308.SetValue(ref s, in p, value);
        }

        /// <summary>Received ink (harvest chunk, parry refund): the share above (to01 - received) reads lighter for a moment.</summary>
        public void NotifyInkGained(float received, float to01)
        {
            if (!built || hidden || received <= 0f) return;
            float to = Mathf.Min(Mathf.Clamp01(to01), inkState.Value);
            float from = Mathf.Max(0f, Mathf.Clamp01(to01) - received);
            if (to <= from || received <= inkParams.PourMinDelta) return;
            LiquidSim308.MarkFresh(ref inkState, in inkParams, Mathf.Min(from, inkState.Level));
        }

        /// <summary>A jolt on the ink (taking a hit, a chunk landing in the bottle), strength 0..1: ripples in proportion, half of
        /// them on the HP liquid. The level is not touched.</summary>
        public void KickInk(float strength)
        {
            if (!built || hidden || reducedMotion) return;
            LiquidSim308.Jolt(ref hpState, ref inkState, in hpParams, in inkParams, strength);
        }

        /// <summary>Cost of the stroke being drawn as a share of the full vessel; 0 hides the line.</summary>
        public void SetInkCostPreview(float cost01) { inkCost01 = Mathf.Clamp01(cost01); }

        /// <summary>What the player did this frame, as measured (ActionMeter308: speed changes of the body, fast view turning,
        /// take-off, landing). It is added up until the next Tick shoves the liquids with it, so nothing is lost or counted
        /// twice whichever of the presenter and the HUD updates first. A zero sample (standing still) is nothing at all.</summary>
        public void AddAction(in ActionSample308 sample)
        {
            if (!built || hidden || !sample.Any) return;
            pending.Add(in sample);
        }

        public void SetActionState(in HudActionState308 state) { action = state; }

        /// <summary>The key glyph of each mark: cells of the atlas key block picked from the real bindings (-1 = none).</summary>
        public void SetKeys(in HudKeys308 cells) { keys = cells; }

        /// <summary>Ink below this (one spell cost, CombatConfigSO.SpellInkCost) splits into dry-brush streaks.</summary>
        public void SetLowInkThreshold(float ink01) { lowInkThreshold = Mathf.Clamp01(ink01); }

        /// <summary>User settings: "UI 크기" (only this cluster follows it) and "움직임 줄이기".</summary>
        public void SetUserSettings(float uiScale, bool reduced)
        {
            userScale = uiScale; reducedMotion = reduced;
            ApplyScale();
        }

        void ApplyScale()
        {
            if (frame == null || profile == null) return;
            var l = profile.Layout;
            float k = l.ClusterScale * (l.FollowUiScale ? Mathf.Clamp(userScale, l.UiScaleClamp.x, l.UiScaleClamp.y) : 1f);
            if (!Mathf.Approximately(frame.localScale.x, k)) frame.localScale = new Vector3(k, k, 1f);
        }

        // ------------------------------------------------------------------ tick
        void OnEnable() { if (built) Snap(); }

        /// <summary>The surface IS the value, no tilt, no film, marks at their state: used when the HUD comes back on screen.</summary>
        public void Snap()
        {
            if (!built) return;
            LiquidSim308.Reset(ref hpState, hpState.Value);
            LiquidSim308.Reset(ref inkState, inkState.Value);
            gate.Clear();
            pending = default;
            HudActionRules308.Evaluate(in action, out viewDodge, out viewJump, out viewVehicle);
            wetDodge = viewDodge.Wet ? 1f : 0f; wetJump = viewJump.Wet ? 1f : 0f; wetVehicle = viewVehicle.Wet ? 1f : 0f;
            Push(0f);
        }

        /// <summary>Once per frame from HudController.LateUpdate.</summary>
        public void Tick()
        {
            if (!built || previewing) return;
            bool visible = rootCanvas != null && rootCanvas.enabled && rootCanvas.gameObject.activeInHierarchy;
            if (!visible) { hidden = true; return; }
            if (hidden) { hidden = false; Snap(); }

            var motion = profile.Motion; var impact = profile.Impact;
            float real = Mathf.Min(Time.unscaledDeltaTime, motion.MaxDt);
            float dt = motion.FreezeWhenPaused && Time.timeScale <= 0f ? 0f : real;
            bool reduced = reducedMotion;

            // ---- impact frame (D308-10b): one kick when it begins, value light while its cells run.
            // Only the STRENGTH travels to the shader; the side the light comes from is taken there, per pixel, from the live
            // deploy layer's point (ImpactHud308.hlsl), like every other HUD shader. The kick needs left / right only.
            ImpactFrameProbe308.Read(impact.FollowHudGlobal, out ImpactFrame308 hit);
            float light = gate.Step(in hit, real, impact.StuckSeconds, impact.MinInterval, out bool began);
            if (!impact.Enabled || reduced) light = 0f;
            if (began && impact.KickOnImpact && !reduced)
            {
                float k = Mathf.Max(hit.Light, impact.KickFloor);
                float pointX = hit.Viewport.x * Screen.width;
                KickAway(ref hpState, in hpParams, hp, pointX, k);
                KickAway(ref inkState, in inkParams, ink, pointX, k);
            }

            // ---- liquids: shoved by what the player did since the last Tick (nothing = nothing), then they swing on. There is
            // no other source of motion: no target tilt, no tremble, no clock (D308-11b)
            LiquidSim308.Frame(ref hpState, ref inkState, in hpParams, in inkParams, in pending, motion.TiltSign, reduced, dt);
            pending = default;

            // ---- marks: wet <-> dry (120 / 90 ms), at once when the action was just used or with reduced motion.
            // The re-wetting a mark showed last frame (cooldown / call-stroke progress) carries over when it turns usable: the
            // stroke that had re-wet to the end must not drop back to dry for the 120 ms fade (that reads as a blink).
            float shownDodge = viewDodge.Wet ? 0f : viewDodge.Fill, shownJump = viewJump.Wet ? 0f : viewJump.Fill,
                shownVehicle = viewVehicle.Wet ? 0f : viewVehicle.Fill;
            HudActionRules308.Evaluate(in action, out viewDodge, out viewJump, out viewVehicle);
            var marks = profile.Marks;
            wetDodge = Wetness(wetDodge, shownDodge, in viewDodge, marks, reduced, real);
            wetJump = Wetness(wetJump, shownJump, in viewJump, marks, reduced, real);
            wetVehicle = Wetness(wetVehicle, shownVehicle, in viewVehicle, marks, reduced, real);

            Push(Mathf.Clamp01(light * impact.LightGain));
        }

        static float Wetness(float now, float shownBefore, in HudMarkView308 view, MarkSpec308 marks, bool reduced, float dt)
        {
            float target = view.Wet ? 1f : 0f;
            if (reduced || (view.Snap && !view.Wet)) return target;
            if (view.Wet && shownBefore > now) now = Mathf.Clamp01(shownBefore);   // continue from the re-wet front, never below it
            float ms = target > now ? marks.RewetMs : marks.DryMs;
            return ms <= 0 ? target : Mathf.MoveTowards(now, target, dt * 1000f / ms);
        }

        void KickAway(ref LiquidState308 s, in LiquidParams308 p, InkVesselGraphic308 g, float pointX, float strength)
        {
            // the liquid is thrown away from the impact point: it piles up on the far side
            var r = g.rectTransform;
            float dx = pointX - r.TransformPoint(r.rect.center).x;   // screen px on the overlay HUD canvas (x has no flip)
            float away = dx >= 0f ? -1f : 1f;
            LiquidSim308.ImpactKick(ref s, in p, away, strength);
        }

        float LowHp01(float hp01)
        {
            float th = skin != null ? skin.DangerBeginsAtHp : profile.Low.LowHpFallback;
            return th > 0f && hp01 < th ? 1f - hp01 / th : 0f;
        }

        float HpDangerLevel => skin != null ? skin.DangerBeginsAtHp : profile.Low.LowHpFallback;

        float LowInk01(float ink01)
        {
            float th = LowInkThreshold;
            return th > 0f ? Mathf.Clamp01((th - ink01) / Mathf.Max(profile.Low.InkDryBand, 1e-4f)) : 0f;
        }

        // level left after paying the stroke cost; negative = the ink cannot pay (the line sits just above the floor)
        float CostLine()
        {
            if (inkCost01 <= .0001f) return 0f;
            float left = inkState.Value - inkCost01;
            return left >= 0f ? Mathf.Max(.001f, left) : -.012f;
        }

        // light = the strength of the impact light for this frame (0 = none). uv3.y carries it; uv3.z stays 0.
        void Push(float light)
        {
            Vessel(hp, in hpState, in hpParams, 1f, LowHp01(hpState.Value), HpDangerLevel, light);
            Vessel(ink, in inkState, in inkParams, 0f, LowInk01(inkState.Value), CostLine(), light);
            bool showKeys = profile.Marks.ShowKeys;
            Mark(markDodge, in viewDodge, wetDodge, light, showKeys ? keys.Dodge : HudKeyGlyph308.None);
            Mark(markJump, in viewJump, wetJump, light, showKeys ? keys.Jump : HudKeyGlyph308.None);
            Mark(markVehicle, in viewVehicle, wetVehicle, light, showKeys ? keys.Vehicle : HudKeyGlyph308.None);
        }

        static void Vessel(InkVesselGraphic308 g, in LiquidState308 s, in LiquidParams308 p, float code, float low, float third, float light)
        {
            if (g == null) return;
            float fresh = LiquidSim308.FreshNow(in s, in p);
            var a = new Vector4(s.Level, s.Tilt, s.WavePx, s.WavePx > 0f ? s.Phase : 0f);
            var b = fresh > 0f ? new Vector4(s.FreshBottom, -fresh, third, low)
                : new Vector4(s.WetTop, LiquidSim308.WetAlphaNow(in s, in p), third, low);
            var c = new Vector4(code, light, 0f, s.Pour);
            g.SetPayload(in a, in b, in c);
        }

        // keyCell = the key glyph this mark carries (-1 = none). It is a second quad of the mark's own graphic: hidden with the
        // mark, dim while the mark is dry. Reduced motion, the flash setting and an impact frame do not touch it.
        static void Mark(InkVesselGraphic308 g, in HudMarkView308 view, float wet, float light, int keyCell)
        {
            if (g == null) return;
            bool show = !view.Hidden;
            if (g.enabled != show) g.enabled = show;
            if (!show) return;
            g.SetKey(keyCell, wet);
            var a = new Vector4(wet, 0f, 0f, 0f);
            var b = Vector4.zero;
            var c = new Vector4((float)view.Glyph, light, 0f, view.Wet ? 0f : view.Fill);
            g.SetPayload(in a, in b, in c);
        }

        // ------------------------------------------------------------------ preview (editor stills; nothing is simulated)
        /// <summary>Holds the cluster at the given states until ClearPreview308 (Tick does nothing meanwhile). light = the strength
        /// of the impact light (0 = no impact look); the side it comes from is the live deploy layer's point, so the look only
        /// shows while an impact frame runs (HudLiquid308 forces one through ImpactFrameDirector308.ForceFrame).
        /// Used by HudLiquid308 preview:on (Edit Mode, nothing saved).</summary>
        public void ApplyPreview308(in LiquidState308 hpPose, in LiquidState308 inkPose, in HudActionState308 actions, float inkCost, float light)
        {
            if (!built) return;
            previewing = true;
            hpState = hpPose; inkState = inkPose; action = actions; inkCost01 = Mathf.Clamp01(inkCost);
            HudActionRules308.Evaluate(in action, out viewDodge, out viewJump, out viewVehicle);
            wetDodge = viewDodge.Wet ? 1f : 0f; wetJump = viewJump.Wet ? 1f : 0f; wetVehicle = viewVehicle.Wet ? 1f : 0f;
            Push(Mathf.Clamp01(light * profile.Impact.LightGain));
        }

        public void ClearPreview308()
        {
            if (!previewing) return;
            previewing = false; inkCost01 = 0f;
            Snap();
        }
    }
}
