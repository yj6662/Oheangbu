using UnityEngine;

namespace Oheangbu.App.World
{
    // #306 §2-6 (SPEC-NATURE-COLLISION-306): the session's view of the near-field natural collider pool (CompactNaturalSolids).
    // The pool follows the walker/vehicle on its own Update (radii, cadence, vehicle look-ahead live in its profile); the session
    // only calls EnsureAround before it tests or uses a point. Play mode only; an absent pool is a valid state.
    // Signature matches CompactNaturalSolids.EnsureAround, so the pool implements this by declaration only.
    public interface INaturalSolidPool306
    {
        // Synchronous: every solid inside the active radius of feet is enabled and transforms synced before this returns.
        // False = not ready (edit mode, disabled, unprepared). May re-centre the active set on feet; the session restores
        // the walker's focus after probing a point away from the walker.
        bool EnsureAround(Vector3 feet);
    }
}
