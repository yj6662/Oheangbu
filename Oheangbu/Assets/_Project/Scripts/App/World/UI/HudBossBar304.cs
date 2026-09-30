using Oheangbu.Combat;
using TMPro;
using UnityEngine;
using V = Oheangbu.App.World.UI.PlaytestUiView;

namespace Oheangbu.App.World.UI
{
    /// <summary>#306 one enemy health stroke (SPEC-PLAYTEST-306 #8): an InkMeter304 with an ink value and a PAPER trailing chip
    /// (the lag layer; no cinnabar, no element colour - ART-UI "적 공격의 속성 힌트 표시 금지"). Bind() snaps to a new source
    /// (no chip from the previous enemy); Tick() follows IHealthBarSource306.Hp01: drops leave the chip, which holds and drains
    /// (Motion.MeterLagHoldMs / MeterLagDrainMs), rises move it with the value. Shared by the lock-on stroke and the boss bar.</summary>
    public sealed class HudHealthStroke304
    {
        public readonly InkMeter304 Meter;
        IHealthBarSource306 source;
        float shown = -1f;
        public IHealthBarSource306 Source => source;
        /// <summary>Unscaled time of the last drop (the lock-on stroke darkens for a moment after it).</summary>
        public float LastHitTime { get; private set; } = -1000f;
        public float Shown01 => shown;

        public HudHealthStroke304(UiStyle304SO s, HudTokens304 k, Transform parent, string name, float x, float y, float w, float h, float chipAlpha)
        {
            s = s != null ? s : UiStyle304SO.Fallback; k = k != null ? k : HudTokens304.Load();
            Meter = V.Meter(s, parent, name, x, y, w, h, s.Ink, true);
            // the HUD meters' QA2 linear look (HudController.LinearLook), with the chip in paper instead of the HP cinnabar
            var m = s.Meter;
            if (Meter.ValueRim != null && Meter.ValueRim.UsesMeterShader) Meter.ValueRim.EdgeOnly = k.MeterValueRimEdgeOnly;
            if (Meter.GhostEdge != null) Meter.GhostEdge.color = UiStyle304SO.A(s.Paper, k.PaperAlpha(Meter.GhostEdge.material, m.GhostEdge));
            if (Meter.ValueRim != null) Meter.ValueRim.color = UiStyle304SO.A(s.Paper, k.PaperAlpha(Meter.ValueRim.material, m.RimAlpha));
            if (Meter.Ghost != null) Meter.Ghost.color = UiStyle304SO.A(s.Ink, k.InkAlphaFor(Meter.Ghost.material, m.GhostInk));
            if (Meter.Lag != null) Meter.Lag.color = UiStyle304SO.A(s.Paper, k.PaperAlpha(Meter.Lag.material, chipAlpha));
            if (Meter.Value != null && HudTokens304.ShaderInkGamma(Meter.Value.material) > 1.0001f)
            { var c = Meter.Value.color; c.a = k.MeterValueAlpha; Meter.Value.color = c; }
        }

        /// <summary>null for a destroyed Unity object behind the interface (EnemyVitals is a MonoBehaviour).</summary>
        public static IHealthBarSource306 Live(IHealthBarSource306 s) => s is Object o && o == null ? null : s;

        /// <summary>True when the source changed (the next Tick snaps value and chip).</summary>
        public bool Bind(IHealthBarSource306 next)
        {
            next = Live(next);
            if (ReferenceEquals(next, source)) return false;
            source = next; shown = -1f; LastHitTime = -1000f;   // a new enemy starts at rest weight, no carried darkening
            return true;
        }

        public void Tick()
        {
            source = Live(source);
            if (source == null) return;
            float v = Mathf.Clamp01(source.Hp01);
            if (float.IsNaN(v)) v = 0f;
            if (shown < 0f) { Meter.SetValue(v, false); shown = v; return; }
            if (Mathf.Abs(v - shown) < .0005f) return;
            bool drop = v < shown;
            if (drop) LastHitTime = Time.unscaledTime;
            Meter.SetValue(v, drop); shown = v;
        }
    }

    /// <summary>#306 보스 바 (SPEC-PLAYTEST-306 #8, D306): bottom centre, a 720 x 8 HudHealthStroke304 with the boss name above
    /// it, left-aligned (Noto Serif KR 600 = EnemyBarSpec304.BossNameRole Label24, paper with the ink halo preset) - the Elden
    /// Ring layout. "BossBar304" under HUD_Canvas (no own Canvas): SetHud(false) hides it with the HUD. HudController.SetBossHealth
    /// feeds it (track C: engagement start, the dying boss once, leash reset -> null); HideNow on player death / teardown. Fades
    /// in / out (BossInMs / BossOutMs); a dead boss keeps the emptied bar for BossDeadHoldMs first (a later null keeps the hold).</summary>
    public sealed class HudBossBar304 : MonoBehaviour
    {
        UiStyle304SO s;
        EnemyBarSpec304 spec;
        RectTransform root, body;
        CanvasGroup group;
        Canvas canvas;
        HudHealthStroke304 stroke;
        TMP_Text nameLabel;
        float target, hideAt = -1f, builtScale = -1f;

