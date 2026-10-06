using System;
using UnityEngine;

namespace Oheangbu.App
{
    // Which field glyph opens a gate: Vine = burnt by the fire field glyph, Boulder = cut by the metal one,
    // Pollution = cleansed by the water one.
    public enum SpellFieldGateKind { Vine, Boulder, Pollution }

    // A thing in the world that one of the three path-opening field glyphs opens (SPEC-SPELL-120-308 WP-12, TEST).
    // This is the spell-side target only: where gates stand in the world is level design (LDB) and is not decided here,
    // and nothing is saved yet (a session can listen to Opened and restore with SetOpen).
    //   - A cast finds a gate through physics: the gate needs a collider (solid or trigger) on itself or on a child that is
    //     active while it is closed. No registry and no static list.
    //   - Closed-only objects are what blocks the way (vines, the boulder, the polluted patch); open-only objects are what
    //     remains (ash, the split rock). Keep them on children: the gate never switches its own object off.
    //   - No light and no emission is driven from here.
    [DisallowMultipleComponent]
    public sealed class SpellFieldGate308 : MonoBehaviour
    {
        public const int QueryCapacity = 32;   // first size of a cast's overlap buffer (it grows when a query fills it)

        [SerializeField] private string _id = "";
        [SerializeField] private SpellFieldGateKind _kind;
        [Tooltip("How far the gate reaches from its pivot, in metres. A cast measures its reach to this edge.")]
        [SerializeField, Min(0f)] private float _radius = 1f;
        [SerializeField] private GameObject[] _closedOnly = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] _openOnly = Array.Empty<GameObject>();
        [SerializeField] private bool _open;

        public string Id => _id;
        public SpellFieldGateKind Kind => _kind;
        public float Radius => _radius;
        public bool IsOpen => _open;

        // Read-only re-broadcast: the gate was opened by a cast. No rule hangs on it.
        public event Action<SpellFieldGate308> Opened;

        // Opens the gate. false = it was open already.
        public bool Open()
        {
            if (_open) return false;
            _open = true; Apply();
            Opened?.Invoke(this);
            return true;
        }

        // Restores a state without announcing it (a saved world, a check's reset).
        public void SetOpen(bool open) { _open = open; Apply(); }

        public void Configure(string id, SpellFieldGateKind kind, float radius, GameObject[] closedOnly, GameObject[] openOnly)
        {
            _id = id ?? ""; _kind = kind; _radius = Mathf.Max(0f, radius);
            _closedOnly = closedOnly ?? Array.Empty<GameObject>(); _openOnly = openOnly ?? Array.Empty<GameObject>();
            Apply();
        }

        private void OnEnable() { Apply(); }

        private void Apply()
        {
            foreach (var item in _closedOnly) if (item != null && item != gameObject) item.SetActive(!_open);
            foreach (var item in _openOnly) if (item != null && item != gameObject) item.SetActive(_open);
        }

        // A TEST target built from a primitive (no asset): a block of the given size that stands for the vines, the boulder or
        // the polluted patch. The polluted patch is a trigger (it does not block walking). Callers make it in Play or in a preview
        // scene only: nothing here keeps it out of a scene that gets saved.
        public static SpellFieldGate308 CreateTest(SpellFieldGateKind kind, Vector3 position, float size)
        {
            var root = new GameObject("Spell308_TestGate_" + kind);
            root.transform.position = position;
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Closed";
            block.transform.SetParent(root.transform, false);
            block.transform.localPosition = Vector3.up * (size * .5f);
            block.transform.localScale = Vector3.one * size;
            var body = block.GetComponent<Collider>();
            if (body != null) body.isTrigger = kind == SpellFieldGateKind.Pollution;
            var gate = root.AddComponent<SpellFieldGate308>();
            gate.Configure("test_" + kind.ToString().ToLowerInvariant(), kind, size * .5f, new[] { block }, null);
            return gate;
        }
    }
}
