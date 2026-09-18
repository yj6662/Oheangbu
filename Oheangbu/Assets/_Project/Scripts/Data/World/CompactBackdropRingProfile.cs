using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>
    /// Distant backdrop mountain ring placement, outside the compact world and its context skirt.
    /// Render-only anonymous 원산: it neither implements nor represents the LDB dominant landmarks
    /// (사산 / 철옹 성곽 산등성이 / 산군=인왕산). Never participates in terrain height or collision.
    /// [SPEC-COMPACT-BACKDROP-RING]
    /// </summary>
    [CreateAssetMenu(menuName="Oheangbu/World/Compact Backdrop Ring")]
    public sealed class CompactBackdropRingProfile : ScriptableObject
    {
        [Serializable] public sealed class Ring
        {
            public string Name="Inner";
            [Tooltip("Instance count on this ellipse.")]
            public int Count=16;
            [Tooltip("Ellipse radii in metres, east and north. Must clear the context skirt in every direction.")]
            public float RadiusX=4700, RadiusZ=5600;
            [Tooltip("Rotates the whole ring so the two rings do not align their peaks.")]
            public float AngleOffsetDegrees;
            [Tooltip("Target world WIDTH in metres before per-instance variation. Height and depth follow the model's own proportions.")]
            public float BaseWidth=1400f;
            [Tooltip("Height multiplier applied on top of the model's natural proportion. 1 keeps the real mountain silhouette; above ~1.3 it starts to read as a needle.")]
            public float HeightEmphasis=1f;
            [Tooltip("Phase of the radial wobble so each ring breaks its circle differently.")]
            public float WobblePhase=.2f;
        }

        [Tooltip("Rings are drawn outermost-last; each is an independent ellipse.")]
        public Ring[] Rings=
        {
            new Ring{Name="Inner",Count=16,RadiusX=2750,RadiusZ=3750,AngleOffsetDegrees=0,BaseWidth=1400,HeightEmphasis=1f,WobblePhase=.2f},
            new Ring{Name="Outer",Count=20,RadiusX=4100,RadiusZ=5200,AngleOffsetDegrees=9,BaseWidth=2000,HeightEmphasis=1f,WobblePhase=1.7f}
        };

        [Tooltip("Ring centre in world XZ. The compact world is centred on the origin.")]
        public Vector2 RingCentre=Vector2.zero;
        [Tooltip("World Y the FOOT of each mountain is seated at, not its centre. Sunk below the terrain skirt so no cut base is ever visible.")]
        public float BaseY=-260f;

        // Deterministic trigonometric variation, not Perlin: the same index always yields the same
        // mountain, so a rebuild reproduces the ring exactly. x=amplitude, y=frequency (and z=frequency
        // for the three size terms, whose x=floor and y=span).
        [Tooltip("Radial wobble: x=amplitude, y=frequency.")]
        public Vector2 Wobble=new Vector2(.07f,2.31f);
        [Tooltip("Width variation: x=floor, y=span, z=frequency.")]
        public Vector3 WidthVariation=new Vector3(.86f,.22f,1.73f);
        [Tooltip("Height variation: x=floor, y=span, z=frequency.")]
        public Vector3 HeightVariation=new Vector3(.84f,.28f,1.37f);
        [Tooltip("Depth variation: x=floor, y=span, z=frequency.")]
        public Vector3 DepthVariation=new Vector3(.88f,.18f,.91f);
        [Tooltip("Index modulo x equals y selects the rounded peak model; every other index uses the ridge.")]
        public Vector2Int PeakSelect=new Vector2Int(3,1);

        // The ring must overlap the skirt in plan view — that overlap is what hides the models' cut
        // bases behind real terrain. What must NOT happen is the backdrop standing INSIDE the skirt
        // as visible geometry. So the rule is vertical, not horizontal: where a backdrop instance
        // overlaps the skirt footprint, its surface there has to sit below the skirt, occluded.
        [Tooltip("Context skirt footprint in world XZ. Overlap is allowed and intended; the skirt hides the model bases.")]
        public Rect ForbiddenRect=new Rect(-2109,-3221,4218,6442);
        [Tooltip("Metres a summit must clear the measured skirt top by, so it reads as a distant peak rather than a hill standing in the field.")]
        public float SummitClearance=120f;

        // Backdrop-only atmosphere. The compact profile's own FarAirRange saturates at 3400 m, so
        // every ring past that distance would resolve to one identical tone and the layered depth
        // would collapse. Widening it HERE, on the backdrop material copy only, keeps the existing
        // terrain materials untouched.
        [Tooltip("Backdrop-only far atmosphere range in metres. The shared compact profile saturates at 3400 m.")]
        public Vector2 FarAirRange=new Vector2(3000,9000);
        [Tooltip("Backdrop-only mid/far atmosphere wash strengths.")]
        public Vector2 AirStrengths=new Vector2(.18f,.64f);
        [Tooltip("Backdrop-only mid atmosphere range in metres.")]
        public Vector2 MidAirRange=new Vector2(1500,4000);
        [Tooltip("Optional slope correction if non-uniform scaling shifts the rock/paper split away from the terrain mountains. Zero keeps the material's own value.")]
        public Vector2 MountainSlopeDegrees=Vector2.zero;

        static readonly int FarAirId=Shader.PropertyToID("_CIFarAirRange");
        static readonly int MidAirId=Shader.PropertyToID("_CIMidAirRange");
        static readonly int StrengthsId=Shader.PropertyToID("_CIAirStrengths");
        static readonly int SlopeId=Shader.PropertyToID("_CIMountainSlope");
        static readonly int OverrideId=Shader.PropertyToID("_CIAtmosphereOverride");

        /// <summary>Writes only the backdrop atmosphere overrides. Guarded so a missing property is skipped, never thrown.</summary>
        public void Apply(Material material)
        {
            if(material==null)return;
            if(material.HasProperty(OverrideId))material.SetFloat(OverrideId,1f);
            if(material.HasProperty(FarAirId))material.SetVector(FarAirId,new Vector4(FarAirRange.x,FarAirRange.y,0,0));
            if(material.HasProperty(MidAirId))material.SetVector(MidAirId,new Vector4(MidAirRange.x,MidAirRange.y,0,0));
            if(material.HasProperty(StrengthsId))material.SetVector(StrengthsId,new Vector4(AirStrengths.x,AirStrengths.y,0,0));
            if(MountainSlopeDegrees!=Vector2.zero&&material.HasProperty(SlopeId))
                material.SetVector(SlopeId,new Vector4(MountainSlopeDegrees.x,MountainSlopeDegrees.y,0,0));
        }

        /// <summary>
        /// Deterministic placement for one index of one ring. Shared by probe, build and verify.
        /// <paramref name="sourceSize"/> is the model's own measured extent: height and depth are
        /// derived from it so the real mountain proportions survive. Driving all three axes from
        /// independent targets is what turns a low ridge model into a needle.
        /// </summary>
        public void Evaluate(Ring ring,int index,Vector3 sourceSize,out Vector3 position,out Vector3 targetSize,out float yaw,out bool usePeak)
        {
            float angle=ring.AngleOffsetDegrees+index*(360f/Mathf.Max(1,ring.Count));
            float radians=angle*Mathf.Deg2Rad;
            float wobble=1f+Wobble.x*Mathf.Sin(index*Wobble.y+ring.WobblePhase);
            position=new Vector3(
                RingCentre.x+Mathf.Sin(radians)*ring.RadiusX*wobble,
                BaseY,
                RingCentre.y+Mathf.Cos(radians)*ring.RadiusZ*wobble);
            usePeak=PeakSelect.x>0&&index%PeakSelect.x==PeakSelect.y;

            float widthVary=WidthVariation.x+WidthVariation.y*Mathf.Abs(Mathf.Sin(index*WidthVariation.z));
            float heightVary=HeightVariation.x+HeightVariation.y*Mathf.Abs(Mathf.Cos(index*HeightVariation.z));
            float depthVary=DepthVariation.x+DepthVariation.y*Mathf.Abs(Mathf.Sin(index*DepthVariation.z));

            float width=ring.BaseWidth*widthVary;
            // Natural proportions of the source model, measured with yaw cleared.
            float heightPerWidth=sourceSize.x>Mathf.Epsilon?sourceSize.y/sourceSize.x:1f;
            float depthPerWidth=sourceSize.x>Mathf.Epsilon?sourceSize.z/sourceSize.x:1f;
            targetSize=new Vector3(
                width,
                width*heightPerWidth*Mathf.Max(.05f,ring.HeightEmphasis)*heightVary,
                width*depthPerWidth*depthVary);
            yaw=angle+180f;
        }
    }
}
