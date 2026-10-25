using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 9a: the VIEW of the vol.2 spell look. It owns no timer that decides a lifetime: every frame it asks the model to tick on the scope clock (<see cref="Vol2VfxModel.Tick"/>),
    /// rebuilds the mesh of a dirty instance (one mesh, one draw call per instance) and hands the shader the instance's age on that same clock. The model's events drive spawning and
    /// retiring; a retired instance's mesh and renderer go back to the pool (nothing is destroyed per cast). No emission, no light, no additive: see Oheangbu/SpellVFX308/Vol2Ink.
    /// Replaces <see cref="Vol2DebugPresenter"/> for the casts the model covers (the debug presenter is told to skip them); everything else still shows the debug lines.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class Vol2VfxPresenter : MonoBehaviour
    {
        sealed class View
        {
            public GameObject Go; public MeshFilter Filter; public MeshRenderer Renderer; public Mesh Mesh; public MaterialPropertyBlock Block;
            public Vol2VfxInstance Instance;
        }

        static readonly int IdAge = Shader.PropertyToID("_Age"), IdHead = Shader.PropertyToID("_Head"), IdOffset = Shader.PropertyToID("_Offset"), IdBaseY = Shader.PropertyToID("_BaseY"),
            IdTail = Shader.PropertyToID("_TailLen"), IdInk = Shader.PropertyToID("_InkColor"), IdPigment = Shader.PropertyToID("_PigmentColor"), IdWet = Shader.PropertyToID("_WetColor"),
            IdEdge = Shader.PropertyToID("_EdgeKind"), IdEnd = Shader.PropertyToID("_EndKind"), IdEndDir = Shader.PropertyToID("_EndDir"), IdFray = Shader.PropertyToID("_Fray"),
            IdGap = Shader.PropertyToID("_Gap"), IdSettle = Shader.PropertyToID("_SettleStart"), IdSettleEnd = Shader.PropertyToID("_SettleEnd");

        readonly Dictionary<Vol2VfxInstance, View> _views = new Dictionary<Vol2VfxInstance, View>();
        readonly Stack<View> _free = new Stack<View>();
        readonly Vol2InkWriter _writer = new Vol2InkWriter();
        Vol2CastCoordinator _coordinator;
        Vol2VfxModel _model;
        Vol2VfxCatalogue _catalogue;
        HideFlags _flags;

        public bool Enabled = true;
        public Vol2VfxModel Model => _model;
        public Vol2CastCoordinator Coordinator => _coordinator;
        public Vol2VfxCatalogue Catalogue => _catalogue;
        /// <summary>Instances drawn so far (the counterpart of the debug presenter's Drawn).</summary>
        public int Drawn => _model != null ? _model.SpawnedCount : 0;
        public int ActiveViews => _views.Count;
        public int PoolFree => _free.Count;
        public int PoolCreated { get; private set; }
        public int Rebuilds { get; private set; }
        public int LastVertices { get; private set; }
        /// <summary>The scope-clock time of the last Step: what the pictures on screen show (a capture reads it).</summary>
        public long LastStepUs { get; private set; }

        /// <summary>Builds the presenter under <paramref name="parent"/>. Null (and a warning) when no catalogue can be had (no asset, no shader).</summary>
        public static Vol2VfxPresenter Attach(Vol2CastCoordinator coordinator, Transform parent, Vol2VfxCatalogue catalogue = null, Vol2DebugPresenter debug = null, HideFlags flags = HideFlags.None)
        {
            catalogue = catalogue ?? Vol2VfxCatalogue.Load();
            if (catalogue == null) { Debug.LogWarning("[Vol2Vfx] no catalogue / shader: the debug lines stay"); return null; }
            var go = new GameObject("Vol2VfxPresenter") { hideFlags = flags };
            if (parent != null) go.transform.SetParent(parent, false);
            var p = go.AddComponent<Vol2VfxPresenter>();
            p._flags = flags;
            p._coordinator = coordinator; p._catalogue = catalogue;
            p._model = new Vol2VfxModel(coordinator, catalogue);
            p._model.Retired += p.OnRetired;
            p._model.Spawned += p.OnSpawned;
            // NB20: the brush tip / grip of the player's great brush (ignition, the ㅏ head's start, the ㅓ pulse on the shaft); searched lazily, a bounded number of times
            p._model.BrushTip = () => p.Rig() is var r && r != null && r.WorldTip != null ? r.WorldTip.position : (Vector3?)null;
            p._model.BrushGrip = () => p.Rig() is var r && r != null && r.WorldBrushRoot308 != null ? r.WorldBrushRoot308.position : (Vector3?)null;
            if (debug != null) debug.Skip = log => p.Enabled && p._model.Covers(log);
            return p;
        }

        World.WorldMacroPlayerGestureRig _rig;
        int _rigSearches;
        World.SpellSwing308 _swing;
        int _swingSearches;
        /// <summary>stroke20: swings the presenter asked the body for (presentation only).</summary>
        public int SwingsShown { get; private set; }
        /// <summary>D308-150 V8: play the body's show swing (no hit) on a stroke20 cast.</summary>
        public bool BodySwing308 = true;

        /// <summary>stroke20 (D308-150 V7, coordinator 2026-10-25 "the old 가 is the floor"): the old route shows a cast as a swing of the great brush (SPEC-SPELL-SWING-308);
        /// the vol.2 route launched with the arm still. A stroke20 cast now asks the body for the same swing motion, presentation only (SpellSwing308.PlayStrike308: the clip
        /// is started so its apex falls 0.08 s after the cast, the plan's first beat; the rule never waits for it and never reads it).</summary>
        void OnSpawned(Vol2VfxInstance inst)
        {
            // D308-150 V8 (보여 주기 휘두르기): ON, through SpellSwing308.PlayShow308 - marked ShowOnly, so the vol.2 swing driver opens no hit window for it
            // (r11 played it as a normal strike and the swing driver hit with it: lab FAIL 110/125). The real left-click swing alone hits.
            if (!BodySwing308) return;
            if (!Application.isPlaying || !(inst.Recipe is Vol2StrokeRecipeBase sr)) return;
            if (_swing == null && _swingSearches < 40) { _swingSearches++; _swing = FindFirstObjectByType<World.SpellSwing308>(); }
            if (_swing != null && _swing.PlayShow308(sr.SwingState, Vol2StrokeRecipeBase.SwingToApex, 0.4f)) SwingsShown++;
        }
        World.WorldMacroPlayerGestureRig Rig()
        {
            if (_rig != null) return _rig;
            if (_rigSearches >= 40 || !Application.isPlaying) return null;
            _rigSearches++;
            _rig = FindFirstObjectByType<World.WorldMacroPlayerGestureRig>();
            return _rig;
        }

        void OnDestroy() { Release(); }

        /// <summary>Frees every view, mesh and the model's subscriptions (what OnDestroy does; public because OnDestroy does not run for an edit-mode fixture).</summary>
        public void Release()
        {
            if (_model != null) { _model.Retired -= OnRetired; _model.Spawned -= OnSpawned; _model.Dispose(); _model = null; }
            foreach (var v in _views.Values) Kill(v);
            _views.Clear();
            while (_free.Count > 0) Kill(_free.Pop());
        }

        static void Free(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);        // an edit-mode fixture drives this class too
        }

        static void Kill(View v)
        {
            Free(v.Mesh); Free(v.Go);
        }

        View Rent()
        {
            if (_free.Count > 0) { var v = _free.Pop(); v.Go.SetActive(true); return v; }
            var go = new GameObject("Vol2Vfx") { hideFlags = _flags };
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "Vol2Vfx", hideFlags = _flags };
            mesh.MarkDynamic();
            var f = go.AddComponent<MeshFilter>(); f.sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _catalogue.inkMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            PoolCreated++;
            return new View { Go = go, Filter = f, Renderer = r, Mesh = mesh, Block = new MaterialPropertyBlock() };
        }

        void OnRetired(Vol2VfxInstance inst)
        {
            if (!_views.TryGetValue(inst, out var v)) return;
            _views.Remove(inst);
            // plan H "회수 확인": the mesh is emptied and the property block cleared before the view goes back to the pool
            v.Mesh.Clear(); v.Block.Clear(); v.Renderer.SetPropertyBlock(v.Block); v.Renderer.enabled = false; v.Instance = null;
            v.Go.SetActive(false);
            _free.Push(v);
        }

        void Update() { Step(); }

        /// <summary>One frame of the view: tick the model on the scope clock, rebuild the dirty meshes, set the per-frame uniforms. Public so an edit-mode fixture can drive it.</summary>
        public void Step()
        {
            if (_model == null || _coordinator == null) return;
            long now = _coordinator.Services.Clock.NowUs;
            LastStepUs = now;
            _model.Tick(now);
            if (!Enabled) return;
            var list = _model.Instances;
            for (int i = 0; i < list.Count; i++)
            {
                var inst = list[i];
                if (inst.Retired) continue;
                if (!_views.TryGetValue(inst, out var v))
                {
                    if (!inst.Dirty) continue;                       // nothing to draw (a cast that only waits for its lasting damage): no view is held
                    v = Rent(); v.Instance = inst; _views.Add(inst, v); Statics(v, inst);
                }
                if (inst.Dirty && Rebuild(v, inst)) { OnRetired(inst); continue; }          // everything of it is dry: the view goes back to the pool
                Frame(v, inst, now);
            }
        }

        void Statics(View v, Vol2VfxInstance inst)
        {
            var look = inst.Look;
            var b = v.Block;
            b.Clear();
            b.SetColor(IdInk, look.inkLayer); b.SetColor(IdPigment, look.dryPigment); b.SetColor(IdWet, look.wetColor);
            // D308-150 V8: a stroke20 cast is inked in the OLD effects' body pigment (strong, like the old 120)
            if (inst.Recipe is Vol2StrokeRecipeBase) { var old = Vol2StrokeRecipeBase.OldPigment(look.body); b.SetColor(IdWet, old); b.SetColor(IdPigment, old); }
            b.SetFloat(IdEdge, (float)(int)look.edge); b.SetFloat(IdEnd, (float)(int)look.end);
            Vector3 dir = inst.Right * look.endDirection.x + Vector3.up * look.endDirection.y + inst.Forward * look.endDirection.z;
            if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
            b.SetVector(IdEndDir, new Vector4(dir.x, dir.y, dir.z, look.endTravel));
            b.SetFloat(IdFray, look.fray); b.SetFloat(IdGap, look.rimGap);
            b.SetFloat(IdBaseY, inst.Origin.y);
            b.SetFloat(IdTail, Mathf.Max(0.5f, inst.Template.tailMeters));
            b.SetFloat(IdSettle, inst.Template.outlineSeconds); b.SetFloat(IdSettleEnd, inst.Template.surfaceSeconds);
        }

        bool Rebuild(View v, Vol2VfxInstance inst)
        {
            _writer.Clear();
            inst.Recipe.Write(inst, _writer);
            _writer.Apply(v.Mesh, inst.Origin, 30f);
            inst.Dirty = false; inst.Version++; Rebuilds++; LastVertices = _writer.Vertices;
            v.Renderer.enabled = _writer.Vertices > 0;
            return _writer.Vertices == 0;
        }

        void Frame(View v, Vol2VfxInstance inst, long now)
        {
            var b = v.Block;
            b.SetFloat(IdAge, inst.Age(now));
            b.SetFloat(IdHead, Mathf.Max(0f, inst.Recipe.Head(inst, now) - Vol2PathRecipeBase.LineStart));
            Vector3 off = inst.Recipe.Offset(inst, now);
            b.SetVector(IdOffset, new Vector4(off.x, off.y, off.z, 0f));
            v.Renderer.SetPropertyBlock(b);
        }

        /// <summary>Drops every view and instance state at once (a scope reset). The coordinator's AbortAll already retires the instances through the model; this is the hard stop.</summary>
        public void ClearAll()
        {
            if (_model == null) return;
            foreach (var kv in new List<KeyValuePair<Vol2VfxInstance, View>>(_views)) OnRetired(kv.Key);
        }
    }
}
