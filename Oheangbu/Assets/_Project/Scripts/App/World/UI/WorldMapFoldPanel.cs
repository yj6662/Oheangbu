using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    internal sealed class WorldMapFoldPanel
    {
        public RectTransform Root { get; }
        public RectTransform MarkerLayer { get; }
        readonly RawImage paperFront, paperBack, map, fog;
        readonly WorldMapPolylineGraphic surfaceLines, zoneFootprint, zoneDetail;
        readonly Image shadow;
        readonly GameObject front;
        readonly GameObject back;

        public WorldMapFoldPanel(Transform parent, int index, float width, float height, Texture paperTexture,
            Texture mapTexture, Texture fogTexture, Color paperColor)
        {
            Root = PlaytestUiView.Rect("HingedPanel_" + (index + 1), parent, 0, 0, width, height);
            Root.anchorMin = Root.anchorMax = new Vector2(.5f, .5f); Root.pivot = new Vector2(0, .5f);
            front = PlaytestUiView.Stretch("Front", Root).gameObject;
            paperFront = PlaytestUiView.Raw((RectTransform)front.transform, paperTexture, paperColor);
            map = PlaytestUiView.Raw(PlaytestUiView.Stretch("MapInk", front.transform), mapTexture, Color.white);
            surfaceLines = PlaytestUiView.Stretch("SurfaceLines", front.transform).gameObject.AddComponent<WorldMapPolylineGraphic>();
            surfaceLines.raycastTarget = false;
            fog = PlaytestUiView.Raw(PlaytestUiView.Stretch("DiscoveryFog", front.transform), fogTexture, Color.white);
            zoneFootprint = PlaytestUiView.Stretch("ZoneFootprint", front.transform).gameObject.AddComponent<WorldMapPolylineGraphic>();
            zoneFootprint.raycastTarget = false; zoneFootprint.gameObject.SetActive(false);
            zoneDetail = PlaytestUiView.Stretch("ZoneDetail", front.transform).gameObject.AddComponent<WorldMapPolylineGraphic>();
            zoneDetail.raycastTarget = false; zoneDetail.gameObject.SetActive(false);
            MarkerLayer = PlaytestUiView.Stretch("Markers", front.transform);
            shadow = PlaytestUiView.Image(PlaytestUiView.Stretch("FoldShadow", front.transform), Color.clear);
            back = PlaytestUiView.Stretch("Back", Root).gameObject;
            paperBack = PlaytestUiView.Raw((RectTransform)back.transform, paperTexture, new Color(paperColor.r * .83f, paperColor.g * .82f, paperColor.b * .77f, 1));
            SetPaperUv(index);
            SetMapUv(new Rect(0, 0, 1, 1), index);
        }

        public void SetTextures(Texture mapTexture, Texture fogTexture)
        {
            map.texture = mapTexture; fog.texture = fogTexture;
        }

        public void SetSurface(WorldMapLineSpec[] lines, WorldMapProjection projection, Rect view, int index, int count)
            => surfaceLines.SetPaths(lines, projection, view, index, count);

        public void SetZoneDetail(WorldMapLineSpec[] footprint, Vector2[] path, WorldMapProjection projection, Rect view, int index, int count, Color color)
        {
            zoneFootprint.SetPaths(footprint, projection, view, index, count);
            zoneDetail.color = color;
            zoneDetail.SetPath(path, projection, view, 3f, index, count);
        }

        public void SetInterior(bool interior)
        {
            map.gameObject.SetActive(!interior); surfaceLines.gameObject.SetActive(!interior);
            fog.gameObject.SetActive(!interior); zoneFootprint.gameObject.SetActive(interior); zoneDetail.gameObject.SetActive(interior);
        }

        public void SetMapUv(Rect whole, int index)
        {
            float slice = whole.width / 6f;
            Rect uv = new Rect(whole.x + slice * index, whole.y, slice, whole.height);
            map.uvRect = uv; fog.uvRect = uv;
        }

        public void SetFold(float signedAngle, float shadowStrength)
        {
            Root.localEulerAngles = new Vector3(0, signedAngle, 0);
            bool backFacing = Mathf.Abs(signedAngle) > 90f;
            front.SetActive(!backFacing); back.SetActive(backFacing);
            shadow.color = new Color(0.11f, 0.09f, 0.065f, Mathf.Clamp01(shadowStrength) * .30f);
        }

        void SetPaperUv(int index)
        {
            Rect frontUv = new Rect(index / 6f, 0, 1f / 6f, 1);
            paperFront.uvRect = frontUv;
            paperBack.uvRect = new Rect((index + 1f) / 6f, 0, -1f / 6f, 1);
        }
    }
}
