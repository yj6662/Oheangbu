using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName = "Oheangbu/World/Compact Architecture")]
    public sealed class CompactArchitectureSheetSO : ScriptableObject
    {
        [Serializable] public sealed class Structure
        {
            public string Id, Realm, PlaceId, Label, SourceId, SceneRoot;
            public Vector3 Position;
            public float Yaw;
            public Vector3 Size;
            public string[] RouteIds = Array.Empty<string>();
        }
        [Serializable] public sealed class Arena
        {
            public string Id, Realm, PlaceId, Label, SceneRoot;
            public string EncounterId, CheckpointId;
            public string[] InteractionIds = Array.Empty<string>();
            public bool TestOnly = true, Interior;
            public Vector3 Centre;
            public Vector2 ClearSize;
            public float ClearHeight;
            public float Yaw;
            public Vector3 Entrance, Exit, SafePoint;
            public Vector3[] Approach = Array.Empty<Vector3>();
        }
        [Serializable] public sealed class Perimeter
        {
            public string Id, Realm, GateScenePath, OpenFact;
            public Vector3[] Points = Array.Empty<Vector3>();
            public Vector3 GateCentre;
            public float GateWidth = 8, Height = 10;
            public bool ActiveGate;
        }
        [Serializable] public sealed class Source
        {
            public string Id, AssetPath, Publisher, Url, License, Use;
            public int MeshyCredits;
        }
        public string Revision = "architecture-296";
        public int Seed = 296;
        public Structure[] Structures = Array.Empty<Structure>();
        public Arena[] Arenas = Array.Empty<Arena>();
        public Perimeter[] Perimeters = Array.Empty<Perimeter>();
        public Source[] Sources = Array.Empty<Source>();
    }
}
