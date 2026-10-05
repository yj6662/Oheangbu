using UnityEngine;
using UnityEngine.AI;

namespace Oheangbu.App.World
{
    // #308 D308-3b 고개 관문 (Seal308) [TEST]: the two 판문 leaves of the stone pass gate. They open only on the durable south-gate fact
    // (D296 capital exits / SouthGateDoorPresentation way: leaves swing inward, the blockers stay until the swing is complete). It is
    // not a gate kind (D308-3): no interaction, no prompt, no light, no emission; spells, keys and vehicles never open it.
    // Reads WorldMacroPlaytestSession.Progress.ledger.completed like CompactMountainGate (no event, no static state - domain reload is off).
    // A separate type on purpose: the demo ending and the south-gate checks find "the" south gate as SouthGateDoorPresentation.
    // Authoring (editor Seal308 seal-scene): ClosedBlockers / ClosedNavigation / OpenColliders are saved DISABLED and the leaves closed,
    // so an edit-time NavMesh bake (PhysicsColliders) sees the pass open. In Play the gate fails closed until the ledger is readable,
    // then snaps to the fact on load (no animation) and animates only when the fact is recorded while playing.
    public sealed class WorldSealGate308 : MonoBehaviour
    {
        public WorldMacroPlaytestSession Session;
        [Tooltip("The durable fact that opens the gate: the builder copies WorldMacroPlaytestSession.SouthGateOpenedId.")]
        public string RequiredCompleted = "";
        [Tooltip("Hinge transforms: left opens with -OpenDegrees, right with +OpenDegrees about local Y (SouthGateDoorPresentation convention).")]
        public Transform LeftLeaf, RightLeaf;
        [Tooltip("TEST: swing of each leaf about its hinge (seal wall manifest).")] public float OpenDegrees = 100f;
        [Tooltip("TEST: seconds for the full swing (seal wall manifest).")] [Min(.1f)] public float Duration = 2.4f;
        [Tooltip("Enabled while the gate is not fully open: the closed leaves' outline blocker (inside the leaves' mass).")]
        public Collider[] ClosedBlockers = new Collider[0];
        [Tooltip("Enabled while the gate is not fully open: carve over the closed leaves.")]
        public NavMeshObstacle[] ClosedNavigation = new NavMeshObstacle[0];
        [Tooltip("Enabled only when fully open: each leaf's own outline, so an open leaf is never walked through.")]
        public Collider[] OpenColliders = new Collider[0];

        Quaternion leftClosed = Quaternion.identity, rightClosed = Quaternion.identity;
        bool captured, initialized, applied;
        float progress, appliedProgress = -1f;

        public bool IsConfigured => LeftLeaf != null && RightLeaf != null && ClosedBlockers != null && ClosedBlockers.Length > 0 && !string.IsNullOrEmpty(RequiredCompleted);
        public bool IsOpen => initialized && progress >= 1f;
        public float OpenProgress => progress;

        void Awake() => Capture();

        void OnEnable()
        {
            Capture();
            if (!initialized) Apply(0f);   // fail closed until the ledger is readable
        }

        void Update()
        {
            var ledger = Session != null && Session.Progress != null ? Session.Progress.ledger : null;
            if (ledger == null || ledger.completed == null) { if (!initialized) Apply(0f); return; }
            // fail closed: a gate missing its fact id or a leaf never opens (its blockers stay on), it never falls back to the
            // authored-open edit state
            bool open = IsConfigured && ledger.completed.Contains(RequiredCompleted);
            if (!initialized) { progress = open ? 1f : 0f; initialized = true; }
            else progress = Mathf.MoveTowards(progress, open ? 1f : 0f, Time.deltaTime / Mathf.Max(.1f, Duration));
            Apply(progress);
        }

        void Capture()
        {
            if (captured) return;
            if (LeftLeaf != null) leftClosed = LeftLeaf.localRotation;
            if (RightLeaf != null) rightClosed = RightLeaf.localRotation;
            captured = true;
        }

        void Apply(float p)
        {
            if (applied && Mathf.Approximately(p, appliedProgress)) return;
            applied = true; appliedProgress = p;
            float v = Mathf.SmoothStep(0f, 1f, p);
            if (LeftLeaf != null) LeftLeaf.localRotation = leftClosed * Quaternion.Euler(0f, -OpenDegrees * v, 0f);
            if (RightLeaf != null) RightLeaf.localRotation = rightClosed * Quaternion.Euler(0f, OpenDegrees * v, 0f);
            bool closed = p < 1f;
            SetColliders(ClosedBlockers, closed);
            SetColliders(OpenColliders, !closed);
            if (ClosedNavigation != null) foreach (var obstacle in ClosedNavigation) if (obstacle != null) obstacle.enabled = closed;
        }

        static void SetColliders(Collider[] colliders, bool on)
        {
            if (colliders == null) return;
            foreach (var c in colliders) if (c != null) c.enabled = on;
        }

        // Editor probes only (GateProbeScope296, Seal308 seal-check): pose the gate fully open or closed without reading the ledger.
        // The caller pauses this component (enabled = false) while probing, restores the saved poses / flags itself and then calls
        // ResyncAfterProbe so Play snaps back to the fact. In Edit mode the saved (closed) pose is re-read as the reference every time.
        public void SetProbePose(bool open)
        {
            if (!Application.isPlaying) captured = false;
            Capture();
            applied = false;
            Apply(open ? 1f : 0f);
        }

        public void ResyncAfterProbe()
        {
            initialized = false;
            applied = false;
            if (!Application.isPlaying) captured = false;
        }
    }
}
