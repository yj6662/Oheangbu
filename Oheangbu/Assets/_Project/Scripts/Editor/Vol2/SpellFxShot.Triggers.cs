using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellFx;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using Object = UnityEngine.Object;
using Vr = Oheangbu.Vol2.Rules;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>2026-10-31 (PLAN-308-MEMBRANE section 5.2 item 6, the trigger lane): the scenes the judge keeps calling "판정 불가" because nothing in the lab makes them happen - a hit on the
    /// caster, an enemy shot into the orbit / the return path, a second cast, the caster's own swing landing on a body, a standing prop for 가른다 to cut. Every option here only DOES what the
    /// game does through its own doors (PlayerVitals.TakeAttackDamage, the letter channel, the production swing driver with the lab's scripted hand, the material registry); no rule file is
    /// touched. Times are cast ages (the ageSec column of index.csv: scope clock from the commit), the same clock as walk=. Options (all go after the usual ones, ':' separated):
    ///   shots=&lt;start s&gt;[,&lt;count 1-8&gt;,&lt;gap s&gt;,&lt;dir&gt;[,&lt;m/s&gt;]]   enemy shots flying at the caster's chest from 7.5 m (dir: front 0 | right 90 | left -90 | back 180 | degrees from the cast forward).
    ///        A value with a ',' or a '.' is this timed form; a bare integer (shots=3) stays the OLD form (N look-only shots, one every 0.3 s from 0.35 s after the cast, 0 - 8).
    ///        Each shot is a look-only SpellFxLabShot (the 겅 part dissolves it) and, for a 돌아온다 cast (or shotrule=1), a twin Vol2LabShot registered in the Return executor's hub (the RULES
    ///        take it: Vol2ReturnReport.Intercepts) with its renderer off. shotrule=0|1|auto.
    ///   hitme=&lt;s&gt;[,&lt;HP 0.1-50, default 5&gt;]     PlayerVitals.TakeAttackDamage(melee, attacker = the nearest body): the real "I was hit" (Damaged -> Vol2SeedHitGate -> the 검 seed).
    ///        With fake=1 it also ends the fake prefab as the rules' recovery would (OnEnd Done: the 걱 part reads it as the interrupt).
    ///   recast=&lt;s&gt;[,&lt;letter&gt;[,&lt;variant&gt;]]  the letter raised again on the letter channel (same letter by default). A same-letter 감 gives only "Plant" contacts: for the 감 follow-up cast a 한 번 (가).
    ///   swinghit=&lt;s&gt;[,script|real]       the caster's swing lands on the nearest body. script (default) = the lab's scripted hand through the production Vol2SwingDriver, tip on the body at any
    ///        distance; real = the driver's own source (the great brush's real swing, the body must be within the brush's reach; also feeds the real 갈무리 -> the 감 seed).
    ///   prop=&lt;ahead m&gt;,&lt;side m&gt;[,&lt;radius m, default 0.7&gt;][/...]   a standing residue material (Earth, player faction) with a low stone-grey slab (0.24 m) on it, up to four; 가른다 splits the material (the
    ///        halves follow their footprints).
    /// What reached the rules is written to the index.csv note column (hitme, shot0:rule-taken, swing:hit, recast ...) and to the end of summary.txt ("triggers:").</summary>
    public static partial class SpellFxShot
    {
        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // options
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        sealed class TrigSpec
        {
            public bool Shots2; public int ShotCount = 1, ShotRule = -1; public float ShotAt, ShotGap = 0.4f, ShotDeg, ShotSpeed = 8.5f;
            public float HitMeAt = -1f, HitMeHp = 5f;
            public float RecastAt = -1f; public char RecastLetter; public string RecastVariant;
            public float SwingAt = -1f; public bool SwingReal;
            public readonly List<Vector3> Props = new List<Vector3>();      // (ahead, side, radius)
            public bool Any => Shots2 || HitMeAt >= 0f || RecastAt >= 0f || SwingAt >= 0f || Props.Count > 0;
        }

        const float ShotFromMeters = 7.5f, PropRadiusDefault = 0.7f;

        static bool Pf(string s, out float f) => float.TryParse((s ?? "").Trim(), NumberStyles.Float, Inv, out f) && !float.IsNaN(f) && !float.IsInfinity(f);

        /// <summary>null = fine, "" = not a trigger option, else what is wrong.</summary>
        static string ParseTrigger(string k, string v, Job job)
        {
            var t = job.Trig;
            switch (k)
            {
                case "shots": return ParseShots(v, job);
                case "shotrule": if (v != "0" && v != "1" && v != "auto") return "0 | 1 | auto"; t.ShotRule = v == "auto" ? -1 : v == "1" ? 1 : 0; return null;
                case "hitme": return ParseHitMe(v, t);
                case "recast": return ParseRecast(v, t);
                case "swinghit": return ParseSwingHit(v, t);
                case "prop": return ParseProp(v, t);
            }
            return "";
        }

        static string ParseShots(string v, Job job)
        {
            var t = job.Trig;
            if (v.IndexOf(',') < 0 && v.IndexOf('.') < 0)
            {
                if (!int.TryParse(v, NumberStyles.Integer, Inv, out int n) || n < 0 || n > 8) return "the old count 0 - 8; the timed form is <start s>[,<count>,<gap s>,<dir>[,<m/s>]] and has a ',' or a '.' (shots=2.0)";
                job.Shots = n; t.Shots2 = false; return null;
            }
            var w = v.Split(',');
            if (w.Length > 5) return "at most five values";
            if (!Pf(w[0], out float at) || at < 0f || at > 120f) return "start second 0 - 120";
            int count = 1; float gap = 0.4f, deg = 0f, speed = 8.5f;
            if (w.Length > 1 && w[1].Trim().Length > 0 && (!int.TryParse(w[1].Trim(), NumberStyles.Integer, Inv, out count) || count < 1 || count > 8)) return "count 1 - 8";
            if (w.Length > 2 && w[2].Trim().Length > 0 && (!Pf(w[2], out gap) || gap < 0.05f || gap > 5f)) return "gap 0.05 - 5 s";
            if (w.Length > 3 && w[3].Trim().Length > 0)
            {
                string d = w[3].Trim().ToLowerInvariant();
                if (d == "front" || d == "toward" || d == "in") deg = 0f;
                else if (d == "right") deg = 90f;
                else if (d == "left") deg = -90f;
                else if (d == "back" || d == "away") deg = 180f;
                else if (!Pf(d, out deg)) return "direction front | right | left | back | degrees";
            }
            if (w.Length > 4 && w[4].Trim().Length > 0 && (!Pf(w[4], out speed) || speed < 1f || speed > 30f)) return "speed 1 - 30 m/s";
            job.Shots = 0; t.Shots2 = true; t.ShotAt = at; t.ShotCount = count; t.ShotGap = gap; t.ShotDeg = deg; t.ShotSpeed = speed;
            return null;
        }

        static string ParseHitMe(string v, TrigSpec t)
        {
            var w = v.Split(',');
            if (w.Length > 2) return "at most two values";
            if (!Pf(w[0], out float at) || at < 0f || at > 120f) return "start second 0 - 120";
            float hp = 5f;
            if (w.Length > 1 && w[1].Trim().Length > 0 && (!Pf(w[1], out hp) || hp < 0.1f || hp > 50f)) return "HP 0.1 - 50";
            t.HitMeAt = at; t.HitMeHp = hp;
            return null;
        }

        static string ParseRecast(string v, TrigSpec t)
        {
            var w = v.Split(',');
            if (w.Length > 3) return "at most three values";
            if (!Pf(w[0], out float at) || at < 0f || at > 120f) return "start second 0 - 120";
            char letter = '\0'; string variant = null;
            if (w.Length > 1 && w[1].Trim().Length > 0) { if (w[1].Trim().Length != 1) return "the letter is one character"; letter = w[1].Trim()[0]; }
            if (w.Length > 2 && w[2].Trim().Length > 0) variant = w[2].Trim();
            t.RecastAt = at; t.RecastLetter = letter; t.RecastVariant = variant;
            return null;
        }

        static string ParseSwingHit(string v, TrigSpec t)
        {
            var w = v.Split(',');
            if (w.Length > 2) return "at most two values";
            if (!Pf(w[0], out float at) || at < 0f || at > 120f) return "start second 0 - 120";
            bool real = false;
            if (w.Length > 1 && w[1].Trim().Length > 0)
            {
                string m = w[1].Trim().ToLowerInvariant();
                if (m == "real") real = true; else if (m != "script") return "mode script | real";
            }
            t.SwingAt = at; t.SwingReal = real;
            return null;
        }

        static string ParseProp(string v, TrigSpec t)
        {
            var list = new List<Vector3>();
            foreach (string one in v.Split('/'))
            {
                var w = one.Split(',');
                if (w.Length < 2 || w.Length > 3) return "<ahead m>,<side m>[,<radius m>] and '/' between props";
                if (!Pf(w[0], out float ahead) || ahead < -2f || ahead > 14f) return "ahead -2 - 14 m";
                if (!Pf(w[1], out float side) || side < -8f || side > 8f) return "side -8 - 8 m";
                float r = PropRadiusDefault;
                if (w.Length > 2 && w[2].Trim().Length > 0 && (!Pf(w[2], out r) || r < 0.3f || r > 2f)) return "radius 0.3 - 2 m";
                list.Add(new Vector3(ahead, side, r));
            }
            if (list.Count == 0 || list.Count > 4) return "one to four props";
            t.Props.Clear(); t.Props.AddRange(list);
            return null;
        }

        /// <summary>The words of the queued message: what the run will do besides the cast.</summary>
        static string TrigDescribe(Job p)
        {
            var t = p.Trig; var sb = new StringBuilder();
            if (p.Shots > 0) sb.Append(" | shots=" + p.Shots + " (old form: " + p.Shots + " look-only shots, 0.3 s apart from 0.35 s after the cast)");
            if (t.Shots2) sb.Append(" | shots from age " + t.ShotAt.ToString("0.##", Inv) + " s: " + t.ShotCount + " x every " + t.ShotGap.ToString("0.##", Inv) + " s from " + t.ShotDeg.ToString("0.#", Inv) + " deg at " + t.ShotSpeed.ToString("0.#", Inv) + " m/s, rule twin " + (t.ShotRule < 0 ? "auto (돌아온다 casts)" : t.ShotRule == 1 ? "on" : "off"));
            if (t.HitMeAt >= 0f) sb.Append(" | hitme at " + t.HitMeAt.ToString("0.##", Inv) + " s (" + t.HitMeHp.ToString("0.##", Inv) + " HP)");
            if (t.RecastAt >= 0f) sb.Append(" | recast at " + t.RecastAt.ToString("0.##", Inv) + " s" + (t.RecastLetter != '\0' ? " letter " + t.RecastLetter : " same letter") + (t.RecastVariant != null ? " variant " + t.RecastVariant : ""));
            if (t.SwingAt >= 0f) sb.Append(" | swinghit at " + t.SwingAt.ToString("0.##", Inv) + " s (" + (t.SwingReal ? "real swing" : "scripted hand") + ")");
            if (t.Props.Count > 0) sb.Append(" | prop " + string.Join(" ", t.Props.Select(q => q.x.ToString("0.##", Inv) + "/" + q.y.ToString("0.##", Inv) + " r" + q.z.ToString("0.##", Inv))));
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // state of the current job (reset in TrigSetup, put back in TrigRestore)
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        sealed class TShot
        {
            public int Index; public double SpawnAge; public SpellFxLabShot Look; public Vol2LabShot Rule; public bool WasFlying;
            public double LookTakenAge = -1, LookGoneAge = -1, RuleTakenAge = -1; public Vector3 LookTakenAt, RuleTakenAt;
        }

        sealed class TProp { public Vector3 Spec; public Vol2Material M; public GameObject Go; public Material Mat; public bool Root; }

        static readonly List<TShot> TShots = new List<TShot>();
        static readonly List<TProp> TProps = new List<TProp>();
        static readonly HashSet<long> TPropCasts = new HashSet<long>();
        static readonly List<string> TLog = new List<string>(), TReact = new List<string>();
        static string _tTok = "", _tPending = "";
        static int _tShotNext, _tReactions;
        static bool _tHitDone, _tRecastDone, _tSwingDone;
        static Vol2CastLog _recastLog; static Vol2CastDecision _recastDecision; static char _recastLetter;
        static PlayerVitals _hpPlayer; static float _hpSaved01; static bool _hpSaved;
        static SwingPassRig _swingRig; static Vol2SwingDriver _swingDrv; static bool _swingWatch, _swingSawOpen, _swingRealGate, _swingRealRestore; static int _swingWait, _swingSwings0, _swingEvents0;
        static SpellFxCast _fakeCast;
        static Action<Vol2Material> _onPropCreated; static Action<Vol2ReactionVisual> _onReaction;

        static void TrigSetup()
        {
            TShots.Clear(); TProps.Clear(); TPropCasts.Clear(); TLog.Clear(); TReact.Clear();
            _tTok = ""; _tPending = ""; _tShotNext = 0; _tReactions = 0; _tHitDone = _tRecastDone = _tSwingDone = false;
            _recastLog = null; _recastDecision = null; _recastLetter = '\0';
            _swingRig = null; _swingDrv = null; _swingWatch = _swingSawOpen = false; _swingWait = 0;
        }

        static void Tok(string s) { _tTok = _tTok.Length == 0 ? s : _tTok + " " + s; }
        static void TNote(double age, string text) { if (TLog.Count < 40) TLog.Add(age.ToString("0.00", Inv) + " s " + text); }

        static double CurrentAge()
        {
            if (_coord == null) return 0.0;
            long now = _coord.Services.Clock.NowUs;
            if (_beganLog != null) return (now - _beganLog.StartUs) / 1000000.0;
            return _fakePlayer != null ? (now - _fakeStartUs) / 1000000.0 : 0.0;
        }

        static Vol2ReturnExecutor ReturnEx() => _coord != null && _coord.Verbs.TryGet(Vr.Verb.Return, out var e) ? e as Vol2ReturnExecutor : null;
        static Vol2SplitExecutor SplitEx() => _coord != null && _coord.Verbs.TryGet(Vr.Verb.Split, out var e) ? e as Vol2SplitExecutor : null;

        static Walker NearestBody()
        {
            Walker best = null; float bd = float.MaxValue;
            foreach (var w in Walkers)
            {
                if (w.Go == null || (w.Vitals != null && !w.Vitals.IsAlive)) continue;
                float d = Flat(w.Go.transform.position - _origin).magnitude;
                if (d < bd) { bd = d; best = w; }
            }
            return best;
        }

        static string CastFrame(Vector3 p)
        {
            Vector3 d = Flat(p - _origin);
            return F(Vector3.Dot(d, _fwd)) + "/" + F(Vector3.Dot(d, _right)) + "/" + F(p.y - _origin.y);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // before the letter: the props
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        static void TrigBeforeCast(Job j)
        {
            var t = j.Trig;
            if (t == null || !t.Any || _coord == null) return;
            if (t.Props.Count > 0)
            {
                long now = _coord.Services.Clock.NowUs;
                _onPropCreated = OnPropMaterial; _coord.Materials.Created += _onPropCreated;
                for (int k = 0; k < t.Props.Count; k++)
                {
                    var p = t.Props[k];
                    Vector3 at = _origin + _fwd * p.x + _right * p.y; at.y = _origin.y;
                    var cast = new Vr.CastId(98301 + k);
                    Vol2Material m = null;
                    try
                    {
                        // a standing residue of another cast, as the old captures staged one (Vol2VfxCapture.PlaceRet): Earth, the player's, 30 s - 가른다 cuts the materials it passes over
                        m = _coord.Materials.Create(cast, '막', Vr.Body.Earth, Vol2MaterialKind.Residue, 0.9m, Vr.Vol2Time.FromSeconds(30.0), Vol2Footprint.Disc(at, p.z), 1, Vol2Faction.Player, true, now, 0, 0, "lab-prop");
                        TPropCasts.Add(cast.Value);
                    }
                    catch (Exception e) { Problems.Add(j.Name + ": prop " + k + " material not made: " + e.GetType().Name + " " + e.Message); }
                    var proxy = MakePropProxy(p, m, true);
                    TProps.Add(proxy);
                }
                SyncProps();
            }
            if (t.Any) { _onReaction = OnReaction; try { _coord.Reactions.ReactionResolved += _onReaction; } catch (Exception) { _onReaction = null; } }
        }

        static TProp MakePropProxy(Vector3 spec, Vol2Material m, bool root)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "SpellFxShotProp";
            var col = go.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
            Material mat = null;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (sh != null)
            {
                mat = new Material(sh) { name = "SpellFxShotProp" };
                var c = new Color(0.30f, 0.29f, 0.27f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c); else mat.color = c;
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.06f);
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            return new TProp { Spec = spec, M = m, Go = go, Mat = mat, Root = root };
        }

        /// <summary>A half the split made (the registry raises Created for the children AFTER the parent's Changed "split"): it gets its own stone, placed by SyncProps from its footprint.</summary>
        static void OnPropMaterial(Vol2Material m)
        {
            try
            {
                if (m == null || !TPropCasts.Contains(m.Cast.Value) || m.Origin != "split") return;
                TProps.Add(MakePropProxy(Vector3.zero, m, false));
                _tPending = _tPending.Length == 0 ? "prop:split-half" : _tPending + " prop:split-half";
            }
            catch (Exception e) { Problems.Add("prop half: " + e.GetType().Name + " " + e.Message); }
        }

        static void OnReaction(Vol2ReactionVisual v)
        {
            try
            {
                _tReactions++;
                if (v != null && TReact.Count < 6) TReact.Add(CurrentAge().ToString("0.00", Inv) + " s " + v);
                _tPending = _tPending.Length == 0 ? "reaction" : _tPending + " reaction";
            }
            catch (Exception) { }
        }

        /// <summary>The stones follow their materials' footprints (a split moves the halves apart); a material that ended hides its stone.</summary>
        const float PropHalfHeight = 0.12f;      // the unit cylinder is 2 m tall: scale y = half the height

        static void SyncProps()
        {
            foreach (var p in TProps)
            {
                if (p.Go == null) continue;
                var m = p.M; var fp = m != null ? m.Footprint : null;
                bool show = m != null ? m.Alive && fp != null && fp.Kind == Vol2FootprintKind.Disc : p.Root;
                if (!show) { if (p.Go.activeSelf) p.Go.SetActive(false); continue; }
                Vector3 c; float r;
                if (fp != null) { c = fp.Origin; r = fp.Radius; } else { c = _origin + _fwd * p.Spec.x + _right * p.Spec.y; r = p.Spec.z; }
                c.y = _origin.y;
                if (!p.Go.activeSelf) p.Go.SetActive(true);
                // 2026-10-31: a low slab (0.24 m), not a 1.1 m drum - on the cast's axis the drum stood between the shoulder camera and the body and hid the whole cut (갓 prop=3,0)
                p.Go.transform.position = c + Vector3.up * PropHalfHeight;
                p.Go.transform.localScale = new Vector3(Mathf.Max(0.1f, 2f * r), PropHalfHeight, Mathf.Max(0.1f, 2f * r));
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // every frame (after the bodies have walked, before the rules are watched)
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        static void TrigStep(Job j, double age, long now)
        {
            _tTok = _tPending; _tPending = "";
            var t = j.Trig;
            if (t == null || !t.Any) return;
            try
            {
                SyncProps();
                if (t.Shots2) StepShots(j, t, age);
                WatchShots(age);
                if (t.HitMeAt >= 0f && !_tHitDone && age >= t.HitMeAt) { _tHitDone = true; DoHitMe(j, t, age); }
                if (t.RecastAt >= 0f && !_tRecastDone && age >= t.RecastAt) { _tRecastDone = true; DoRecast(j, t, age); }
                if (t.SwingAt >= 0f && !_tSwingDone && age >= t.SwingAt) { _tSwingDone = true; DoSwingHit(j, t, age, now); }
                WatchSwing(age);
            }
            catch (Exception e) { Problems.Add(j.Name + ": trigger step EXCEPTION " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
        }

        // ---- shots= ----

        static void StepShots(Job j, TrigSpec t, double age)
        {
            while (_tShotNext < t.ShotCount && age >= t.ShotAt + _tShotNext * t.ShotGap) { SpawnShot(j, t, _tShotNext, age); _tShotNext++; }
        }

        static void SpawnShot(Job j, TrigSpec t, int k, double age)
        {
            float ang = t.ShotDeg * Mathf.Deg2Rad;
            Vector3 dir = _fwd * Mathf.Cos(ang) + _right * Mathf.Sin(ang);
            Vector3 from = _origin + dir * ShotFromMeters + Vector3.up * (1.1f + 0.35f * (k % 2)), to = _origin + Vector3.up * 1.15f;
            var s = new TShot { Index = k, SpawnAge = age };
            s.Look = SpellFxLabShot.Create(from, to, t.ShotSpeed, 0f);
            LabShots.Add(s.Look.gameObject);
            bool returnCast = _beganLog != null && _beganLog.Glyph.Verb == Vr.Verb.Return;
            bool twin = !j.Fake && (t.ShotRule == 1 || (t.ShotRule < 0 && returnCast));
            string ruleNote = "no rule twin";
            if (twin)
            {
                var ex = ReturnEx();
                if (ex == null) ruleNote = "no rule twin (the scope has no Return executor)";
                else
                {
                    var shooter = NearestBody();
                    s.Rule = Vol2LabShot.Create("trig" + k, from, (to - from).normalized * t.ShotSpeed, 6f, shooter != null ? shooter.Vitals : null);
                    var r = s.Rule.GetComponent<Renderer>(); if (r != null) r.enabled = false;      // the look is the SpellFxLabShot; this one is only what the rules see
                    ex.Hub.Register(s.Rule);
                    ruleNote = "rule twin registered in the Return executor's hub";
                }
            }
            TShots.Add(s);
            TNote(age, "shot" + k + " from " + t.ShotDeg.ToString("0.#", Inv) + " deg (" + CastFrame(from) + " -> chest) " + t.ShotSpeed.ToString("0.#", Inv) + " m/s; " + ruleNote);
            Tok("shot" + k);
        }

        static void WatchShots(double age)
        {
            foreach (var s in TShots)
            {
                if (s.Look != null)
                {
                    if (s.Look.Flying) s.WasFlying = true;
                    else if (s.WasFlying && s.LookTakenAge < 0) { s.LookTakenAge = age; s.LookTakenAt = s.Look.Position; Tok("shot" + s.Index + ":dissolved"); }
                }
                else if (s.WasFlying && s.LookTakenAge < 0 && s.LookGoneAge < 0) s.LookGoneAge = age;      // its own 6 s life ran out: nothing took it
                if (s.Rule != null && s.RuleTakenAge < 0 && s.Rule.InterceptedBy != 0) { s.RuleTakenAge = age; s.RuleTakenAt = s.Rule.Position; Tok("shot" + s.Index + ":rule-taken"); }
            }
        }

        // ---- hitme= ----

        static void DoHitMe(Job j, TrigSpec t, double age)
        {
            var pv = _wiring != null ? _wiring.Vol2Player : null;
            if (pv == null) pv = Object.FindFirstObjectByType<PlayerVitals>();
            if (pv == null) { TNote(age, "hitme: NO PlayerVitals in the scene - nothing was hit"); Problems.Add(j.Name + ": hitme: no PlayerVitals"); return; }
            string gateNote = "";
            var hub = _coord != null ? Vol2SeedExecutor.HubOf(_coord) : null;
            if (hub != null && (hub.Hits == null || !hub.Hits.Attached))
            {
                try { hub.AttachHits(pv); gateNote = " (the seed hub's hit gate was not attached to the player: the tool attached it)"; }
                catch (Exception e) { gateNote = " (attaching the hit gate failed: " + e.Message + ")"; }
            }
            long seen0 = hub != null ? hub.PlayerHitsSeen : -1;
            if (!_hpSaved) { _hpPlayer = pv; _hpSaved01 = pv.Hp01; _hpSaved = true; }
            float hp0 = pv.Hp01 * pv.MaxHp;
            var by = NearestBody();
            bool landed = pv.TakeAttackDamage(t.HitMeHp, IncomingDamageKind.Melee, by != null ? by.Vitals : null);
            float hp1 = pv.Hp01 * pv.MaxHp;
            TNote(age, "hitme " + t.HitMeHp.ToString("0.##", Inv) + " HP melee: landed=" + landed + ", HP " + hp0.ToString("0.#", Inv) + " -> " + hp1.ToString("0.#", Inv)
                + (hub != null ? ", seed hub hits seen " + seen0 + " -> " + hub.PlayerHitsSeen : ", no seed hub") + gateNote);
            Tok(landed ? "hitme" : "hitme-refused");
            if (_fakePlayer != null)
            {
                // fake=1 runs no rule, so the rules' recovery cannot end the prefab: end it the way the director would (cast ended, OnEnd Done) - the 걱 part reads that as the interrupt
                if (_fakeCast != null && _fakeCast.EndUs < 0) _fakeCast.EndUs = _coord.Services.Clock.NowUs;
                _fakePlayer.End(SpellFxEndReason.Done);
                TNote(age, "FAKE-INTERRUPT: the fake prefab got OnEnd(Done) (no rule ran)");
                Tok("fake-interrupt");
            }
        }

        // ---- recast= ----

        static void DoRecast(Job j, TrigSpec t, double age)
        {
            if (j.Fake || _coord == null || _wiring == null) { TNote(age, "recast: ignored (fake=1 runs no rule)"); return; }
            char letter = t.RecastLetter != '\0' ? t.RecastLetter : j.Letter;
            if (t.RecastVariant != null)
            {
                if (!SavedVariants.ContainsKey(letter)) SavedVariants[letter] = SpellFxSwitch.Get(letter);
                SpellFxSwitch.Set(letter, t.RecastVariant);
            }
            _shapes.PoolInk?.Refill();
            Vol2CastLog second = null; Vol2CastDecision dec = null;
            Action<Vol2CastLog> onBegan = l => { if (second == null) second = l; };
            Action<Vol2CastDecision> onDec = d => dec = d;
            _coord.CastBegan += onBegan; _coord.Services.Route.Adapter.Decided += onDec;
            try { _wiring.LetterChannel.Raise(new DrawnLetter(letter, default, default, null, 0.6f, 0.6f, 1f, 1f, 3)); }
            catch (Exception e) { Problems.Add(j.Name + ": recast " + letter + ": raising the letter: " + e.Message); }
            finally { _coord.CastBegan -= onBegan; _coord.Services.Route.Adapter.Decided -= onDec; }
            _recastLog = second; _recastDecision = dec; _recastLetter = letter;
            if (second != null) { TNote(age, "recast " + letter + ": cast #" + second.Cast.Value + " began (" + second.Glyph.Verb + " / " + second.Shape + ")"); Tok("recast:" + letter); }
            else { TNote(age, "recast " + letter + ": NO CAST (" + (dec != null ? "accepted=" + dec.Accepted + " reason=" + dec.Reason : "no decision") + ")"); Tok("recast:none"); }
        }

        // ---- swinghit= ----

        static void DoSwingHit(Job j, TrigSpec t, double age, long now)
        {
            if (j.Fake || _coord == null) { TNote(age, "swinghit: ignored (fake=1 runs no rule)"); return; }
            var body = NearestBody();
            if (body == null) { TNote(age, "swinghit: no live body to hit"); Problems.Add(j.Name + ": swinghit: no live body"); return; }
            Vector3 bp = body.Go.transform.position;
            float dist = Flat(bp - _origin).magnitude, reach = _coord.Tuning.BrushReach, rad = _coord.Tuning.SwingRadius;
            Vector3 sleeve = _origin + Vector3.up * 1.1f, tip = bp + Vector3.up * 1.1f;      // the rules judge on the plane (x, z): height only has to look sane
            string where = "nearest body at " + F(dist) + " m (brush reach " + F(reach) + " m + capsule " + F(rad) + " m + the body's radius)";
            var drv = _shapes != null ? _shapes.Swing : null;
            if (drv == null)
            {
                long swing = _coord.Services.Ids.NextSwing().Value;
                for (int k = 0; k < 3; k++) _shapes.Swings.Raise(new Vol2SwingEvent(swing, sleeve, tip, now, k));
                TNote(age, "swinghit [hand-raised swing signal: the scope has no swing driver] swing " + swing + ", " + where + (_beganLog != null ? " | cast swing reports " + _beganLog.SwingReports : ""));
                Tok("swing:signal");
                return;
            }
            _swingDrv = drv; _swingSwings0 = drv.Swings; _swingEvents0 = drv.RaisedEvents; _swingSawOpen = false; _swingWait = 0;
            if (t.SwingReal)
            {
                _swingRealGate = drv.Tuning.GateByCooldown; drv.Tuning.GateByCooldown = false; _swingRealRestore = true;
                bool avail = drv.IsAvailable, began = avail && drv.TryBeginSwing();
                if (!began)
                {
                    drv.Tuning.GateByCooldown = _swingRealGate; _swingRealRestore = false; _swingDrv = null;
                    TNote(age, "swinghit real: REFUSED (rig available=" + avail + ") - use script");
                    Problems.Add(j.Name + ": swinghit real: the driver refused the click (available=" + avail + ")");
                    return;
                }
                TNote(age, "swinghit real: the great brush's own swing began; " + where + (dist > reach + rad + 0.5f ? " - BEYOND the reach: it will miss" : ""));
            }
            else
            {
                _swingRig = SwingPassRig.Begin(_shapes, _coord.Services);
                _swingRig.Click(sleeve, tip);
                TNote(age, "swinghit script: scripted hand, sleeve at the chest, tip on the body; " + where + (dist > reach ? " (farther than the real brush reaches: a lab hand)" : ""));
            }
            _swingWatch = true;
            Tok("swing:begin");
        }

        static void WatchSwing(double age)
        {
            if (!_swingWatch || _swingDrv == null) return;
            if (_swingDrv.InSwing) { _swingSawOpen = true; return; }
            if (!_swingSawOpen && ++_swingWait < 90) return;          // a real swing opens on a later frame
            _swingWatch = false;
            var drv = _swingDrv; var last = drv.Last;
            bool mine = drv.Swings > _swingSwings0;
            string rec = _swingRig != null ? " | recorder: hits " + _swingRig.Recorder.Hits.Count + " misses " + _swingRig.Recorder.Misses.Count : "";
            TNote(age, "swing closed: " + (mine ? last.ToString() : "NO SWING OPENED") + " | driver: swings " + (drv.Swings - _swingSwings0) + ", events raised " + (drv.RaisedEvents - _swingEvents0) + rec
                + (_beganLog != null ? " | cast: swing reports " + _beganLog.SwingReports + ", duplicates " + _beganLog.SwingDuplicates + ", contacts " + _beganLog.Contacts.Count : ""));
            Tok(mine ? (last.Hit ? "swing:hit" : "swing:miss") : "swing:none");
            if (_swingRig != null) { try { _swingRig.End(); } catch (Exception) { } _swingRig = null; }
            if (_swingRealRestore) { try { drv.Tuning.GateByCooldown = _swingRealGate; } catch (Exception) { } _swingRealRestore = false; }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------
        // the end: what reached the rules, and putting everything back
        // ------------------------------------------------------------------------------------------------------------------------------------------------

        static string TriggerSummary()
        {
            var j = _job;
            if (j == null || j.Trig == null || (!j.Trig.Any && TLog.Count == 0)) return "";
            var t = j.Trig; var sb = new StringBuilder(" | triggers:");
            try
            {
                foreach (var l in TLog) sb.Append(" [" + l + "]");
                if (_swingWatch) sb.Append(" [the swing was still open when the shot ended]");
                if (t.Shots2)
                {
                    sb.Append(" | shots (" + TShots.Count + " of " + t.ShotCount + " spawned):");
                    foreach (var s in TShots)
                    {
                        sb.Append(" shot" + s.Index + " @" + s.SpawnAge.ToString("0.00", Inv) + " s: look " + (s.LookTakenAge >= 0 ? "dissolved @" + s.LookTakenAge.ToString("0.00", Inv) + " s at along/right/h " + CastFrame(s.LookTakenAt) : s.LookGoneAge >= 0 ? "flew its 6 s unharmed (no part took it)" : s.Look != null ? "still flying" : "never flew"));
                        if (s.Rule != null || s.RuleTakenAge >= 0) sb.Append(", rule " + (s.RuleTakenAge >= 0 ? "TOOK it @" + s.RuleTakenAge.ToString("0.00", Inv) + " s at " + CastFrame(s.RuleTakenAt) : "did not take it"));
                        sb.Append(';');
                    }
                    var rep = ReturnEx() != null && _beganLog != null ? ReturnEx().ReportOf(_beganLog.Cast) : null;
                    if (rep != null) sb.Append(" Return report: intercepts " + rep.Intercepts.Count + " [" + string.Join(" ", rep.Intercepts.Select(i => i.Leg + "@" + ((i.AtUs - _beganLog.StartUs) / 1e6).ToString("0.00", Inv) + (i.Reflected ? " reflected" : i.Reason.Length > 0 ? " " + i.Reason : ""))) + "], reflected " + rep.Reflected + ", orbit revolutions " + rep.OrbitRevolutions);
                }
                if (t.HitMeAt >= 0f || t.RecastAt >= 0f || t.SwingAt >= 0f)
                {
                    var hub = _coord != null ? Vol2SeedExecutor.HubOf(_coord) : null;
                    if (hub != null && _beganLog != null)
                        sb.Append(" | seed hub: player hits seen " + hub.PlayerHitsSeen + ", owner hits seen " + hub.OwnerHitsSeen + ", gather contacts " + hub.GatherContactsSeen + ", records of the cast [" + string.Join(" ", hub.RecordsOf(_beganLog.Cast).Select(r => r.Kind + (r.Trigger.Length > 0 ? "/" + r.Trigger : ""))) + "]");
                }
                if (t.RecastAt >= 0f && _tRecastDone)
                {
                    if (_recastLog != null)
                        sb.Append(" | recast " + _recastLetter + " cast #" + _recastLog.Cast.Value + " " + _recastLog.Status + " contacts " + _recastLog.Contacts.Count + " [" + string.Join(" ", _recastLog.Contacts.Take(8).Select(c => c.Kind + (c.BudgetAccepted ? "" : "(refused " + c.BudgetReason + ")") + ":" + (c.TargetName ?? "").Replace(DummyName, "body"))) + "] notes [" + string.Join(" | ", _recastLog.Notes.Take(4)) + "]");
                    else sb.Append(" | recast " + _recastLetter + ": no cast (" + (_recastDecision != null ? "accepted=" + _recastDecision.Accepted + " " + _recastDecision.Reason : "no decision") + ")");
                }
                if (t.Props.Count > 0)
                {
                    sb.Append(" | props:");
                    foreach (var p in TProps.Where(q => q.Root))
                        sb.Append(" [" + p.Spec.x.ToString("0.##", Inv) + "/" + p.Spec.y.ToString("0.##", Inv) + " r" + p.Spec.z.ToString("0.##", Inv) + " " + (p.M == null ? "no material" : p.M.Id + " " + (p.M.Alive ? "alive rem " + p.M.Remaining.ToString(Inv) : "ended " + p.M.End) + " splits " + p.M.Splits) + "]");
                    sb.Append(" halves made " + TProps.Count(q => !q.Root));
                    var sx = SplitEx(); var data = sx != null && _beganLog != null ? sx.DataOf(_beganLog.Cast) : null;
                    if (data != null) sb.Append(", the cast's split records " + data.Splits.Count + (data.Splits.Count > 0 ? " [" + string.Join(" ", data.Splits.Select(s => s.Where)) + "]" : ""));
                }
                if (_tReactions > 0) sb.Append(" | reactions " + _tReactions + " [" + string.Join(" ; ", TReact) + "]");
            }
            catch (Exception e) { sb.Append(" (summary failed: " + e.GetType().Name + " " + e.Message + ")"); }
            return sb.ToString();
        }

        /// <summary>Puts back everything the trigger options changed for one job (also the way out of an aborted run).</summary>
        static void TrigRestore()
        {
            try
            {
                if (_coord != null)
                {
                    if (_onPropCreated != null) _coord.Materials.Created -= _onPropCreated;
                    if (_onReaction != null) _coord.Reactions.ReactionResolved -= _onReaction;
                }
            }
            catch (Exception) { }
            _onPropCreated = null; _onReaction = null;
            try { if (_swingRig != null) _swingRig.End(); } catch (Exception) { }
            _swingRig = null;
            try { if (_swingRealRestore && _swingDrv != null) _swingDrv.Tuning.GateByCooldown = _swingRealGate; } catch (Exception) { }
            _swingRealRestore = false; _swingDrv = null; _swingWatch = false;
            try { if (_hpSaved && _hpPlayer != null) _hpPlayer.Restore(_hpSaved01); } catch (Exception) { }
            _hpSaved = false; _hpPlayer = null;
            var ex = ReturnEx();
            foreach (var s in TShots)
                if (s.Rule != null) { try { if (ex != null) ex.Hub.Unregister(s.Rule); } catch (Exception) { } Object.DestroyImmediate(s.Rule.gameObject); }
            TShots.Clear();
            foreach (var p in TProps) { if (p.Go != null) Object.DestroyImmediate(p.Go); if (p.Mat != null) Object.DestroyImmediate(p.Mat); }
            TProps.Clear(); TPropCasts.Clear();
            _fakeCast = null;
        }
    }
}
