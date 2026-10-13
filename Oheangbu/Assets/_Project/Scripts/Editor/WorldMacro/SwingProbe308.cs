using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Core.Events;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // SPEC-SPELL-SWING-308 Play probe (editor only, isolated store only). Turns the swing rule on for the running Play with an
    // in-memory profile (nothing is written: the game's default stays "no profile"), injects letters the way SpellSmoke308 does
    // (the letter channel) but only when the real drawing gate is open, and records release / launch / fizzle / damage clocks.
    //   on | off | cast:<letter> | burst:<letter>:<count>:<intervalSeconds> | dodge | ink | trail | report | reset
    public static class SwingProbe308
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly List<string> lines = new List<string>();
        static CombatLoopWiring hooked;
        static SpellSwingProfile308 profile;
        static char burstLetter; static int burstLeft, burstTried, burstOpen; static float burstEvery, burstNext;
        static bool ticking;

        public static string Run(string command)
        {
            try { return Dispatch((command ?? "").Trim()); }
            catch (Exception e) { return "probe exception: " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n')[0]; }
        }

        static string T => Time.time.ToString("F3", Inv);
        static void Line(string text) { lines.Add(T + " " + text); }

        static string Dispatch(string command)
        {
            if (command == "report") return lines.Count == 0 ? "no lines" : string.Join("\n", lines);
            if (command == "reset") { lines.Clear(); return "reset"; }
            if (!EditorApplication.isPlaying) return "refused: Play only";
            var s = Harness303.Session; if (s == null || !s.InitializationComplete || s.Walker == null || s.Walker.Wiring == null) return "refused: no ready session";
            if (string.IsNullOrEmpty(s.TestSaveSuffix) || !s.TestSaveSuffix.StartsWith("_", StringComparison.Ordinal)) return "refused: this session is on the real slot (no suffix)";
            var walker = s.Walker; var wiring = walker.Wiring;
            string head = command.Split(':')[0], rest = command.Contains(":") ? command.Substring(command.IndexOf(':') + 1) : "";
            switch (head)
            {
                case "on": return On(wiring);
                case "off": return Off(wiring);
                case "cast": return Letter(rest, out char one) ? Cast(walker, one) : "refused: cast:<letter|uXXXX>";
                case "castdodge":
                {
                    var a = rest.Split(':'); if (a.Length != 2 || !Letter(a[0], out char l)) return "refused: castdodge:<letter|uXXXX>:<delay>";
                    string reply = Cast(walker, l); dodgeAt = Time.time + float.Parse(a[1], Inv); Tick(true); return reply + " | dodge in " + a[1] + " s";
                }
                case "burst":
                {
                    var a = rest.Split(':'); if (a.Length != 3 || !Letter(a[0], out char b)) return "refused: burst:<letter|uXXXX>:<count>:<interval>";
                    burstLetter = b; burstLeft = int.Parse(a[1], Inv); burstEvery = float.Parse(a[2], Inv); burstNext = Time.time; burstTried = burstOpen = 0;
                    Tick(true); return "burst started " + burstLetter + " x" + burstLeft + " every " + burstEvery.ToString("F2", Inv) + " s";
                }
                case "dodge": return Dodge(walker);
                case "ink": { var ink = Harness303.Field<InkPool>(wiring, "_ink"); return "ink " + (ink != null ? ink.Value.ToString("F3", Inv) : "?") + " | locked " + wiring.SwingLocked308 + " pending " + wiring.SwingPending308; }
                case "brush": return Brush(walker);
                case "near": return Near(walker);
                case "nearset": return NearSet(rest);
                case "brushlog": watchUntil = Time.time + float.Parse(rest, Inv); watchNext = 0f; Tick(true); return "brush watch for " + rest + " s";
                case "trail":
                {
                    var trail = walker.Body != null ? walker.Body.GetComponentInChildren<BrushInkTrail308>(true) : null;
                    if (trail == null) trail = UnityEngine.Object.FindFirstObjectByType<BrushInkTrail308>();
                    return trail == null ? "trail: no component" : "trail: enabled " + trail.isActiveAndEnabled + " emitting " + trail.Emitting + " points " + trail.PointCount;
                }
            }
            return "SwingProbe308: on | off | cast:<letter> | burst:<letter>:<n>:<interval> | dodge | ink | trail | report | reset";
        }

        // the close-up (first person) brush in the draw mode: bristle press / splay, and how far the ink ribbon's head is from its tip
        static string Near(WorldMacroCombatWalker walker)
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>(); if (rig == null) return "near: no rig";
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            object brush = typeof(WorldMacroPlayerGestureRig).GetField("_nearBrush", Any)?.GetValue(rig);
            bool visible = (bool)(typeof(WorldMacroPlayerGestureRig).GetField("_nearVisible", Any)?.GetValue(rig) ?? false);
            string text = "near: visible " + visible + " drawing " + (walker.Drawing != null && walker.Drawing.InDrawMode) + " stroking " + (walker.Drawing != null && walker.Drawing.IsStroking);
            if (brush != null)
            {
                var great = brush.GetType().GetField("Great", Any)?.GetValue(brush) as GreatBrushBristles308; var tip = brush.GetType().GetField("Tip", Any)?.GetValue(brush) as Transform;
                text += great == null ? " | no great bristles on the close-up brush" : " | bound " + great.IsBound + " press " + great.Press.ToString("F2", Inv) + " shapes splay " + great.HasSplayShape + " press " + great.HasPressShape + (great.BindingError != null ? " error " + great.BindingError : "");
                var trail = UnityEngine.Object.FindFirstObjectByType<BrushInkTrail308>(); var line = trail != null ? GameObject.Find("BrushInkTrail308") : null;
                if (tip != null) text += " | tip to effect tip " + ((tip.position - rig.EffectTip.position).magnitude * 100f).ToString("F1", Inv) + " cm" + (line != null ? " | tip to ribbon head " + ((tip.position - line.transform.position).magnitude * 100f).ToString("F1", Inv) + " cm" : "") + " | tip to camera " + (Camera.main != null ? (tip.position - Camera.main.transform.position).magnitude.ToString("F2", Inv) : "?") + " m";
            }
            return text;
        }
        // Play only, memory only: a close-up number of the great brush profile for this Play (restore puts the first values back).
        //   nearset:<Field>=<float | x,y,z>[;<Field>=...]   |   nearset:restore
        static readonly Dictionary<string, object> nearOriginal = new Dictionary<string, object>();
        static string NearSet(string text)
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>(); var swing = UnityEngine.Object.FindFirstObjectByType<SpellSwing308>();
            if (rig == null || swing == null || swing.Profile == null) return "nearset: no rig / profile";
            var profile = swing.Profile; var type = profile.GetType(); var reply = new System.Text.StringBuilder("nearset:");
            if (text == "restore") { foreach (var pair in nearOriginal) type.GetField(pair.Key).SetValue(profile, pair.Value); reply.Append(" restored " + nearOriginal.Count); nearOriginal.Clear(); }
            else foreach (string part in text.Split(';'))
            {
                var kv = part.Split('='); if (kv.Length != 2) return "refused: " + part;
                var field = type.GetField(kv[0]); if (field == null || !kv[0].StartsWith("Near", StringComparison.Ordinal)) return "refused: no close-up field " + kv[0];
                if (!nearOriginal.ContainsKey(kv[0])) nearOriginal[kv[0]] = field.GetValue(profile);
                var n = kv[1].Split(',');
                if (field.FieldType == typeof(float)) field.SetValue(profile, float.Parse(n[0], Inv));
                else if (field.FieldType == typeof(bool)) field.SetValue(profile, n[0] == "1" || n[0] == "true");
                else if (field.FieldType == typeof(Vector3) && n.Length == 3) field.SetValue(profile, new Vector3(float.Parse(n[0], Inv), float.Parse(n[1], Inv), float.Parse(n[2], Inv)));
                else if (field.FieldType == typeof(Quaternion) && n.Length == 3) field.SetValue(profile, Quaternion.Euler(float.Parse(n[0], Inv), float.Parse(n[1], Inv), float.Parse(n[2], Inv)));
                else return "refused: " + kv[0] + " is " + field.FieldType.Name;
                reply.Append(" " + kv[0] + "=" + field.GetValue(profile));
            }
            bool bound = rig.RefreshGreatBrush308();
            return reply + " | rebound " + bound + " great " + rig.GreatBrushBound308 + (rig.NearGripError308 != null ? " near grip: " + rig.NearGripError308 : "");
        }
        static float watchUntil, watchNext; static string watchLast;
        static string Brush(WorldMacroCombatWalker walker)
        {
            var swing = walker.Body != null ? walker.Body.GetComponentInChildren<SpellSwing308>(true) : null;
            if (swing == null) swing = UnityEngine.Object.FindFirstObjectByType<SpellSwing308>();
            if (swing == null) return "brush: no SpellSwing308";
            string text = "brush: active " + swing.Active + " stance " + swing.Stance + " blend " + swing.StanceBlend.ToString("F2", Inv) + " state " + (swing.State ?? "-") + " t " + swing.ClipSeconds.ToString("F2", Inv) +
                          " upper " + swing.UpperWeight.ToString("F2", Inv) + " body " + swing.BodyWeight.ToString("F2", Inv) + " left " + swing.LeftHandOnShaft.ToString("F2", Inv) + " swings " + swing.Swings + " cuts " + swing.Cuts + " back " + swing.BackBlend.ToString("F2", Inv) + " sprint " + (walker.Motor != null && walker.Motor.IsSprinting);
            var animator = swing.GetComponent<Animator>(); var rig = swing.GetComponent<WorldMacroPlayerGestureRig>();
            if (animator != null && rig != null && rig.WorldTip != null && swing.Profile != null)
            {
                Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand), right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform root = rig.WorldTip; while (root.parent != null && !root.name.StartsWith("C02_WorldBrush", StringComparison.Ordinal)) root = root.parent;
                Transform socketL = Deep(root, "GripSocketL"), socket = Deep(root, "GripSocket");
                if (hand != null && socketL != null) text += " | left hand to GripSocketL " + ((hand.TransformPoint(swing.Profile.LeftHandHoldLocal) - socketL.position).magnitude * 100f).ToString("F1", Inv) + " cm";
                if (right != null && socket != null) text += " | right hand to GripSocket " + ((right.position - socket.position).magnitude * 100f).ToString("F1", Inv) + " cm";
                text += " | tip y " + (rig.WorldTip.position.y - swing.transform.position.y).ToString("F2", Inv) + " brush " + root.name + " shown " + root.gameObject.activeInHierarchy;
            }
            return text;
        }
        static Transform Deep(Transform root, string name)
        {
            if (root == null) return null; if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++) { var found = Deep(root.GetChild(i), name); if (found != null) return found; }
            return null;
        }
        static float dodgeAt = float.PositiveInfinity;
        static bool Letter(string text, out char letter)
        {
            letter = ' ';
            if (text.Length == 1) { letter = text[0]; return true; }
            if (text.Length == 5 && (text[0] == 'u' || text[0] == 'U') && int.TryParse(text.Substring(1), NumberStyles.HexNumber, Inv, out int code)) { letter = (char)code; return true; }
            return false;
        }

        static FieldInfo F(string name) => typeof(CombatLoopWiring).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

        static string On(CombatLoopWiring wiring)
        {
            if (profile == null) { profile = ScriptableObject.CreateInstance<SpellSwingProfile308>(); profile.hideFlags = HideFlags.HideAndDontSave; }
            F("_swing308").SetValue(wiring, profile); F("_swingLooked").SetValue(wiring, true);
            Hook(wiring);
            return "swing profile on (memory only) | single " + profile.AttackSingle.ApexSeconds.ToString("F2", Inv) + "/" + profile.AttackSingle.LockSeconds.ToString("F2", Inv) +
                   " area " + profile.AttackArea.ApexSeconds.ToString("F2", Inv) + "/" + profile.AttackArea.LockSeconds.ToString("F2", Inv) + " parry lock " + profile.Parry.LockSeconds.ToString("F2", Inv);
        }

        static string Off(CombatLoopWiring wiring)
        {
            F("_swing308").SetValue(wiring, null); F("_swingLooked").SetValue(wiring, true);
            Hook(wiring);
            return "swing profile off";
        }

        static void Hook(CombatLoopWiring wiring)
        {
            if (hooked == wiring) return;
            Unhook(); hooked = wiring;
            wiring.SwingBegan308 += OnBegan; wiring.SwingLaunched308 += OnLaunched; wiring.SwingFizzled308 += OnFizzled;
            wiring.CastAccepted += OnAccepted; wiring.EnemyDamageResolved += OnDamage;
        }
        static void Unhook()
        {
            if (hooked == null) return;
            hooked.SwingBegan308 -= OnBegan; hooked.SwingLaunched308 -= OnLaunched; hooked.SwingFizzled308 -= OnFizzled;
            hooked.CastAccepted -= OnAccepted; hooked.EnemyDamageResolved -= OnDamage;
            hooked = null;
        }
        static void OnBegan(SpellCast cast, string state, float apex, float until) => Line("BEGAN " + cast.Letter + " kind " + cast.Kind + " state " + state + " apex +" + (apex - Time.time).ToString("F3", Inv) + " lock +" + (until - Time.time).ToString("F3", Inv) + " power " + cast.Power.ToString("F4", Inv));
        static void OnLaunched(SpellCast cast, float after) => Line("LAUNCHED " + cast.Letter + " after " + after.ToString("F3", Inv) + " power " + cast.Power.ToString("F4", Inv));
        static void OnFizzled(SpellCast cast) => Line("FIZZLED " + cast.Letter);
        static void OnAccepted(SpellCast cast, Vector3 origin, Vector3 forward) => Line("ACCEPTED " + cast.Letter + " power " + cast.Power.ToString("F4", Inv));
        static void OnDamage(EnemyDamageResult result) => Line("DAMAGE " + result);

        static string Cast(WorldMacroCombatWalker walker, char letter)
        {
            var wiring = walker.Wiring; Hook(wiring);
            bool gate = walker.Drawing == null || walker.Drawing.EntryAllowed == null || walker.Drawing.EntryAllowed();
            var ink = Harness303.Field<InkPool>(wiring, "_ink"); float before = ink != null ? ink.Value : -1f;
            if (!gate) { Line("GATE CLOSED " + letter + " (locked " + wiring.SwingLocked308 + ")"); return "gate closed | locked " + wiring.SwingLocked308; }
            if (!SpellGrammar308.TryDecompose(letter, out var glyph)) return "not a vocabulary glyph";
            Jamo? final = glyph.Final == SpellFinal.None ? (Jamo?)null : (Jamo)((int)glyph.Final - 1);
            var drawn = new DrawnLetter(letter, glyph.Initial, glyph.Medial, final, .8f, .75f, 1.2f, 1.4f, 2);
            var channel = Harness303.Field<DrawnLetterEventChannelSO>(wiring, "_letterDrawn");
            Line("RELEASE " + letter + " ink " + before.ToString("F3", Inv));
            channel.Raise(drawn);
            float after = ink != null ? ink.Value : -1f;
            return "released " + letter + " | ink " + before.ToString("F3", Inv) + " -> " + after.ToString("F3", Inv) + " | pending " + wiring.SwingPending308 + " locked " + wiring.SwingLocked308;
        }

        static string Dodge(WorldMacroCombatWalker walker)
        {
            var motor = walker.Motor; if (motor == null) return "no motor";
            var dodge = motor.GetComponent<DodgeAction>(); if (dodge == null) return "no DodgeAction";
            bool ok = dodge.TryDodge(-motor.transform.forward);
            Line("DODGE " + (ok ? "started" : "refused"));
            return "dodge " + (ok ? "started" : "refused") + " | dashing " + dodge.IsDashing;
        }

        static void Tick(bool on)
        {
            if (on == ticking) return;
            ticking = on;
            if (on) EditorApplication.update += Update; else EditorApplication.update -= Update;
        }

        static void Update()
        {
            if (!EditorApplication.isPlaying) { burstLeft = 0; dodgeAt = float.PositiveInfinity; Tick(false); return; }
            if (Time.time < watchUntil && Time.time >= watchNext)
            {
                watchNext = Time.time + .1f; var w = Harness303.Session;
                if (w != null && w.Walker != null) { string now = Brush(w.Walker); string key = now.Split('|')[0]; int cut = key.IndexOf(" t ", StringComparison.Ordinal); string head = cut > 0 ? key.Substring(0, cut) : key; if (head != watchLast) { watchLast = head; Line("BRUSH " + now); } }
            }
            if (Time.time >= dodgeAt) { dodgeAt = float.PositiveInfinity; var h = Harness303.Session; if (h != null && h.Walker != null) Dodge(h.Walker); }
            if (burstLeft <= 0) { if (burstTried > 0) Line("BURST END tried " + burstTried + " gate open " + burstOpen); burstTried = 0; if (float.IsPositiveInfinity(dodgeAt) && Time.time >= watchUntil) Tick(false); return; }
            if (Time.time < burstNext) return;
            burstNext = Time.time + burstEvery; burstLeft--; burstTried++;
            var s = Harness303.Session; if (s == null || s.Walker == null) return;
            string reply = Cast(s.Walker, burstLetter);
            if (reply.StartsWith("released", StringComparison.Ordinal)) burstOpen++;
        }
    }
}
