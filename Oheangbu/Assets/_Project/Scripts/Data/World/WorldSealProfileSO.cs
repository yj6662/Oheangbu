using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    // #308 1b′ 남문 전 봉인 — save relocation data (SPEC-WORLD-ENCLOSURE-305 §1b′ "세이브 이전", D308-3) [TEST].
    // Written by the editor seal builder from the offline closure (Out/beyond308.json); read by WorldSealRules308 at load.
    // No Odin, no runtime logic here: the rule lives in Oheangbu.App (WorldSealRules308, pure static functions).
    [CreateAssetMenu(menuName="Oheangbu/World/Seal Profile 308")]
    public sealed class WorldSealProfileSO:ScriptableObject
    {
        // One polygon ring on the XZ plane (x = world X, y = world Z). Rings are combined with the even-odd rule, so a hole is a ring
        // inside another ring. Beyond = (open-state reach - closed-state reach) on the 4 m grid, shrunk inwards by 8 m (TEST).
        [Serializable] public sealed class Ring
        {
            public Vector2[] Points=Array.Empty<Vector2>();
        }
        // An EA mine box (closed-state reachable underground). A point inside it and deeper than UndergroundDepth under the surface
        // counts as the mine, not as the land above it.
        [Serializable] public sealed class Volume
        {
            public string Id="";
            public Vector3 Center;
            public Vector3 Size=Vector3.one;
            [Tooltip("Rotation about +Y in degrees.")] public float Yaw;
        }
        [Tooltip("The durable fact that lifts the seal: the builder copies WorldMacroPlaytestSession.SouthGateOpenedId.")]
        public string RequiredFact="";
        public Ring[] BeyondRings=Array.Empty<Ring>();
        public Volume[] EaUndergroundVolumes=Array.Empty<Volume>();
        [Tooltip("Rests reachable while the seal is closed. The nearest one that resolves is chosen.")]
        public string[] EaRestIds=Array.Empty<string>();
        [Tooltip("Used in order when no EaRestIds entry resolves.")]
        public string[] FallbackRestIds={"geumpyo_inn","mine_start"};
        [Tooltip("TEST: move a currency drop that lies beyond the seal.")] public bool MoveDrops=true;
        [Tooltip("TEST: move a saved vehicle position that lies beyond the seal.")] public bool MoveVehicle=true;
        [Tooltip("TEST: metres under the surface from which a point inside an EA underground volume counts as the mine.")]
        [Min(0)] public float UndergroundDepth=4f;
        [Tooltip("Hash of the offline inputs the builder read (ledger only).")]
        public string SourceHash="";
        public bool IsConfigured=>!string.IsNullOrWhiteSpace(RequiredFact)&&BeyondRings!=null&&BeyondRings.Length>0;
    }
}
