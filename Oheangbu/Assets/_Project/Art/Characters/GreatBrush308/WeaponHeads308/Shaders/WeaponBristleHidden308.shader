// #308 D308-127 step 2: what the great brush's bristle slot draws while a weapon head is on - nothing.
// One colour pass that writes no colour and no depth, every vertex placed outside the clip volume (nothing is rasterised).
// No ShadowCaster / DepthOnly / DepthNormals pass: neither the shadows nor the ink post (it reads depth and normals) see
// the hidden bristles. Transparent queue: the copied depth after the opaques never holds them either.
Shader "Oheangbu/WeaponBristleHidden308"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Hidden"
            Tags { "LightMode" = "UniversalForward" }
            ColorMask 0
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input) { Varyings output; output.positionCS = float4(2.0, 2.0, 2.0, 1.0); return output; }
            half4 Frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
