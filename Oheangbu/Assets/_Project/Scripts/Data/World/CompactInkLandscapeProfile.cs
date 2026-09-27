using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>Opt-in compact-map material authoring; never changes a shared palette or exposure.</summary>
    [CreateAssetMenu(menuName="Oheangbu/World/Compact Ink Landscape")]
    public sealed class CompactInkLandscapeProfile : ScriptableObject
    {
        public Color Ink=new Color(.165f,.149f,.133f);
        public Color Paper=new Color(.969f,.945f,.894f);
        public Color Air=new Color(.9f,.88f,.83f);
        public Vector2 DetailRange=new Vector2(30,150);
        public Vector2 RockDetailRange=new Vector2(150,350);
        public Vector2 MountainRange=new Vector2(350,900);
        public Vector2 AirRange=new Vector2(900,2200);
        public Vector4 GroundTones=new Vector4(.27f,.56f,.24f,.48f);
        [Tooltip("Opt-in dark foreground revision. Existing profiles retain the original ink response.")]
        public bool DarkNearMode;
        public Vector2 MountainTones=new Vector2(.06f,.18f);
        [Tooltip("Slope in degrees: mountain ink begins, then fully replaces ordinary ground. Authored paths take priority.")]
        public Vector2 MountainSlopeDegrees=new Vector2(12,37);
        [Tooltip("Use independent atmosphere ranges without moving soil detail or foliage chroma ranges.")]
        public bool OverrideAtmosphere;
        public Vector2 MidAirRange=new Vector2(500,1500);
        public Vector2 FarAirRange=new Vector2(1500,3400);
        public Vector2 AirStrengths=new Vector2(.18f,.64f);
        [Tooltip("Opt-in world-space pigment and brushwork. Existing profiles keep their current surface response.")]
        public bool PainterlyMode;
        [Tooltip("Minimum/maximum stroke spacing, then full width, in world metres.")]
        public Vector4 StrokeSpacingWidth=new Vector4(20,40,2,7);
        [Tooltip("Stroke fade start/end in metres, maximum subtractive tone, reserved.")]
        public Vector4 StrokeFadeStrength=new Vector4(200,1400,.02f,0);
        [Tooltip("Maximum mountain broad-tone perturbation, ground pigment contrast, reserved, reserved.")]
        public Vector4 PigmentContrast=new Vector4(.03f,.65f,0,0);
        [Tooltip("Foliage paint transition start/end, RGB mip bias, maximum contrast compression.")]
        public Vector4 FoliagePainterly=new Vector4(30,180,1.5f,.3f);
        [Tooltip("Build mountain faces from overlapping landscape-sized ink strokes instead of fine brush marks.")]
        public bool BroadBrushMode;
        [Tooltip("Minimum/maximum full stroke width, then full length, in world metres.")]
        public Vector4 BroadBrushScale=new Vector4(80,240,420,1000);
        [Tooltip("Ink opacity, edge feather fraction, secondary coat load, world-space warp fraction.")]
        public Vector4 BroadBrushCoverage=new Vector4(.90f,.18f,.76f,.20f);
        [Tooltip("Dark/soft ink-coat tone. The uncovered mountain keeps MountainTones.")]
        public Vector2 BroadBrushTones=new Vector2(.015f,.07f);
        [Tooltip("Two broad shading thresholds, transition width, and three-band shading amount.")]
        public Vector4 BroadBrushBands=new Vector4(.33f,.66f,.12f,.8f);
        [Range(0,1)] public float NearTextureContrast=.22f;
        [Range(0,1)] public float NormalStrength=.5f;
        [Range(0,1)] public float BaseSaturation=.2f;
        [Range(0,1)] public float RegionalColourContribution=.2f;
        [Range(0,1)] public float OpenGrassDensity=.25f;
        [Range(0,1)] public float OpenShrubDensity=.35f;
        public void Apply(Material material)
        {
            if(material==null||!material.HasProperty("_CIEnabled"))return;
            material.SetFloat("_CIEnabled",1);
            material.SetColor("_CIInk",Ink);material.SetColor("_CIPaper",Paper);material.SetColor("_CIAir",Air);
            material.SetVector("_CIDetailRange",new Vector4(DetailRange.x,DetailRange.y,0,0));
            material.SetVector("_CIRockRange",new Vector4(RockDetailRange.x,RockDetailRange.y,0,0));
            material.SetVector("_CIMountainRange",new Vector4(MountainRange.x,MountainRange.y,0,0));
            material.SetVector("_CIAirRange",new Vector4(AirRange.x,AirRange.y,0,0));
            material.SetVector("_CITones",GroundTones);
            material.SetFloat("_CIDarkNear",DarkNearMode?1:0);
            material.SetVector("_CIMountainTones",new Vector4(MountainTones.x,MountainTones.y,0,0));
            material.SetVector("_CIMountainSlope",new Vector4(MountainSlopeDegrees.x,MountainSlopeDegrees.y,0,0));
            material.SetFloat("_CIAtmosphereOverride",OverrideAtmosphere?1:0);
            material.SetFloat("_CIPainterly",PainterlyMode?1:0);
            material.SetVector("_CIMidAirRange",new Vector4(MidAirRange.x,MidAirRange.y,0,0));
            material.SetVector("_CIFarAirRange",new Vector4(FarAirRange.x,FarAirRange.y,0,0));
            material.SetVector("_CIAirStrengths",new Vector4(AirStrengths.x,AirStrengths.y,0,0));
            material.SetVector("_CIStrokeSpacingWidth",StrokeSpacingWidth);
            material.SetVector("_CIStrokeFadeStrength",StrokeFadeStrength);
            material.SetVector("_CIPigmentContrast",PigmentContrast);
            material.SetVector("_CIFoliagePainterly",FoliagePainterly);
            material.SetFloat("_CIBroadBrush",BroadBrushMode?1:0);
            material.SetVector("_CIBroadBrushScale",BroadBrushScale);
            material.SetVector("_CIBroadBrushCoverage",BroadBrushCoverage);
            material.SetVector("_CIBroadBrushTones",new Vector4(BroadBrushTones.x,BroadBrushTones.y,0,0));
            material.SetVector("_CIBroadBrushBands",BroadBrushBands);
            material.SetFloat("_CIDetailContrast",NearTextureContrast);material.SetFloat("_CINormalStrength",NormalStrength);
        }

        // CPU parity for CIEvaluateMountainTone / CIAtmosphere in CompactInkLandscape.hlsl.
        // Feed fixed surface pigment/light samples to audit distance response without changing geometry.
        public float EvaluateDarkMountainTone(float broadShade,float detailInk,float distance)
        {
            return EvaluateDarkMountainTone(broadShade,detailInk,distance,.5f,0);
        }

        // broadPigment and brushInk are fixed world-space samples in [0,1]. Dry paper gaps
        // are already removed from brushInk; they never add a positive detail layer.
        public float EvaluateDarkMountainTone(float broadShade,float detailInk,float distance,float broadPigment,float brushInk)
        {
            if(BroadBrushMode)return EvaluateBroadMountainTone(broadShade,broadPigment,brushInk);
            float nearDetail=1-Progress(distance,DetailRange.x,RockDetailRange.y);
            float baseTone=Mathf.Lerp(MountainTones.x,MountainTones.y,Mathf.Clamp01(broadShade));
            float brush=0;
            if(PainterlyMode)
            {
                baseTone=Mathf.Clamp(baseTone+(Mathf.Clamp01(broadPigment)*2-1)*Mathf.Max(0,PigmentContrast.x),
                    Mathf.Min(MountainTones.x,MountainTones.y),Mathf.Max(MountainTones.x,MountainTones.y));
                brush=Mathf.Clamp01(brushInk)*Mathf.Max(0,StrokeFadeStrength.z)
                    *(1-Progress(distance,StrokeFadeStrength.x,StrokeFadeStrength.y));
            }
            return Mathf.Clamp01(baseTone-.035f*Mathf.Clamp01(detailInk)*nearDetail-brush);
        }

        // CPU parity with CIEvaluateBroadMountainTone. Large coats persist at every distance;
        // the one shared atmosphere supplies the entire distance response in this mode.
        // coatCoverage is the combined world-space coverage before BroadBrushCoverage.x opacity.
        public float EvaluateBroadMountainTone(float broadShade,float broadPigment,float coatCoverage)
        {
            float shade=Mathf.Clamp01(broadShade),halfWidth=Mathf.Max(.001f,BroadBrushBands.z*.5f);
            float bands=.5f*SoftStep(shade,BroadBrushBands.x-halfWidth,BroadBrushBands.x+halfWidth)
                +.5f*SoftStep(shade,BroadBrushBands.y-halfWidth,BroadBrushBands.y+halfWidth);
            float banded=Mathf.Lerp(shade,bands,Mathf.Clamp01(BroadBrushBands.w));
            float baseTone=Mathf.Lerp(MountainTones.x,MountainTones.y,banded);
            float pigment=Mathf.Clamp01(broadPigment);
            baseTone=Mathf.Clamp(baseTone+(pigment-.5f)*.012f,
                Mathf.Min(MountainTones.x,MountainTones.y),Mathf.Max(MountainTones.x,MountainTones.y));
            float coatTone=Mathf.Lerp(BroadBrushTones.x,BroadBrushTones.y,Mathf.Clamp01(banded*.65f+pigment*.35f));
            return Mathf.Clamp01(Mathf.Lerp(baseTone,Mathf.Min(baseTone,coatTone),
                Mathf.Clamp01(coatCoverage)*Mathf.Clamp01(BroadBrushCoverage.x)));
        }

        public float EvaluateAtmosphereWash(float distance)
        {
            if(OverrideAtmosphere)return Mathf.Clamp01(Mathf.Max(0,AirStrengths.x)*Progress(distance,MidAirRange.x,MidAirRange.y)
                +Mathf.Max(0,AirStrengths.y)*Progress(distance,FarAirRange.x,FarAirRange.y));
            return Mathf.Clamp01(.24f*Progress(distance,RockDetailRange.x,MountainRange.y)
                +.68f*Progress(distance,AirRange.x,AirRange.y));
        }

        static float Progress(float distance,float start,float end)
        {
            float t=Mathf.Clamp01((distance-start)/(Mathf.Max(start+1,end)-start));
            return t*t*(3-2*t);
        }

        static float SoftStep(float value,float start,float end)
        {
            float t=Mathf.Clamp01((value-start)/Mathf.Max(.000001f,end-start));
            return t*t*(3-2*t);
        }
    }
}
