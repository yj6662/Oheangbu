using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.App
{
    // D308-150 V10-V11 free showcase: a routing recipe put in place of the legacy bridge's recipe of a template (P0 / F0 / P1). It plays the free showcase look
    // for ONE glyph of that template (나 / 고 / 각) and hands every other glyph, unchanged, to the recipe it replaced. The showcase look reads only the executor's
    // facts on the scope clock (launch = the emitter's StartUs, the predicted contact and its impact clock, the contacts, the materials' creation / end, the
    // cast's end); nothing is decided or delayed here (visuals never affect damage). It draws no vol.2 ink mesh (Write writes nothing).

    /// <summary>One cast's showcase look (Unity side). Lives from the cast's begin to the instance's retirement.</summary>
    public abstract class Vol2FsRun
    {
        protected Vol2VfxInstance Inst;
        protected Vol2FsKit Kit;
        protected GameObject Root;
        protected float LaunchRel;
        public string Note = "";

        public void Bind(Vol2VfxInstance inst, Vol2FsKit kit, Transform parent)
        {
            Inst = inst; Kit = kit;
            Root = new GameObject("Vol2FreeShowcase_" + inst.Log.Glyph.Letter);
            if (parent != null) Root.transform.SetParent(parent, false);
            Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);      // every piece is placed in world coordinates
            Root.transform.localScale = Vector3.one;
            LaunchRel = inst.Rel(Vol2LegacyRecipeBase.LaunchUs(inst));
        }

        protected float Now(long nowUs) => Inst.Rel(nowUs);
        protected Vector3 Up => Vector3.up;
        protected Vector3 Fwd => Inst.Forward;
        protected Vector3 Right => Inst.Right;
        protected float Ground => Inst.Origin.y;
        protected Vector3 CamPos { get { var c = Camera.main; return c != null ? c.transform.position : Inst.Origin - Inst.Forward * 3f + Vector3.up * 2f; } }

        /// <summary>The brush tip now (the great brush of the scene), else the caster's hand height ahead.</summary>
        protected Vector3 Tip()
        {
            var f = Inst.Model.BrushTip;
            var p = f != null ? f() : null;
            return p ?? Inst.Origin + Vector3.up * 1.3f + Inst.Forward * 0.6f + Inst.Right * 0.25f;
        }

        protected Transform TargetTransform(long id)
        {
            var c = Inst.Model.Coordinator;
            if (c == null || id == 0) return null;
            return c.Registry.Find(id) is Vol2EnemyTarget e && e.Vitals != null ? e.Vitals.transform : null;
        }

        protected IVol2Target Target(long id) { var c = Inst.Model.Coordinator; return c != null && id != 0 ? c.Registry.Find(id) : null; }

        /// <summary>The body's renderer bounds (centre, size) - the cage / wrap are fitted to it.</summary>
        protected static Bounds BodyBounds(Transform t, Vector3 feet, float radius)
        {
            var b = new Bounds(feet + Vector3.up * 0.6f, new Vector3(radius * 2f, 1.2f, radius * 2f));
            if (t == null) return b;
            bool any = false;
            // the body's own skinned meshes only (r1: a bear's full renderer bounds were ~5 m tall - the cage / snare grew into the sky)
            foreach (var r in t.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (r == null || !r.enabled) continue;
                var rb = r.bounds;
                // batch 6: a renderer whose bounds are not finite this frame is skipped (NaN passed the size tests below and poisoned every mesh built on it)
                if (!(rb.size.y <= 3f && rb.size.x <= 4f) || float.IsNaN(rb.center.x + rb.center.y + rb.center.z) || float.IsInfinity(rb.center.x + rb.center.y + rb.center.z)) continue;
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            // clamp to a believable body (a dummy / bear is 0.6 - 1.6 m tall, 0.3 - 1.0 m half width)
            Vector3 ext = b.extents;
            ext.y = Mathf.Clamp(ext.y, 0.3f, 0.8f); ext.x = Mathf.Clamp(ext.x, 0.3f, 1.0f); ext.z = Mathf.Clamp(ext.z, 0.3f, 1.0f);
            float bottom = Mathf.Max(feet.y, b.min.y);
            return new Bounds(new Vector3(b.center.x, bottom + ext.y, b.center.z), ext * 2f);
        }

        /// <summary>batch 2: the brush's grip now (the sleeve end of the shaft), else the caster's hand height.</summary>
        protected Vector3 Grip()
        {
            var f = Inst.Model.BrushGrip;
            var p = f != null ? f() : null;
            return p ?? Inst.Origin + Vector3.up * 1.1f + Inst.Forward * 0.3f + Inst.Right * 0.25f;
        }

        // batch 2 (2026-10-26): the batch 1 "a struck body was destroyed -> hide at once" rule for every look (batch 0 나 / 고 / 각 too). A look registers the
        // bodies it is drawn on; the recipe asks before every tick (the lab capture's teardown destroys the bodies but leaves the presenter's instances alive).
        readonly List<Transform> _watched = new List<Transform>();
        public bool BodyGone { get; private set; }
        protected void Watch(Transform t) { if (t != null && !_watched.Contains(t)) _watched.Add(t); }
        public bool CheckBodyGone()
        {
            if (BodyGone) return true;
            foreach (var w in _watched)
                if (w == null) { BodyGone = true; if (Root != null) Root.SetActive(false); Note += " | body gone"; return true; }
            return false;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------
        // 2026-10-27 common material pass (showcase120_plan.md step 2; the user's reference "팬텀 블레이드 제로의 vfx 효과를 참고해",
        // Docs/Research/VFX-REF-PHANTOM-BLADE-ZERO.md principles 2 / 3 / 5, TEST) for every showcase look, drawn by the recipe after the look's own Tick:
        //  * the swing: the brush tip's REAL recent path as a thin-to-wide crescent in the body's one accent colour + two lagged phantom afterimages
        //    (the ㅓ 번진다 five draw their own - OwnSwingTrail);
        //  * a short hit-stop: on a contact the look's own particles hold for ~75 ms (simulation speed only; no game time, no timeScale, no rule).
        // Presentation only: nothing here is read by a rule or delays anything.
        // ---------------------------------------------------------------------------------------------------------------------------------------------
        readonly List<(Vector3 P, float T)> _tipPath = new List<(Vector3 P, float T)>();
        Vol2FsStrips _swing, _phantom; Mesh _swingMesh, _phantomMesh; bool _swingBuilt;
        float _stopUntil = -1f;
        /// <summary>The look draws its own swing trail / phantoms (batch 4 ㅓ).</summary>
        protected virtual bool OwnSwingTrail => "건넌먼선언".IndexOf(Inst.Log.Glyph.Letter) >= 0;
        public const float SwingSpan = 0.22f, HitStopSeconds = 0.075f;

        /// <summary>The body's one accent colour (Phantom Blade principle 1: ink-dark base, one strong accent).</summary>
        public static Color Accent(Body b)
        {
            switch (b)
            {
                case Body.Fire: return new Color(1f, 0.4f, 0.12f);
                case Body.Metal: return new Color(1f, 0.86f, 0.55f);
                case Body.Wood: return new Color(0.35f, 0.95f, 0.55f);
                case Body.Water: return new Color(0.32f, 0.64f, 1f);
                default: return new Color(0.95f, 0.66f, 0.3f);
            }
        }

        /// <summary>A short hit-stop of this look's particles from now (t = the look's clock).</summary>
        protected void HitStop(float t, float seconds = HitStopSeconds)
        {
            if (Root == null) return;
            _stopUntil = Mathf.Max(_stopUntil, t + seconds);
            foreach (var ps in Root.GetComponentsInChildren<ParticleSystem>()) { var m = ps.main; m.simulationSpeed = 0.04f; }
        }

        /// <summary>Called by the recipe after <see cref="Tick"/> every frame.</summary>
        public void KitTick(long nowUs)
        {
            if (Root == null || BodyGone || !Root.activeSelf) return;
            float t = Now(nowUs);
            if (_stopUntil > 0f && t >= _stopUntil)
            {
                _stopUntil = -1f;
                foreach (var ps in Root.GetComponentsInChildren<ParticleSystem>()) { var m = ps.main; m.simulationSpeed = 1f; }
            }
            if (OwnSwingTrail || t > LaunchRel + 0.6f) { if (_swingMesh != null && _swingMesh.vertexCount > 0) { _swingMesh.Clear(); _phantomMesh.Clear(); } return; }
            _tipPath.Add((Tip(), t));
            if (_tipPath.Count > 90) _tipPath.RemoveAt(0);
            if (!_swingBuilt)
            {
                _swingBuilt = true;
                var acc = Accent(Inst.Log.Glyph.Body);
                _swing = new Vol2FsStrips(); _phantom = new Vol2FsStrips();
                Vol2Fs.MeshObject(Root.transform, "KitSwing", Vol2FsB4A.PhantomMat(Kit, acc, 3f), out _swingMesh);
                Vol2Fs.MeshObject(Root.transform, "KitPhantoms", Vol2FsB4A.PhantomMat(Kit, acc, 1.3f), out _phantomMesh);
            }
            // the crescent lives through the swing to just after the launch, then thins away
            float life = 1f - Vol2Fs.Smooth(LaunchRel + 0.12f, LaunchRel + 0.55f, t);
            var cam = CamPos;
            _swing.Clear(); _phantom.Clear();
            Vol2FsB4A.BladeTrail(_swing, _tipPath, t, SwingSpan, 0.24f, life, 0.37f, cam);      // r3: wider (r2 read as a hairline from the shoulder)
            Vol2FsB4A.Phantoms(_phantom, _tipPath, t, SwingSpan, 0.24f, life * 1.4f, cam);
            _swing.Write(_swingMesh); _phantom.Write(_phantomMesh);
        }

        public virtual void Began(long nowUs) { }
        public virtual void Contact(Vol2ContactRecord rec) { }
        public virtual void MaterialCreated(Vol2Material m) { }
        public virtual void MaterialEnded(Vol2Material m, Vol2MaterialEnd why) { }
        /// <summary>batch 3: the registry changed this cast's material (what = drawn | split | moved | extended | thickened | blocked | wet).</summary>
        public virtual void MaterialChanged(Vol2Material m, string what) { }
        /// <summary>batch 2 (ㅓ): a successful parry landed while this cast's window was open (the combat wiring's ParryResolved, relayed by the scope). true = drawn.</summary>
        public virtual bool Parried(Vector3 point, long nowUs) => false;
        public abstract void Tick(long nowUs);
        public abstract bool Done(long nowUs);

        public void Dispose()
        {
            if (Root != null)
            {
                // the per-cast meshes (Vol2Fs.MeshObject) are not owned by the GameObject: free them with it
                // batch 1: the kit's shared meshes (rock / slab / sword / blade props) stay - the kit owns and frees them
                foreach (var f in Root.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null && (Kit == null || !Kit.Owns(f.sharedMesh))) Object.Destroy(f.sharedMesh);
                Object.Destroy(Root);
            }
            Root = null;
        }

        /// <summary>true while any particle system under the root still shows particles.</summary>
        protected bool ParticlesAlive()
        {
            if (Root == null) return false;
            foreach (var ps in Root.GetComponentsInChildren<ParticleSystem>()) if (ps.IsAlive(false)) return true;
            return false;
        }
    }

    /// <summary>The routing recipe: the glyphs in <see cref="Glyphs"/> play their own showcase look (batch 1, 2026-10-26: one template entry dispatches by
    /// the cast's glyph - P0 나 가 마 사 아, F0 고 노 모 소 오), every other glyph goes to <see cref="Inner"/>.</summary>
    public sealed class Vol2FreeShowcaseRecipe : Vol2VfxRecipe
    {
        public readonly Vol2VfxRecipe Inner;
        readonly IReadOnlyDictionary<char, Func<Vol2FsRun>> _makers;
        readonly Func<Vol2FsKit> _kit;
        readonly Func<bool> _on;
        readonly Transform _parent;
        readonly Dictionary<Vol2VfxInstance, Vol2FsRun> _runs = new Dictionary<Vol2VfxInstance, Vol2FsRun>();
        readonly Dictionary<char, int> _shownBy = new Dictionary<char, int>();
        /// <summary>The glyphs this entry shows (in registration order).</summary>
        public readonly string Glyphs;
        /// <summary>Casts the showcase took (bookkeeping, read by the command / ledger).</summary>
        public int Shown { get; private set; }
        public int ShownOf(char glyph) => _shownBy.TryGetValue(glyph, out var n) ? n : 0;
        public string LastNote = "";
        /// <summary>batch 4: the last note of each glyph (the status lists them, so every glyph's facts can be read, not only the template's last one).</summary>
        public readonly Dictionary<char, string> Notes = new Dictionary<char, string>();

        public Vol2FreeShowcaseRecipe(Vol2VfxRecipe inner, IReadOnlyDictionary<char, Func<Vol2FsRun>> makers, string order, Func<Vol2FsKit> kit, Func<bool> on, Transform parent)
        { Inner = inner; _makers = makers; Glyphs = order; _kit = kit; _on = on; _parent = parent; }

        public override Vol2Recipe Kind => Inner.Kind;

        bool Mine(Vol2VfxInstance inst) => _runs.ContainsKey(inst);

        public override void OnBegan(Vol2VfxInstance inst)
        {
            var kit = _kit();
            char letter = inst.Log.Glyph.Letter;
            if (!Application.isPlaying || !_makers.TryGetValue(letter, out var make) || !_on() || kit == null || !kit.Ok) { Inner.OnBegan(inst); return; }
            var run = make();
            _shownBy[letter] = ShownOf(letter) + 1;
            run.Bind(inst, kit, _parent);
            _runs[inst] = run;
            Shown++;
            // the great brush's show swing of the bridge (presentation only, never a hit): the old category's motion, its apex at the launch
            var legacy = inst.Model.Legacy;
            if (legacy != null)
            {
                float toApex = Mathf.Max(Vol2LegacyRecipeBase.MinToApex, (float)((Vol2LegacyRecipeBase.LaunchUs(inst) - inst.StartUs) / 1000000.0));
                // batch 2: the guard (ㅓ) and the ward (ㅜ) motions of the bridge, from the clip's start (as Vol2LegacyRecipeBase plays them)
                var frame = inst.Log.Glyph.Frame;
                // batch 4: the ㅓ 번진다 dresses the caster for its 6 s attachment and swings like 남긴다 (Self), as the bridge's own LegacyKind for an attachment
                // batch 5: the ㅓ 심는다 plants its seed on the caster's own body (no parry window): Self too
                // batch 6: the ㅓ 가른다 puts a 6 s blade on the caster (its swings cut; no parry window): Self too
                // batch 7: the ㅓ 돌아온다 circles the caster for 6 s (an orbit, no parry window): Self too (the bridge's own LegacyKind for an attachment verb)
                var kind = frame == Frame.Fan ? Vol2LegacyKind.Area : frame == Frame.Attachment && (inst.Log.Glyph.Verb == Verb.Leave || inst.Log.Glyph.Verb == Verb.Spread || inst.Log.Glyph.Verb == Verb.Seed || inst.Log.Glyph.Verb == Verb.Split || inst.Log.Glyph.Verb == Verb.Return) ? Vol2LegacyKind.Self : frame == Frame.Attachment ? Vol2LegacyKind.Guard : frame == Frame.Ring ? Vol2LegacyKind.Ward : Vol2LegacyKind.Single;
                legacy.Swing(new Vol2LegacySwing
                {
                    Letter = inst.Log.Glyph.Letter, Body = inst.Log.Glyph.Body, Kind = kind,
                    ToApex = toApex, FromClipStart = kind == Vol2LegacyKind.Guard || kind == Vol2LegacyKind.Ward || kind == Vol2LegacyKind.Self, Distance = kind == Vol2LegacyKind.Guard || kind == Vol2LegacyKind.Ward || kind == Vol2LegacyKind.Self ? 0f : 4.6f,
                });
            }
            try { run.Began(inst.StartUs); } catch (Exception e) { Debug.LogException(e); }
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        { if (_runs.TryGetValue(inst, out var r)) { try { r.Contact(rec); } catch (Exception e) { Debug.LogException(e); } } else Inner.OnContact(inst, rec); }

        public override void OnMaterialCreated(Vol2VfxInstance inst, Vol2Material m)
        { if (_runs.TryGetValue(inst, out var r)) { try { r.MaterialCreated(m); } catch (Exception e) { Debug.LogException(e); } } else Inner.OnMaterialCreated(inst, m); }

        public override void OnMaterialChanged(Vol2VfxInstance inst, Vol2Material m, string what)
        { if (_runs.TryGetValue(inst, out var r)) { try { r.MaterialChanged(m, what); } catch (Exception e) { Debug.LogException(e); } } else Inner.OnMaterialChanged(inst, m, what); }      // batch 3: forwarded (drawn / extended / moved ...)

        public override void OnMaterialEnded(Vol2VfxInstance inst, Vol2Material m, Vol2MaterialEnd why)
        { if (_runs.TryGetValue(inst, out var r)) { try { r.MaterialEnded(m, why); } catch (Exception e) { Debug.LogException(e); } } else Inner.OnMaterialEnded(inst, m, why); }

        public override bool OnParried(Vol2VfxInstance inst, Vector3 point, long nowUs)
        {
            if (!_runs.TryGetValue(inst, out var r)) return Inner.OnParried(inst, point, nowUs);
            try { return r.Parried(point, nowUs); } catch (Exception e) { Debug.LogException(e); return false; }
        }

        public override void Update(Vol2VfxInstance inst, long nowUs)
        {
            if (_runs.TryGetValue(inst, out var r))
            {
                if (r.CheckBodyGone()) { LastNote = inst.Log.Glyph.Letter + ": " + r.Note; return; }
                try { r.Tick(nowUs); LastNote = inst.Log.Glyph.Letter + ": " + r.Note; Notes[inst.Log.Glyph.Letter] = r.Note; } catch (Exception e) { Debug.LogException(e); }
                try { r.KitTick(nowUs); } catch (Exception e) { Debug.LogException(e); }      // 2026-10-27: swing crescent + phantoms, hit-stop
            }
            else Inner.Update(inst, nowUs);
        }

        public override void OnRetired(Vol2VfxInstance inst, bool aborted)
        {
            if (_runs.TryGetValue(inst, out var r)) { _runs.Remove(inst); r.Dispose(); }
            else Inner.OnRetired(inst, aborted);
        }

        public override bool IsFinished(Vol2VfxInstance inst, long nowUs)
        {
            if (!_runs.TryGetValue(inst, out var r)) return Inner.IsFinished(inst, nowUs);
            if (r.BodyGone) return true;
            if (!inst.CastIsEnded && !inst.Log.Finished) return false;
            try { return r.Done(nowUs); } catch (Exception e) { Debug.LogException(e); return true; }
        }

        /// <summary>Drops every showcase look at once (the installer is going away).</summary>
        public void ClearAll() { foreach (var r in _runs.Values) r.Dispose(); _runs.Clear(); }
    }
}
