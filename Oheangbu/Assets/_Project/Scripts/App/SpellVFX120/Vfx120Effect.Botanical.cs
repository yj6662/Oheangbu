using UnityEngine;
using Oheangbu.Spellcraft;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        private sealed class BotanicalPart
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public float Height;
            public MaterialPropertyBlock Block;
        }
        private BotanicalPart[] _botanicalParts;
        public bool BotanicalConfigured { get; private set; }
        public GameObject BotanicalInstance { get; private set; }
        public string BotanicalDiagnostic { get; private set; } = "UNASSIGNED";
        public float BotanicalOpacity { get; private set; }
        // Nominal stem placement, not a claim that every leaf bounds the damage path.
        public float BotanicalNominalHalfWidth { get; private set; }
        private static readonly int VisibilityId = Shader.PropertyToID("_Visibility");
        private static readonly int BotanicalGroundId = Shader.PropertyToID("_GroundY");

        private void BuildBotanical()
        {
            var source = Profile.BotanicalPrefab;
            var kind = Profile.BotanicalKind;
            if (source == null || kind == Vfx120BotanicalKind.None) return;
            if (kind != Vfx120BotanicalKind.RootBind && kind != Vfx120BotanicalKind.BambooFront
                && kind != Vfx120BotanicalKind.PlantedTree)
            { BotanicalDiagnostic = "UNSUPPORTED_KIND"; return; }
            var plan = ReceivedAreaPlan;
            if (kind == Vfx120BotanicalKind.BambooFront && (plan == null || plan.Shape != AreaShape.Path
                || !WashFinite(plan.Point) || !WashFinite(plan.Direction) || !WashFinite(plan.Radius)
                || !WashFinite(plan.Length) || !WashFinite(plan.Speed) || !WashFinite(plan.Delay)
                || plan.Radius <= 0 || plan.Length <= 0 || plan.Speed <= 0 || plan.Delay < 0))
            { BotanicalDiagnostic = "COMPATIBLE_PATH_REQUIRED"; return; }
            // Accept only the deliberately authored mesh-only source, not arbitrary behaviours
            // or the original scene-tree prefab's movement/camera collision components.
            var parts = source.GetComponentsInChildren<MeshRenderer>(true);
            if (parts.Length == 0 || parts.Length > 8 || source.transform.childCount != parts.Length
                || source.GetComponentsInChildren<MonoBehaviour>(true).Length != 0
                || source.GetComponentsInChildren<Collider>(true).Length != 0
                || source.GetComponentsInChildren<Rigidbody>(true).Length != 0
                || source.GetComponentsInChildren<Animator>(true).Length != 0
                || source.GetComponentsInChildren<ParticleSystem>(true).Length != 0)
            { BotanicalDiagnostic = "MESH_ONLY_SOURCE_REQUIRED"; return; }
            foreach (var part in parts)
            {
                var filter = part.GetComponent<MeshFilter>();
                if (part.transform.parent != source.transform || filter == null || filter.sharedMesh == null
                    || !WashFinite(part.transform.localPosition) || !WashFinite(part.transform.localScale)
                    || !WashFinite(filter.sharedMesh.bounds.center) || !WashFinite(filter.sharedMesh.bounds.size)
                    || part.sharedMaterials.Length != filter.sharedMesh.subMeshCount)
                { BotanicalDiagnostic = "MESH_OR_TRANSFORM_INVALID"; return; }
                foreach (var material in part.sharedMaterials)
                    if (material == null || material.shader == null || !material.shader.isSupported
                        || !material.HasProperty(VisibilityId) || material.GetTexture("_BaseMap") == null)
                    { BotanicalDiagnostic = "BOTANICAL_MATERIAL_REQUIRED"; return; }
            }
            BotanicalInstance = Instantiate(source, transform, false);
            BotanicalInstance.name = "Botanical_" + kind;
            BotanicalInstance.transform.localPosition = Vector3.zero;
            BotanicalInstance.transform.localRotation = Quaternion.identity;
            BotanicalInstance.transform.localScale = Vector3.one;
            var renderers = BotanicalInstance.GetComponentsInChildren<MeshRenderer>(true);
            _botanicalParts = new BotanicalPart[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i]; var part = renderer.transform;
                // Bounds measured in the authored assembly, including rotations at root forks.
                var bounds = renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                var matrix = Matrix4x4.TRS(part.localPosition, part.localRotation, part.localScale);
                float maxY = 0;
                for (int corner = 0; corner < 8; corner++)
                {
                    var v = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    maxY = Mathf.Max(maxY, matrix.MultiplyPoint3x4(v).y);
                }
                _botanicalParts[i] = new BotanicalPart { Transform = part, Renderer = renderer,
                    Position = part.localPosition, Rotation = part.localRotation, Scale = part.localScale,
                    Height = Mathf.Max(.05f, maxY), Block = new MaterialPropertyBlock() };
                renderer.enabled = false;
            }
            BotanicalConfigured = true;
            BotanicalDiagnostic = "CURATED_MESH_BODY_CONFIGURED";
        }

        private void SampleBotanical()
        {
            if (!BotanicalConfigured || BotanicalInstance == null || !WashFinite(Age)) return;
            var kind = Profile.BotanicalKind;
            bool bamboo = kind == Vfx120BotanicalKind.BambooFront;
            bool tree = kind == Vfx120BotanicalKind.PlantedTree;
            Vector3 anchor;
            if (bamboo) anchor = Center();
            else if (tree && ReceivedAreaPlan != null && ReceivedAreaPlan.Shape == AreaShape.Circle)
                anchor = transform.InverseTransformPoint(ReceivedAreaPlan.Point);
            else anchor = new Vector3(_aim.x, _targetGround, _aim.z);
            var host = BotanicalInstance.transform;
            host.localPosition = anchor;
            host.localRotation = bamboo ? Quaternion.LookRotation(AreaDirection(), Vector3.up) : Quaternion.identity;
            host.localScale = Vector3.one;
            float formation = bamboo ? ReceivedAreaPlan.Delay : _flight;
            float elapsed = Age - formation;
            float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Life - .55f, Life, Age));
            if (tree)
            {
                float release = CueContext().ReleaseAt;
                if (release >= 0) fade *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(release, release + .4f, Age));
            }
            BotanicalOpacity = elapsed >= 0 && Age < Life ? fade * Mathf.Clamp01((elapsed + .001f) / .12f) : 0;
            BotanicalNominalHalfWidth = bamboo ? ReceivedAreaPlan.Radius : 0;
            float worldGround = transform.TransformPoint(anchor).y;
            for (int i = 0; i < _botanicalParts.Length; i++)
            {
                var part = _botanicalParts[i];
                float localAge = elapsed - (tree ? 0 : i * .024f);
                float growth = Mathf.SmoothStep(0, 1, Mathf.Clamp01(localAge / (tree ? .62f : .42f)));
                var position = part.Position;
                if (bamboo) position.x *= BotanicalNominalHalfWidth;
                // Full-size textured geometry emerges from soil. No elastic trunk scaling.
                position.y -= part.Height * (1 - growth);
                part.Transform.localPosition = position;
                part.Transform.localScale = part.Scale;
                float sway = bamboo ? Mathf.Sin(elapsed * 1.8f + i * 1.73f) * 1.7f * growth
                    : tree ? Mathf.Sin(elapsed * .9f) * .28f * growth : 0;
                part.Transform.localRotation = part.Rotation * Quaternion.Euler(0, 0, sway);
                part.Block.SetFloat(VisibilityId, BotanicalOpacity);
                part.Block.SetFloat(BotanicalGroundId, worldGround - .012f);
                part.Renderer.SetPropertyBlock(part.Block);
                part.Renderer.enabled = BotanicalOpacity > .001f && growth > 0;
            }
        }

        private void ClearBotanical()
        {
            if (BotanicalInstance != null) DisposeNativeHost(BotanicalInstance);
            BotanicalInstance = null; _botanicalParts = null;
            BotanicalConfigured = false; BotanicalOpacity = BotanicalNominalHalfWidth = 0;
            BotanicalDiagnostic = "UNASSIGNED";
        }
    }
}
