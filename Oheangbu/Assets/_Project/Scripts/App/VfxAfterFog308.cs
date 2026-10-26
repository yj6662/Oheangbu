using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App
{
    // 2026-10-26 fog fix (the user: "실험실이든 실제 메인씬이든, 화면 위쪽 안개에 글자 및 vfx가 가려지더라"; Docs/Research/vfx120/fog_occlusion_20261026.md, TEST).
    // Renderer297's realm fog (CompactMist238 = RealmFog297 at BeforeRenderingPostProcessing) runs after every transparent and reads the depth texture. A transparent
    // spell piece writes no depth, so the fog of whatever stands BEHIND it - the 운해 band over the sky, a far ridge's distance fog - was painted over the piece.
    // Renderer297 already draws the layer "VfxAfterFog" after that fog (VfxAfterFog300_Opaque / _Transparent at 550, depth-tested against the world: a piece behind a
    // hill or a house is still hidden). This moves the transparent-queue renderers (queue > 2500) of a spell hierarchy from Default / TransparentFX onto that layer.
    // Depth-writing pieces (opaque / alpha-test: bark, rocks, ink solids, the 오의 stroke) are NOT moved: the fog reads their own depth (they are not covered), and on
    // the late layer they would also skip InkWash297 and the realm air. An object with a collider is left alone (the layer is its physics layer too).
    // Cost of the move: a routed piece takes no distance fog / 운해 of its own (the smoke columns of EventWash308.AfterFog made the same trade). Presentation only.
    public static class VfxAfterFog308
    {
        public const string LayerName = "VfxAfterFog";
        const int TransparentFx = 1;
        static int _layer = -2;
        // scratch lists, emptied after every call (no Unity object is held between calls)
        static readonly List<Renderer> Renderers = new List<Renderer>(64);
        static readonly List<Material> Materials = new List<Material>(4);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _layer = -2; Renderers.Clear(); Materials.Clear(); }

        /// <summary>The after-fog layer (20 in this project), or -1 when the project has no such layer (then nothing is moved).</summary>
        public static int Layer { get { if (_layer == -2) _layer = LayerMask.NameToLayer(LayerName); return _layer; } }

        /// <summary>Does this renderer draw any material in the transparent queues (it writes no depth the fog could read)?</summary>
        public static bool Transparent(Renderer r)
        {
            if (r == null) return false;
            r.GetSharedMaterials(Materials);
            bool any = false;
            foreach (var m in Materials) if (m != null && m.renderQueue > (int)RenderQueue.GeometryLast) { any = true; break; }
            Materials.Clear();
            return any;
        }

        /// <summary>Moves every transparent renderer under <paramref name="root"/> (inactive ones too) that sits on Default / TransparentFX and has no collider onto the
        /// after-fog layer. Returns how many objects moved.</summary>
        public static int Route(Transform root)
        {
            int layer = Layer;
            if (root == null || layer < 0) return 0;
            int moved = 0;
            root.GetComponentsInChildren(true, Renderers);
            try
            {
                foreach (var r in Renderers)
                {
                    if (r == null) continue;
                    var go = r.gameObject;
                    if ((go.layer != 0 && go.layer != TransparentFx) || !Transparent(r) || go.TryGetComponent<Collider>(out _)) continue;
                    go.layer = layer; moved++;
                }
            }
            finally { Renderers.Clear(); }
            return moved;
        }

        /// <summary>Per-frame form for a spell root: routes when the hierarchy changed (a piece was added / removed) and every <paramref name="every"/> calls anyway
        /// (a material swapped to a transparent one later). <paramref name="count"/> / <paramref name="tick"/> are the caller's memo (start them at -1 / 0).</summary>
        public static void Keep(Transform root, ref int count, ref int tick, int every = 20)
        {
            if (root == null || !Application.isPlaying) return;
            int n = root.hierarchyCount;
            if (n == count && ++tick < every) return;
            count = n; tick = 0;
            Route(root);
        }
    }
}