        public bool Showing => target > 0f;
        public float Alpha => group != null ? group.alpha : 0f;
        public string BossName => nameLabel != null ? nameLabel.text : "";
        public InkMeter304 Meter => stroke != null ? stroke.Meter : null;
        public IHealthBarSource306 Source => stroke != null ? stroke.Source : null;
        public RectTransform Root => root;

        public static HudBossBar304 Create(UiStyle304SO s, HudTokens304 k, Transform hudCanvas, EnemyBarSpec304 spec)
        {
            s = s != null ? s : UiStyle304SO.Fallback; spec = spec ?? new EnemyBarSpec304();
            var b = spec.BossBar;
            var role = s.Role(spec.BossNameRole);
            float nameH = Mathf.Ceil(role.Size * role.LineHeight);
            float top = b.y - spec.BossNameGap - nameH;
            var root = V.RectAnchored("BossBar304", hudCanvas, b.x, top, b.width, b.y + b.height - top, spec.BossAnchor);
            var bar = root.gameObject.AddComponent<HudBossBar304>();
            bar.s = s; bar.spec = spec; bar.root = root;
            bar.group = root.gameObject.AddComponent<CanvasGroup>();
            bar.group.blocksRaycasts = false; bar.group.interactable = false; bar.group.alpha = 0f;
            bar.body = V.Stretch("Body", root);
            // name bottom-aligned in its box: a bigger 본문 크기 grows it up, away from the bar
            bar.nameLabel = V.Label(s, bar.body, "BossName", "", role, s.Paper, 0f, 0f, b.width, nameH, TextAlignmentOptions.BottomLeft);
            var halo = s.TmpMaterial(bar.nameLabel.font, TmpPreset304.Paper_UnderInk);
            if (halo != null) bar.nameLabel.fontSharedMaterial = halo;
            bar.stroke = new HudHealthStroke304(s, k, bar.body, "BossHpStroke", 0f, b.y - top, b.width, b.height, spec.ChipAlpha);
            bar.body.gameObject.SetActive(false);
            bar.canvas = root.GetComponentInParent<Canvas>();
            return bar;
        }

        /// <summary>The engaged boss, or null to hide. A dead boss (Alive false) empties the bar, holds it, then fades; a null
        /// during that hold leaves it running (LateUpdate ends it) - HideNow cuts it short.</summary>
        public void Set(IHealthBarSource306 boss)
        {
            boss = HudHealthStroke304.Live(boss);
            if (boss == null) { if (hideAt < 0f) target = 0f; return; }
            if (stroke.Bind(boss) || nameLabel.text != (boss.DisplayName ?? "")) nameLabel.text = boss.DisplayName ?? "";
            if (boss.Alive) { target = 1f; hideAt = -1f; return; }
            if (target > 0f && hideAt < 0f) hideAt = Time.unscaledTime + spec.BossDeadHoldMs / 1000f;
        }

        /// <summary>Gone at once, no fade, any dead-boss hold dropped (player death, teardown): the bar never lingers over a respawn.</summary>
        public void HideNow()
        {
            target = 0f; hideAt = -1f;
            if (group != null) group.alpha = 0f;
            if (body != null && body.gameObject.activeSelf) body.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            float now = Time.unscaledTime;
            if (hideAt >= 0f && now >= hideAt) { target = 0f; hideAt = -1f; }
            // SetHud(false): menus, map, loading, ending. A bar released meanwhile (player death, leash reset) is gone
            // when the HUD returns instead of fading out over the respawn.
            if (canvas != null && !canvas.enabled)
            {
                if (target <= 0f && group.alpha > 0f) { group.alpha = 0f; if (body.gameObject.activeSelf) body.gameObject.SetActive(false); }
                return;
            }
            bool reduced = UiTween304.ReducedMotion;
            float sec = s.Motion.Sec(target > group.alpha ? spec.BossInMs : spec.BossOutMs, reduced);
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / Mathf.Max(.01f, sec));
            bool on = group.alpha > 0f || target > 0f;
            if (body.gameObject.activeSelf != on) body.gameObject.SetActive(on);
            if (!on) return;
            stroke.Tick();
            if (!Mathf.Approximately(builtScale, UiText304.TextScale)) { builtScale = UiText304.TextScale; UiText304.ApplyTextScale(body, builtScale); }
        }
    }
}
