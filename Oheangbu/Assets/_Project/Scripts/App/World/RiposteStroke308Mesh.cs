using Oheangbu.BrushRender;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    // D308-148 (step 5): one pooled stroke ribbon. Built ONCE at the scene's warm-up (GameObject, dynamic Mesh, a Material copy,
    // the RibbonMeshBuilder and its point buffer grown to the maximum), then reused fire after fire: the fire only places the plane
    // and rewrites the vertices. The mesh is planar (RibbonMeshBuilder's "local XY" premise): the object sits on the stroke's tilted
    // world plane and never follows a camera. RibbonMeshBuilder is used as it is, with NO smoothing and NO caps, so the vertex pairs'
    // midpoints are exactly RiposteStroke308Path.Pos (the pierce is provable on the final mesh) and UV0.u = arc length / total length
    // (the whole stroke is always built, the reveal is the shader's `_RevealS`, so nothing slides). Vertex alpha = ink density.
    public sealed class RiposteStroke308Mesh
    {
        static readonly int RevealId = Shader.PropertyToID("_RevealS"), DryId = Shader.PropertyToID("_Dry"), SeedId = Shader.PropertyToID("_Seed");
        static readonly Color White = Color.white;

        readonly GameObject _go;
        readonly Transform _t;
        readonly MeshRenderer _renderer;
        readonly Mesh _mesh;
        readonly Material _material;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly BrushStrokeData _data = new BrushStrokeData();
        readonly RibbonMeshBuilder _builder = new RibbonMeshBuilder();

        public Mesh Mesh => _mesh;
        public Transform Transform => _t;
        public bool Visible => _renderer != null && _renderer.enabled;

        public RiposteStroke308Mesh(string name, Transform parent, Material source, int layer)
        {
            _go = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = layer };
            _t = _go.transform; _t.SetParent(parent, false);
            _mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off; _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off; _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _material = source != null ? new Material(source) { hideFlags = HideFlags.DontSave } : null;
            _renderer.sharedMaterial = _material;
            _renderer.enabled = false;
            // grow every buffer to the maximum once: a dummy build of the largest stroke
            _data.Clear();
            for (int i = 0; i < RiposteStroke308Path.MaxSamples; i++) _data.Add(new BrushStrokePoint(new Vector3(i * .1f, 0f, 0f), .2f, 1f, 0f));
            _builder.Build(_data, White, 1f, 0, 0, 0, _mesh);
            _data.Clear();
            _mesh.Clear();
        }

        /// <summary>Puts the object on the stroke's plane (once per fire).</summary>
        public void Place(RiposteStroke308Path path)
        {
            _t.SetPositionAndRotation(path.Origin, Quaternion.LookRotation(path.N, path.V));
            _t.localScale = Vector3.one;
            if (_t.parent != null)
            {
                var ls = _t.parent.lossyScale;
                _t.localScale = new Vector3(1f / Mathf.Max(1e-4f, ls.x), 1f / Mathf.Max(1e-4f, ls.y), 1f / Mathf.Max(1e-4f, ls.z));
            }
        }

        /// <summary>Rewrites the vertices from the path's samples (the whole stroke, every time).</summary>
        public void Rebuild(RiposteStroke308Path path)
        {
            _data.Clear();
            for (int i = 0; i < path.Count; i++) _data.Add(new BrushStrokePoint(path.Pos[i], path.Width[i], path.Ink[i], 0f));
            _builder.Build(_data, White, 1f, 0, 0, 0, _mesh);
        }

        public void SetState(float reveal, float dry, float seed)
        {
            if (_renderer == null) return;
            _block.SetFloat(RevealId, reveal); _block.SetFloat(DryId, dry); _block.SetFloat(SeedId, seed);
            _renderer.SetPropertyBlock(_block);
        }

        public void Show(bool on) { if (_renderer != null) _renderer.enabled = on && _material != null; }

        public void Hide() { Show(false); }

        public void Dispose()
        {
            if (_go != null) Object.Destroy(_go);
            if (_mesh != null) Object.Destroy(_mesh);
            if (_material != null) Object.Destroy(_material);
        }

        /// <summary>Edit mode (the checks): immediate destruction.</summary>
        public void DisposeImmediate()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_mesh != null) Object.DestroyImmediate(_mesh);
            if (_material != null) Object.DestroyImmediate(_material);
        }
    }
}
