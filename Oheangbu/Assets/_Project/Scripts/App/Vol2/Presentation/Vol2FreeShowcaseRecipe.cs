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
                if (r == null || !r.enabled || r.bounds.size.y > 3f || r.bounds.size.x > 4f) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            // clamp to a believable body (a dummy / bear is 0.6 - 1.6 m tall, 0.3 - 1.0 m half width)
            Vector3 ext = b.extents;
            ext.y = Mathf.Clamp(ext.y, 0.3f, 0.8f); ext.x = Mathf.Clamp(ext.x, 0.3f, 1.0f); ext.z = Mathf.Clamp(ext.z, 0.3f, 1.0f);
            float bottom = Mathf.Max(feet.y, b.min.y);
            return new Bounds(new Vector3(b.center.x, bottom + ext.y, b.center.z), ext * 2f);
        }

        public virtual void Began(long nowUs) { }
        public virtual void Contact(Vol2ContactRecord rec) { }
        public virtual void MaterialCreated(Vol2Material m) { }
        public virtual void MaterialEnded(Vol2Material m, Vol2MaterialEnd why) { }
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
            inst.Dirty = false;                                   // no vol.2 ink mesh for a showcase cast
            // the great brush's show swing of the bridge (presentation only, never a hit): the old category's motion, its apex at the launch
            var legacy = inst.Model.Legacy;
            if (legacy != null)
            {
                float toApex = Mathf.Max(Vol2LegacyRecipeBase.MinToApex, (float)((Vol2LegacyRecipeBase.LaunchUs(inst) - inst.StartUs) / 1000000.0));
                legacy.Swing(new Vol2LegacySwing
                {
                    Letter = inst.Log.Glyph.Letter, Body = inst.Log.Glyph.Body, Kind = inst.Log.Glyph.Frame == Frame.Fan ? Vol2LegacyKind.Area : Vol2LegacyKind.Single,
                    ToApex = toApex, FromClipStart = false, Distance = 4.6f,
                });
            }
            try { run.Began(inst.StartUs); } catch (Exception e) { Debug.LogException(e); }
        }

        public override void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec)
        { if (_runs.TryGetValue(inst, out var r)) { try { r.Contact(rec); } catch (Exception e) { Debug.LogException(e); } } else Inner.OnContact(inst, rec); }

        public override void OnMaterialCreated(Vol2VfxInstance inst, Vol2Material m)
        { if (_runs.TryGetValue(inst, out var r)) { try { r.MaterialCreated(m); } catch (Exception e) { Debug.LogException(e); } } else Inner.OnMaterialCreated(inst, m); }

        public override void OnMaterialChanged(Vol2VfxInstance inst, Vol2Material m, string what)
        { if (!Mine(inst)) Inner.OnMaterialChanged(inst, m, what); }

        public override void OnMaterialEnded(Vol2VfxInstance inst, Vol2Material m, Vol2MaterialEnd why)
        { if (_runs.TryGetValue(inst, out var r)) { try { r.MaterialEnded(m, why); } catch (Exception e) { Debug.LogException(e); } } else Inner.OnMaterialEnded(inst, m, why); }

        public override bool OnParried(Vol2VfxInstance inst, Vector3 point, long nowUs) => Mine(inst) ? false : Inner.OnParried(inst, point, nowUs);

        public override void Update(Vol2VfxInstance inst, long nowUs)
        {
            if (_runs.TryGetValue(inst, out var r)) { inst.Dirty = false; try { r.Tick(nowUs); LastNote = inst.Log.Glyph.Letter + ": " + r.Note; } catch (Exception e) { Debug.LogException(e); } }
            else Inner.Update(inst, nowUs);
        }

        public override void OnRetired(Vol2VfxInstance inst, bool aborted)
        {
            if (_runs.TryGetValue(inst, out var r)) { _runs.Remove(inst); r.Dispose(); }
            else Inner.OnRetired(inst, aborted);
        }

        public override float Head(Vol2VfxInstance inst, long nowUs) => Mine(inst) ? inst.HeadAlong(nowUs) : Inner.Head(inst, nowUs);
        public override Vector3 Offset(Vol2VfxInstance inst, long nowUs) => Mine(inst) ? Vector3.zero : Inner.Offset(inst, nowUs);
        public override void Write(Vol2VfxInstance inst, Vol2InkWriter w) { if (!Mine(inst)) Inner.Write(inst, w); }

        public override bool IsFinished(Vol2VfxInstance inst, long nowUs)
        {
            if (!_runs.TryGetValue(inst, out var r)) return Inner.IsFinished(inst, nowUs);
            if (!inst.CastIsEnded && !inst.Log.Finished) return false;
            try { return r.Done(nowUs); } catch (Exception e) { Debug.LogException(e); return true; }
        }

        /// <summary>Drops every showcase look at once (the installer is going away).</summary>
        public void ClearAll() { foreach (var r in _runs.Values) r.Dispose(); _runs.Clear(); }
    }
}
