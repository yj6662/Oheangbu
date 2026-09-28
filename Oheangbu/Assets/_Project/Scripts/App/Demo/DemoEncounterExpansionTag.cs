using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // Data-only ownership/classification. No combat, save, spawn, or campaign authority.
    public sealed class DemoEncounterExpansionTag : MonoBehaviour
    {
        public string GroupId, Role, RouteId, Narrative;
        public bool IsElite;
        public EnemyArchetype Archetype;
        public float RouteFraction;
        public Vector3 ApproachFeet;
    }
}
