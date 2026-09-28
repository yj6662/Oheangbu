// Editor diagnostic only: never assigned to a scene material or renderer.
Shader "Hidden/Oheangbu/Study/LeafPaddingMipReadback"
{
    Properties
    {
        _MainTex("Imported source texture", 2D) = "white" {}
        _ReadbackMip("Exact source mip", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off ColorMask RGBA
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture2D<float4> _MainTex;
            float _ReadbackMip;
            float4 frag(v2f_img input) : SV_Target
            {
                // SV_POSITION is at pixel centres; integer truncation yields0..size-1.
                // Target size equals this mip's dimensions. Load avoids filtering,
                // fractional LOD and UV precision at sparse alpha boundaries.
                return _MainTex.Load(int3(int2(input.pos.xy), int(_ReadbackMip)));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
