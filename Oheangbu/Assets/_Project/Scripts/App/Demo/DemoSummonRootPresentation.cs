using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.Demo
{
    // Existing botanical mesh, positioned from the same immutable ground plan as combat.
    // No independent Update, physics, random path, or damage callback.
    public sealed class DemoSummonRootPresentation : MonoBehaviour
    {
        sealed class Piece
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public Vector3 Position, Up;
            public float Arrival, GroundY;
        }
        readonly List<Piece> pieces = new List<Piece>();
        MaterialPropertyBlock block;
        SummonRootAttackPlan plan;
        public int VisiblePieces { get; private set; }

        public void Sample(SummonCombatProfile profile, SummonRootAttackPlan next, float age, bool active)
        {
            if (block == null) block = new MaterialPropertyBlock();
            if (!ReferenceEquals(plan, next))
            {
                Clear(); plan = next;
                if (plan != null && profile.RootMesh != null && profile.RootMaterial != null) Build(profile);
            }
            VisiblePieces = 0;
            foreach (var piece in pieces)
            {
                float since = age - piece.Arrival;
                bool visible = active && plan != null && !plan.IsCancelled && !plan.IsFinished && since >= -.12f &&
                    since < profile.RootSegmentHoldSeconds + profile.RootSegmentRetractSeconds;
                piece.Renderer.enabled = visible;
                if (!visible) continue;
                VisiblePieces++;
                float rise = Mathf.SmoothStep(0, 1, Mathf.Clamp01((since + .12f) / .12f));
                float retract = Mathf.Clamp01((since - profile.RootSegmentHoldSeconds) / Mathf.Max(.001f,profile.RootSegmentRetractSeconds));
                piece.Transform.position = piece.Position - piece.Up * (.35f * (1 - rise) + .35f * retract);
                block.Clear(); block.SetFloat("_Visibility", 1 - retract);
                // The authored botanical shader clips buried surfaces to its ground plane.
                block.SetFloat("_GroundY", piece.GroundY);
                piece.Renderer.SetPropertyBlock(block);
            }
        }

        void Build(SummonCombatProfile profile)
        {
            var points = plan.GroundPositions;
            var times = plan.ArrivalTimes;
            Bounds bounds = profile.RootMesh.bounds;
            // One existing branched root spans roughly .75m: at most eight pieces for the TEST 6m line.
            // Sparse rigid parts preserve asset detail without spawning a mesh for every ground probe.
            for (int i = 0; i + 1 < points.Count;)
            {
                int end = i + 1;
                while (end + 1 < points.Count && Vector3.Distance(points[i], points[end]) < .72f) end++;
                Vector3 delta = points[end] - points[i];
                if (delta.sqrMagnitude < .0001f) { i = end; continue; }
                Quaternion rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
                Vector3 up = rotation * Vector3.up;
                var go = new GameObject("BotanicalRoot_" + pieces.Count);
                go.transform.SetParent(transform, false);
                Vector3 scale = new Vector3(profile.RootAttackWidth / Mathf.Max(.001f,bounds.size.x),
                    2.2f, delta.magnitude / Mathf.Max(.001f,bounds.size.z));
                go.transform.rotation = rotation; go.transform.localScale = scale;
                Vector3 offset = new Vector3(bounds.center.x, bounds.min.y, bounds.min.z);
                Vector3 position = points[i] - rotation * Vector3.Scale(offset,scale);
                go.transform.position = position;
                go.AddComponent<MeshFilter>().sharedMesh = profile.RootMesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = profile.RootMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.enabled = false;
                pieces.Add(new Piece { Transform=go.transform, Renderer=renderer, Position=position, Up=up, Arrival=times[i], GroundY=Mathf.Min(points[i].y,points[end].y)-.025f });
                i = end;
            }
        }
        void Clear()
        {
            foreach (var piece in pieces) if (piece.Transform != null)
            {
                piece.Transform.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(piece.Transform.gameObject); else DestroyImmediate(piece.Transform.gameObject);
            }
            pieces.Clear(); VisiblePieces = 0;
        }
        void OnDestroy() { Clear(); }
    }
}
