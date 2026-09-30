using UnityEngine;

namespace Oheangbu.App.World.UI
{
    // #306 contract between the world map (WorldMapPresenter implements) and the HUD minimap (HudMinimap304 reads). The minimap
    // draws with a second PaperMapSurface material that shares the unfolded map's _MapTex / _FogTex / _CaveDiscovery and has its
    // own _InkTex; the map keeps the discovery state, the HUD only asks for prints of a window. Windows are normalized map uv
    // rects (the same space as the sheet's _WorldUv), square in metres and NOT clamped to the map, so the player stays centred.
    public interface IMapMiniSource304
    {
        /// <summary>Initialized and the saved walked-land state is loaded (before that the minimap stays hidden).</summary>
        bool MiniReady { get; }
        Texture MapTex { get; }
        Texture FogTex { get; }
        /// <summary>The minimap's own ink layer (walked roads, cave detail lines), drawn by PrintMini.</summary>
        Texture InkTex { get; }
        /// <summary>Bumps when a print would change: walked land (fog), the known line set (shortcuts) or the zone.</summary>
        int MiniRevision { get; }
        /// <summary>Inside a cave / interior zone (the minimap closes to its cave range and shows the walked cave plan).</summary>
        bool MiniInterior { get; }
        /// <summary>A new PaperMapSurface material set up for walked land only (unwalked = bare sheet, no rivers / borders /
        /// objective). The caller owns it and destroys it. Null while the map is not initialized.</summary>
        Material CreateMiniMaterial();
        /// <summary>Square window of ±rangeMetres around centre (normalized map uv, unclamped).</summary>
        Rect WindowUv(Vector3 centre, float rangeMetres);
        /// <summary>World position inside a window as 0..1 (x east, y north); false outside it.</summary>
        bool TryWorldToWindow(Vector3 world, Rect window, out Vector2 window01);
        /// <summary>Redraws InkTex for inkWindow, displayed inkDisplayPx wide (stroke widths are in display px).</summary>
        void PrintMini(Rect inkWindow, float inkDisplayPx);
        /// <summary>Applies the window, zone, cave plan and terrain tile of the last print to a mini material (call it for the
        /// material and for its masked rendering copy).</summary>
        void ApplyMini(Material mini, Rect inkWindow);
        /// <summary>Player (or the seated vehicle) position and heading in degrees clockwise from north, as the unfolded map shows it.</summary>
        void MiniPose(out Vector3 world, out float headingDeg);
        /// <summary>The HUD minimap root becomes WorldMapPresenter.MiniRoot ("PersistentMinimap", smoke-test name).</summary>
        void AttachMiniRoot(RectTransform root);
        void DetachMiniRoot(RectTransform root);
    }
}
