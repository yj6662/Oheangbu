using System;
using System.Collections.Generic;
using Oheangbu.App.SpellFx;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // S5-b slice 9a: the PURE half of the vol.2 presenter. It listens to the coordinator (cast begun, contact resolved, material created / changed / ended, cast ended) and keeps, per
    // cast, what is on screen: parts with timestamps taken from the executors (the scope clock). It owns NO lifetime: a part lives until the executor's event ends it (a material's
    // expiry or end reason, the path emitter finishing, the return report) and then runs its look-tail (plan B: <= 0.3 s) measured on the same clock from the event frame.
    // It makes no Unity objects, so an edit-mode fixture drives it with the hand clock of Vol2ShapeFixtureKit.Rig. The recipes registered today (the legacy bridge and the free
    // showcase) present each cast with their own Unity objects; the ink-mesh recipes of slice 9a / NB20 / stroke20 and their mesh view were removed on 2026-10-26
    // (D308-150 V5 - V8 rejected; Docs/Research/vfx120/cleanup_code_20261026.md).

    /// <summary>One drawn piece of an instance (a path stroke, a landing, a tree, a mark, a seed dot ...). <see cref="Kind"/> is the recipe's own number.</summary>
    public sealed class Vol2VfxPart
    {
        public int Kind;
        public long AppearUs;
        /// <summary>The executor's end event frame (scope clock), -1 while alive.</summary>
        public long EndUs = -1;
        public Vol2EndCode Code;
        /// <summary>> 0 = the look-tail length of this end (seconds); else the template's length for the end code.</summary>
        public float EndSeconds;
        public bool Gone;
        /// <summary>The look-tail of this part is over (set by the model's tick): it is no longer drawn.</summary>
        public bool RetiredFlag;
        public bool Dead => Gone || RetiredFlag;
        public Vol2Material Mat;
        public Vector3 Pos, Dir;
        public float F0, F1, Density = 1f;
        public int Seed;
        public long Aux;
        /// <summary>The executor's SCHEDULED end for a part with no material of its own (a strip's UntilUs, the latest mark's expiry): > 0 once known. The part is ended by the recipe at that moment.</summary>
        public long EndSchedule;
        /// <summary>Expiry warning window before <see cref="EndSchedule"/> (seconds, 0 = none).</summary>
        public float WarnSeconds;
        public bool Ended => EndUs >= 0;
    }

    /// <summary>Everything on screen for ONE cast.</summary>
    public sealed class Vol2VfxInstance
    {
        public Vol2VfxModel Model { get; }
        public Vol2CastLog Log { get; }
        public Vol2TemplateId TemplateId { get; }
        public Vol2VfxTemplate Template { get; }
        public Vol2BodyLook Look { get; }
        public Vol2VfxRecipe Recipe { get; }
        public long StartUs { get; }
        public Vector3 Origin { get; }
        public Vector3 Forward { get; }
        public Vector3 Right { get; }
        public float ChestY { get; }
        public readonly List<Vol2VfxPart> Parts = new List<Vol2VfxPart>(8);
        public bool Retired;
        public bool CastIsEnded;
        public long CastEndUs;
        public bool HasLanding;
        public Vector3 Landing;
        public string Note = "";

        public Vol2VfxInstance(Vol2VfxModel model, Vol2CastLog log, Vol2VfxTemplate template, Vol2BodyLook look, Vol2VfxRecipe recipe, float chestHeight)
        {
            Model = model; Log = log; Template = template; Look = look; Recipe = recipe;
            TemplateId = new Vol2TemplateId(log.Glyph.Frame, log.Glyph.Verb);
            StartUs = log.StartUs;
            var spec = log.Spec;
            Origin = spec.Origin; Forward = spec.Forward; Right = Vector3.Cross(Vector3.up, spec.Forward);
            ChestY = Origin.y + chestHeight;
        }

        public CastId Cast => Log.Cast;
        public Vol2PathEmitter Path => Log.Emitter as Vol2PathEmitter;
        public float Rel(long us) => (float)((us - StartUs) / 1000000.0);
        public float Age(long nowUs) => Rel(nowUs);

        public Vol2VfxPart AddPart(int kind, long appearUs)
        {
            var p = new Vol2VfxPart { Kind = kind, AppearUs = appearUs, Seed = Parts.Count + 1 };
            Parts.Add(p);
            return p;
        }

        public Vol2VfxPart FirstOf(int kind) { foreach (var p in Parts) if (p.Kind == kind) return p; return null; }
        public Vol2VfxPart PartOf(Vol2Material m) { foreach (var p in Parts) if (p.Mat == m) return p; return null; }

        public void EndPart(Vol2VfxPart p, long endUs, Vol2EndCode code, float seconds = 0f)
        {
            if (p == null || p.Ended) return;
            p.EndUs = endUs; p.Code = code; p.EndSeconds = seconds;
        }

        /// <summary>The path head in metres from the cast point at <paramref name="nowUs"/>: the emitter's own number once it stopped, else the pen speed of the spec (the emitter ticks once per frame).</summary>
        public float HeadAlong(long nowUs)
        {
            var p = Path; var spec = Log.Spec;
            if (p == null) return 0f;
            if (p.Finished) return p.HeadAlong;
            return Mathf.Min(spec.PathLength, spec.PathSpeed * Mathf.Max(0f, Rel(nowUs)));
        }

        /// <summary>true when this part's look-tail is over at <paramref name="nowUs"/> (or the lab reset it).</summary>
        public bool PartRetired(Vol2VfxPart p, long nowUs)
        {
            if (p.Gone) return true;
            if (!p.Ended) return false;
            float tail = p.EndSeconds > 0f ? p.EndSeconds : Template.EndSeconds(p.Code);
            return nowUs >= p.EndUs + Vol2Time.FromSeconds(tail) + 20000;
        }
    }

    /// <summary>The presentation of one template. The model calls the On* hooks from coordinator events and <see cref="Update"/> once per tick; a recipe shows the cast with its own Unity objects.</summary>
    public abstract class Vol2VfxRecipe
    {
        public abstract Vol2Recipe Kind { get; }
        public virtual void OnBegan(Vol2VfxInstance inst) { }
        public virtual void OnContact(Vol2VfxInstance inst, Vol2ContactRecord rec) { }
        public virtual void OnMaterialCreated(Vol2VfxInstance inst, Vol2Material m) { }
        public virtual void OnMaterialChanged(Vol2VfxInstance inst, Vol2Material m, string what) { }
        public virtual void OnMaterialEnded(Vol2VfxInstance inst, Vol2Material m, Vol2MaterialEnd why)
        {
            var p = inst.PartOf(m);
            if (p != null) inst.EndPart(p, m.EndedUs, Vol2EndCodes.From(why));
        }
        /// <summary>A successful parry landed while this cast's window was open (the combat wiring's ParryResolved, relayed by the scope on the scope clock).</summary>
        public virtual bool OnParried(Vol2VfxInstance inst, Vector3 point, long nowUs) => false;
        public virtual void Update(Vol2VfxInstance inst, long nowUs) { }
        /// <summary>D308-150 V9: the instance left the model (its look is over, the lab reset aborted it, or the model was disposed). The legacy bridge lets go of its
        /// Unity body here.</summary>
        public virtual void OnRetired(Vol2VfxInstance inst, bool aborted) { }
        /// <summary>Is everything of this instance over? Default: the cast ended and every part's tail is over.</summary>
        public virtual bool IsFinished(Vol2VfxInstance inst, long nowUs)
        {
            if (!inst.CastIsEnded && !inst.Log.Finished) return false;
            foreach (var p in inst.Parts) if (!inst.PartRetired(p, nowUs)) return false;
            return true;
        }
    }

    /// <summary>The single list of recipes: ADD A RECIPE HERE (one line each), exactly like Vol2VerbRegistry.</summary>
    public sealed class Vol2VfxRecipeRegistry
    {
        readonly Dictionary<Vol2Recipe, Vol2VfxRecipe> _by = new Dictionary<Vol2Recipe, Vol2VfxRecipe>();
        public Vol2VfxRecipeRegistry Add(Vol2VfxRecipe r)
        {
            if (_by.ContainsKey(r.Kind)) throw new InvalidOperationException("recipe " + r.Kind + " already registered");
            _by.Add(r.Kind, r); return this;
        }
        /// <summary>Puts <paramref name="r"/> in place of the recipe of its kind (a capture or a fixture comparing two looks of one template; the game never calls it).</summary>
        public Vol2VfxRecipeRegistry Set(Vol2VfxRecipe r) { _by[r.Kind] = r; return this; }
        public bool TryGet(Vol2Recipe kind, out Vol2VfxRecipe r) => _by.TryGetValue(kind, out r);
        public int Count => _by.Count;

        public static Vol2VfxRecipeRegistry CreateDefault()
        {
            var reg = new Vol2VfxRecipeRegistry();
            // D308-150 V9 (2026-10-25, the user: "기존 투사체 방식이 전부 낫다"): the 20 no-batchim spells P0 / A0 / F0 / R0 play the OLD main game's visuals of each glyph
            // through the legacy bridge (Vol2LegacyRecipes + Vol2LegacyStage). The rejected ink looks (slice 9a V5, NB20 V6, stroke20 V7 / V8) were removed on 2026-10-26.
            // Step 4: the 100 batchim spells (P1 - P5, A1 - A5, F1 - F5, R1 - R5) play the old glyph bodies + the verb states as old parts (Vol2LegacyVerbRecipes)
            // (the free showcase wraps these recipes per glyph: Vol2FreeShowcaseInstaller)
            reg.Add(new Vol2LegacyPathRecipe());        // P0 (legacy bridge)
            reg.Add(new Vol2LegacyAttachRecipe());      // A0 (legacy bridge)
            reg.Add(new Vol2LegacyFanRecipe());         // F0 (legacy bridge)
            reg.Add(new Vol2LegacyRingRecipe());        // R0 (legacy bridge)
            foreach (var frame in new[] { Frame.Path, Frame.Attachment, Frame.Fan, Frame.Ring })
                for (int v = 1; v <= 5; v++) reg.Add(new Vol2LegacyVerbRecipe(frame, (Verb)v));      // P1 - R5 (legacy bridge, step 4)
            return reg;
        }
    }

    public sealed class Vol2VfxModel : IDisposable
    {
        readonly Vol2CastCoordinator _c;
        readonly Dictionary<long, Vol2VfxInstance> _byCast = new Dictionary<long, Vol2VfxInstance>();
        readonly List<Vol2VfxInstance> _list = new List<Vol2VfxInstance>();
        bool _disposed;

        public Vol2VfxCatalogue Catalogue { get; }
        public Vol2VfxRecipeRegistry Recipes { get; }
        public Vol2CastCoordinator Coordinator => _c;
        public IReadOnlyList<Vol2VfxInstance> Instances => _list;
        public event Action<Vol2VfxInstance> Spawned, Retired;
        public int SpawnedCount { get; private set; }
        public int RetiredCount { get; private set; }
        public int AbortedCount { get; private set; }
        public int ContactsAccented { get; private set; }
        public int ContactsSkipped { get; private set; }
        /// <summary>Plan H "기술 슬롯": my sustained instances at most 3. NOT ENFORCED (no shrink / hysteresis yet): the count is recorded, the overflow is counted here.</summary>
        public int LastingCap = 3;
        public int CapExceeded { get; private set; }
        /// <summary>Where the player's brush tip and grip are now (the view binds it to the great brush of the scene; null in a fixture: the recipes fall back to the caster's hand height).</summary>
        public Func<Vector3?> BrushTip, BrushGrip;
        /// <summary>D308-150 V9: the Unity side of the legacy bridge (the view binds Vol2LegacyStage; a fixture binds a recording fake; null = the bridge recipes only keep their bookkeeping).</summary>
        public IVol2LegacyStage Legacy;
        public int ParriesDrawn { get; private set; }

        /// <summary>A successful parry at <paramref name="point"/> - every live ㅓ instance whose parry window is open at <paramref name="nowUs"/> is told (the showcase draws its splash). A plain call
        /// (the scope relays the combat wiring's ParryResolved; the capture calls it for a lab-simulated parry); never decides anything of the parry.</summary>
        public void Parried(Vector3 point, long nowUs)
        {
            foreach (var inst in _list)
            {
                if (inst.Retired || inst.TemplateId.Frame != Frame.Attachment) continue;
                var host = inst.Log.Emitter as Vol2AttachmentHost;
                if (host != null && !host.Open(nowUs)) continue;
                var i = inst;
                Safe(() => { if (i.Recipe.OnParried(i, point, nowUs)) ParriesDrawn++; });
            }
        }

        public Vol2VfxModel(Vol2CastCoordinator coordinator, Vol2VfxCatalogue catalogue, Vol2VfxRecipeRegistry recipes = null)
        {
            _c = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            Catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            Recipes = recipes ?? Vol2VfxRecipeRegistry.CreateDefault();
            _c.CastBegan += OnBegan; _c.CastEnded += OnEnded; _c.ContactResolved += OnContact;
            _c.Materials.Created += OnCreated; _c.Materials.Changed += OnChanged; _c.Materials.Ended += OnMaterialEnded;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _c.CastBegan -= OnBegan; _c.CastEnded -= OnEnded; _c.ContactResolved -= OnContact;
            _c.Materials.Created -= OnCreated; _c.Materials.Changed -= OnChanged; _c.Materials.Ended -= OnMaterialEnded;
            foreach (var i in _list) { i.Retired = true; try { i.Recipe.OnRetired(i, true); } catch (Exception e) { Debug.LogException(e); } Retired?.Invoke(i); }
            _list.Clear(); _byCast.Clear();
        }

        /// <summary>Does this model draw the cast (a filled template of a registered recipe)? The debug presenter skips the casts this answers true for.</summary>
        public bool Covers(Vol2CastLog log)
        {
            if (log == null || log.Spec == null || log.Emitter == null) return false;
            var t = Catalogue.Template(new Vol2TemplateId(log.Glyph.Frame, log.Glyph.Verb));
            return t != null && t.filled && Recipes.TryGet(t.recipe, out _);
        }

        public bool TryGet(CastId cast, out Vol2VfxInstance inst) => _byCast.TryGetValue(cast.Value, out inst);

        void Safe(Action a) { try { a(); } catch (Exception e) { Debug.LogException(e); } }

        void OnBegan(Vol2CastLog log)
        {
            Safe(() =>
            {
                if (_disposed || log == null || log.Spec == null || log.Emitter == null) return;
                var tmpl = Catalogue.Template(new Vol2TemplateId(log.Glyph.Frame, log.Glyph.Verb));
                var look = Catalogue.Look(log.Glyph.Body);
                if (tmpl == null || !tmpl.filled || look == null || !Recipes.TryGet(tmpl.recipe, out var recipe)) return;
                // D308-153: a letter the new spell-fx catalogue covers is drawn by the SpellFxDirector only - this cast gets a recipe that draws nothing, so neither the legacy
                // bridge nor the free showcase (which wraps it) ever sees it. The instance itself stays (Covers / SpawnedCount / the cast camera are untouched).
                if (SpellFxSilencer.IsCovered(log.Glyph.Letter)) recipe = new SpellFxSilentRecipe(tmpl.recipe);
                var inst = new Vol2VfxInstance(this, log, tmpl, look, recipe, _c.Tuning.ChestHeight);
                _byCast[log.Cast.Value] = inst; _list.Add(inst);
                SpawnedCount++;
                recipe.OnBegan(inst);
                Spawned?.Invoke(inst);
            });
        }

        void OnContact(Vol2CastLog log, Vol2ContactRecord rec)
        {
            Safe(() =>
            {
                if (log == null || !_byCast.TryGetValue(log.Cast.Value, out var inst) || inst.Retired) return;
                // a contact the rules refused (ParryNoEffect, FirstTargetOnly, ResidueGone ...) draws no hit
                if (!rec.BudgetAccepted) { ContactsSkipped++; return; }
                ContactsAccented++;
                inst.Recipe.OnContact(inst, rec);
            });
        }

        void OnCreated(Vol2Material m)
        {
            Safe(() =>
            {
                if (!_byCast.TryGetValue(m.Cast.Value, out var inst) || inst.Retired) return;
                inst.Recipe.OnMaterialCreated(inst, m);
                // plan H slot "my sustained looks, 3": counted when a sustained material stands, NOT enforced
                int lasting = 0;
                foreach (var i in _list) { if (i.Retired) continue; foreach (var p in i.Parts) if (p.Mat != null && !p.Dead && !p.Ended) { lasting++; break; } }
                if (lasting > LastingCap) CapExceeded++;
            });
        }

        void OnChanged(Vol2Material m, string what)
        {
            Safe(() => { if (_byCast.TryGetValue(m.Cast.Value, out var inst) && !inst.Retired) inst.Recipe.OnMaterialChanged(inst, m, what); });
        }

        void OnMaterialEnded(Vol2Material m, Vol2MaterialEnd why)
        {
            Safe(() =>
            {
                if (!_byCast.TryGetValue(m.Cast.Value, out var inst) || inst.Retired) return;
                if (why == Vol2MaterialEnd.Aborted) { var p = inst.PartOf(m); if (p != null) p.Gone = true; return; }
                inst.Recipe.OnMaterialEnded(inst, m, why);
            });
        }

        void OnEnded(Vol2CastLog log)
        {
            Safe(() =>
            {
                if (log == null || !_byCast.TryGetValue(log.Cast.Value, out var inst)) return;
                inst.CastIsEnded = true; inst.CastEndUs = log.EndUs;
            });
        }

        /// <summary>Once per frame, after the coordinator's tick. Reads the scope clock only.</summary>
        public void Tick(long nowUs)
        {
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var inst = _list[i];
                if (inst.Retired) { _list.RemoveAt(i); continue; }
                try
                {
                    // the lab reset (AbortAll) stops a cast without raising CastEnded: its look is cleared at once, no tail
                    if (inst.Log.Status == "Aborted") { Retire(inst, true); _list.RemoveAt(i); continue; }
                    if (inst.Log.Finished && !inst.CastIsEnded) { inst.CastIsEnded = true; inst.CastEndUs = inst.Log.EndUs; }
                    inst.Recipe.Update(inst, nowUs);
                    foreach (var p in inst.Parts) if (!p.RetiredFlag && inst.PartRetired(p, nowUs)) p.RetiredFlag = true;
                    if (inst.Recipe.IsFinished(inst, nowUs)) { Retire(inst, false); _list.RemoveAt(i); }
                }
                catch (Exception e) { Debug.LogException(e); Retire(inst, true); _list.RemoveAt(i); }
            }
        }

        void Retire(Vol2VfxInstance inst, bool aborted)
        {
            inst.Retired = true; RetiredCount++; if (aborted) AbortedCount++;
            _byCast.Remove(inst.Cast.Value);
            try { inst.Recipe.OnRetired(inst, aborted); } catch (Exception e) { Debug.LogException(e); }
            Retired?.Invoke(inst);
        }
    }
}
