using System;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    [Serializable]
    public sealed class DemoEscortStop
    {
        public string Id, CheckpointId, RouteId;
        public Transform Interaction, Parking, CompanionWait, CargoWait, Checkpoint;
        public Transform StagingApproach;
    }

    // Physical scene references only; the session exclusively owns escort state and transactions.
    public sealed class DemoEscortSceneRoute : MonoBehaviour
    {
        public DemoEscortStop[] Stops = Array.Empty<DemoEscortStop>();
    }
}
