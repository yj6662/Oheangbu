using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.Combat
{
    // Use the owner's physics world, including preview/additive local-physics scenes.
    // Buffers are reused and expanded when necessary, matching RaycastAll's untruncated semantics.
    public static class ScenePhysicsQuery
    {
        public static int RaycastAll(Scene scene, Vector3 origin, Vector3 direction, float distance,
            int mask, ref RaycastHit[] hits)
        {
            if (distance <= 0 || direction.sqrMagnitude < .000001f) return 0;
            PhysicsScene physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
            if (!physics.IsValid()) return 0;
            if (hits == null || hits.Length == 0) hits = new RaycastHit[16];
            while (true)
            {
                int count = physics.Raycast(origin, direction.normalized, hits, distance, mask, QueryTriggerInteraction.Ignore);
                if (count < hits.Length) return count;
                System.Array.Resize(ref hits, hits.Length * 2);
            }
        }
    }
}
