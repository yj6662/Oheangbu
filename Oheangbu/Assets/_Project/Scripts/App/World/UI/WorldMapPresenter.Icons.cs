using UnityEngine;

namespace Oheangbu.App.World.UI
{
    // #304: the icon mode no longer hides text. Theme.Icons (CompactUiProfileSO) is only a source of support pictures:
    // the inn / cave / mountain drawings for marks of 34 px and more (legend 44, map marks). Smaller marks (the places
    // list 30 px, the HUD bearing line 32 px) use the flat D13 pictograms of MapStyle304SO.
    public sealed partial class WorldMapPresenter
    {
        static Sprite Pick(Sprite a, Sprite b) => a != null ? a : b;
        static Sprite Pick(Sprite a, Sprite b, Sprite c) => a != null ? a : b != null ? b : c;

        /// <summary>Drawing for a mark of 34 px or more (DESIGN §5.13 그림: 그림이 곳의 생김이다).</summary>
        Sprite PictureFor(WorldMapMarkerKind kind)
        {
            var icons = dependencies != null ? dependencies.Icons : null;
            Sprite inn = icons != null ? icons.Inn : null, cave = icons != null ? icons.Cave : null, mountain = icons != null ? icons.Mountain : null;
            switch (kind)
            {
                case WorldMapMarkerKind.Rest: case WorldMapMarkerKind.Checkpoint: return Pick(inn, mapStyle.PictRest, style.Sprites.Disc);
                case WorldMapMarkerKind.Mountain: return Pick(mountain, mapStyle.PictMountain, style.Sprites.Disc);
                case WorldMapMarkerKind.Gate: return Pick(mapStyle.PictGate, cave, style.Sprites.Disc);
                case WorldMapMarkerKind.Settlement: return Pick(mapStyle.PictVillage, inn, style.Sprites.Disc);
                case WorldMapMarkerKind.Drop: return Pick(mapStyle.Coin, style.Sprites.Disc);
                default: return Pick(cave, mapStyle.PictCave, style.Sprites.Disc);
            }
        }

        /// <summary>D13 flat pictogram for 30~32 px; falls back to the drawing when the map asset is missing.</summary>
        Sprite PictogramFor(WorldMapMarkerKind kind) => Pick(mapStyle.Pictogram(kind), PictureFor(kind));

        static MapMarkerKind304 KindOf(WorldMapMarkerKind kind)
        {
            switch (kind)
            {
                case WorldMapMarkerKind.Rest: case WorldMapMarkerKind.Checkpoint: return MapMarkerKind304.Rest;
                case WorldMapMarkerKind.Drop: return MapMarkerKind304.Coin;
                case WorldMapMarkerKind.Pin: return MapMarkerKind304.Pin;
                default: return MapMarkerKind304.Place;
            }
        }

        /// <summary>Mark size (page px) of a data marker on the map.</summary>
        float MarkSize(WorldMapMarkerKind kind) => kind == WorldMapMarkerKind.Rest || kind == WorldMapMarkerKind.Checkpoint ? mapStyle.RestSize : mapStyle.PlaceSize;
    }
}
