using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 (SPEC-WORLD-REALM-LOOK-308, TEST): pushes the realm look palette to shader globals (_OhRealmLook*), the same way WorldLookDriver
    // feeds the ink/paper palette. A scene without this component leaves _OhRealmLook at 0 and every shader renders as before.
    // No singleton, no static state (immutable property ids only), no per-frame work: the values are written on enable / validate and by
    // the editor tool after it rewrites the asset. OnDisable switches the look off so nothing leaks into the next scene (domain reload is off).
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class RealmLookDriver308 : MonoBehaviour
    {
        [SerializeField] private RealmLookPaletteSO _palette;

        private static readonly int StrengthId = Shader.PropertyToID("_OhRealmLook");
        private static readonly int MapId = Shader.PropertyToID("_OhRealmLookMap");
        private static readonly int RectId = Shader.PropertyToID("_OhRealmLookRect");
        private static readonly int AmountsId = Shader.PropertyToID("_OhRealmLookAmounts");
        private static readonly int SlopeId = Shader.PropertyToID("_OhRealmLookSlope");
        private static readonly int StrataId = Shader.PropertyToID("_OhRealmLookStrata");
        private static readonly int FaceId = Shader.PropertyToID("_OhRealmLookFace");
        private static readonly int LiftId = Shader.PropertyToID("_OhRealmLookLift");
        private static readonly int JointsId = Shader.PropertyToID("_OhRealmLookJoints");
        private static readonly int GroundId = Shader.PropertyToID("_OhRealmGround");
        private static readonly int RockId = Shader.PropertyToID("_OhRealmRock");
        private static readonly int GrassId = Shader.PropertyToID("_OhRealmGrass");
        private static readonly int FoliageId = Shader.PropertyToID("_OhRealmFoliage");

        // map channel order: r Cheongrim, g Jeokro, b Cheolong, a Hyeongang, remainder Hwanggyeong
        private const int Channels = 5;
        private readonly Vector4[] _ground = new Vector4[Channels], _rock = new Vector4[Channels], _grass = new Vector4[Channels], _foliage = new Vector4[Channels];

        public RealmLookPaletteSO Palette { get => _palette; set { _palette = value; if (isActiveAndEnabled) Apply(); } }
        /// <summary>True when the last Apply switched the look on (palette, map and a positive strength).</summary>
        public bool Live { get; private set; }

        public static int Channel(RealmId id)
        {
            switch (id)
            {
                case RealmId.Cheongrim: return 0;
                case RealmId.Jeokro: return 1;
                case RealmId.Cheolong: return 2;
                case RealmId.Hyeongang: return 3;
                default: return 4;
            }
        }

        // hue at unit luminance (linear) times the role's value; w = the realm's share of the role amount
        private static Vector4 Wash(Color srgb, float value, float amount)
        {
            Color c = srgb.linear;
            float luma = Mathf.Max(.02f, .2126f * c.r + .7152f * c.g + .0722f * c.b);
            float k = Mathf.Max(0f, value) / luma;
            return new Vector4(c.r * k, c.g * k, c.b * k, Mathf.Clamp01(amount));
        }

        private void OnEnable() => Apply();
        private void OnValidate() { if (isActiveAndEnabled) Apply(); }
        private void OnDisable() { Shader.SetGlobalFloat(StrengthId, 0f); Live = false; }

        public void Apply()
        {
            var p = _palette;
            if (p == null || p.Map == null || p.Strength <= 0f || p.Realms == null || p.Realms.Length == 0 || p.Rect.z <= 0f || p.Rect.w <= 0f)
            {
                Shader.SetGlobalFloat(StrengthId, 0f); Live = false; return;
            }
            var neutral = new Vector4(1f, 1f, 1f, 0f);
            for (int i = 0; i < Channels; i++) _ground[i] = _rock[i] = _grass[i] = _foliage[i] = neutral;
            foreach (var r in p.Realms)
            {
                int i = Channel(r.Id);
                _ground[i] = Wash(r.Ground, r.GroundValue, r.Amount);
                _rock[i] = Wash(r.Rock, r.RockValue, r.Amount);
                _grass[i] = Wash(r.Grass, r.GrassValue, r.Amount);
                _foliage[i] = Wash(r.Foliage, r.FoliageValue, r.Amount);
            }
            Shader.SetGlobalTexture(MapId, p.Map);
            Shader.SetGlobalVector(RectId, new Vector4(p.Rect.x, p.Rect.y, 1f / p.Rect.z, 1f / p.Rect.w));
            Shader.SetGlobalVector(AmountsId, p.Amounts);
            Shader.SetGlobalVector(SlopeId, p.Slope);
            Shader.SetGlobalVector(StrataId, p.Strata);
            Shader.SetGlobalVector(FaceId, p.Face);
            Shader.SetGlobalVector(LiftId, p.Lift);
            Shader.SetGlobalVector(JointsId, p.Joints);
            Shader.SetGlobalVectorArray(GroundId, _ground);
            Shader.SetGlobalVectorArray(RockId, _rock);
            Shader.SetGlobalVectorArray(GrassId, _grass);
            Shader.SetGlobalVectorArray(FoliageId, _foliage);
            Shader.SetGlobalFloat(StrengthId, Mathf.Clamp01(p.Strength));
            Live = true;
        }
    }
}
