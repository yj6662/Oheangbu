using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 (SPEC-ENEMY-RIG-VERIFY-308): the one door EnemyRigVerify308 uses into the #298 studio still (CPU-baked pose, fixed
    // orthographic camera). Review / Film behave exactly as before; nothing here is called by them.
    public static partial class CompactFolklore298
    {
        internal static void RenderPoseVerify308(PreviewRenderUtility preview, GameObject instance, Bounds frame, float yaw, string path) =>
            RenderPose298(preview, instance, frame, yaw, path);
        internal static Bounds MeshBoundsVerify308(GameObject root) => MeshBounds298(root);
    }
}
