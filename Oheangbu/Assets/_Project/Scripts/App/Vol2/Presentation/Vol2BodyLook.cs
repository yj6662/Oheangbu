using System;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>One body (오행) look: two lightness layers (plan C-2: reading ink L* 18 and dry pigment L* 60), the colour it is laid wet, the edge cue, and the end behaviour. No emission, no glow.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/Vol2/Body Look", fileName = "BodyLook")]
    public sealed class Vol2BodyLook : ScriptableObject
    {
        public Body body;
        [Tooltip("reading ink layer, L* 18")] public Color inkLayer = new Color(0.1725f, 0.1725f, 0.1725f, 1f);
        [Tooltip("the body's wet reference colour (plan C table)")] public Color wetColor = Color.gray;
        [Tooltip("dry pigment layer, L* 60, same a* b* as the body colour")] public Color dryPigment = Color.gray;
        public Vol2EdgeKind edge;
        public Vol2EndBehaviour end;
        [Tooltip("natural-end direction in the cast frame: x = right, y = up, z = forward")] public Vector3 endDirection = Vector3.forward;
        [Tooltip("metres the natural end moves the stroke")] public float endTravel = 0.4f;
        [Tooltip("how ragged the silhouette is (0 clean .. 1 torn)")] [Range(0f, 1f)] public float fray = 0.5f;
        [Tooltip("share of the rim that is broken into gaps (dry rim)")] [Range(0f, 0.6f)] public float rimGap = 0.18f;
        [Tooltip("short form of the plan colour, for the sheet labels")] public string note = "";
    }
}
