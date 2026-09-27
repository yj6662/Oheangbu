using UnityEngine;
using UnityEngine.AI;

namespace Oheangbu.App.World
{
    // Visual/physical shortcut door only. The session owns the durable fact (ledger.completed contains Id);
    // the door is derived from committed progress and never writes it. Unbarred from the inside only:
    // the interaction point sits behind the closed leaves, so the blocker also blocks the line of sight.
    public sealed class WorldShortcutDoor : MonoBehaviour
    {
        public WorldMacroPlaytestSession Session;
        public string Id;
        public Transform LeftLeaf, RightLeaf, Bar;
        public Vector3 BarOpenOffset = new Vector3(-1.6f, 0, 0);
        public float OpenDegrees = 95, Duration = 2.6f;
        public Collider[] Blockers = new Collider[0];
        public NavMeshObstacle Obstacle;
        Quaternion leftClosed, rightClosed; Vector3 barClosed; float amount; bool initialized, posed;
        public bool Opened => Session != null && Session.Progress?.ledger?.completed != null && Session.Progress.ledger.completed.Contains(Id);
        public bool Passable => initialized && amount >= .99f;
        void Awake()
        {
            if (LeftLeaf != null) leftClosed = LeftLeaf.localRotation;
            if (RightLeaf != null) rightClosed = RightLeaf.localRotation;
            if (Bar != null) barClosed = Bar.localPosition;
            posed = true;
        }
        void Update()
        {
            if (!posed || Session == null || Session.Progress == null) return;
            bool open = Opened;
            if (!initialized) { amount = open ? 1 : 0; initialized = true; }
            else amount = Mathf.MoveTowards(amount, open ? 1 : 0, Time.deltaTime / Mathf.Max(.1f, Duration));
            Apply();
        }
        void Apply()
        {
            // The bar slides into its wall pocket first, then the leaves swing inward.
            float bar = Mathf.SmoothStep(0, 1, Mathf.Clamp01(amount / .3f));
            float swing = Mathf.SmoothStep(0, 1, Mathf.Clamp01((amount - .3f) / .7f));
            if (Bar != null) Bar.localPosition = barClosed + BarOpenOffset * bar;
            if (LeftLeaf != null) LeftLeaf.localRotation = leftClosed * Quaternion.Euler(0, -OpenDegrees * swing, 0);
            if (RightLeaf != null) RightLeaf.localRotation = rightClosed * Quaternion.Euler(0, OpenDegrees * swing, 0);
            bool blocked = amount < .99f;
            if (Blockers != null) foreach (var collider in Blockers) if (collider != null) collider.enabled = blocked;
            if (Obstacle != null) Obstacle.enabled = blocked;
        }
    }
}
