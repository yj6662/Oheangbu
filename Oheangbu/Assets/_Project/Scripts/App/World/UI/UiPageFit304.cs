using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>Keeps a 1920x1080 page (V.Page304) inside its parent: localScale = min(1, W/1920, H/1080) (IMPLEMENTATION §4.3,
    /// DESIGN §7: UI 배율 1.3 still shows the whole page). The HUD does not use it (corner anchors).</summary>
    [DisallowMultipleComponent]
    public sealed class UiPageFit304 : MonoBehaviour
    {
        public const float Width = 1920f, Height = 1080f;

        public float Scale { get; private set; } = 1f;

        void LateUpdate()
        {
            var parent = transform.parent as RectTransform;
            if (parent == null) return;
            Vector2 size = parent.rect.size;
            float k = Mathf.Min(1f, size.x / Width, size.y / Height);
            if (k <= 0f || float.IsNaN(k)) return;
            Scale = k;
            if (!Mathf.Approximately(transform.localScale.x, k)) transform.localScale = new Vector3(k, k, 1f);
        }
    }
}
