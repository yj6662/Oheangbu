Shader "Oheangbu/Interaction/FocusMask" {
Properties { _StencilOp("Mask operation",Float)=2 }
SubShader {Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-23"}
Pass {Tags {"LightMode"="SRPDefaultUnlit"}
Stencil {Ref 64 ReadMask 64 WriteMask 64 Comp Always Pass [_StencilOp]}
Cull Off ZWrite Off ZTest LEqual ColorMask 0
HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct a {float4 pos:POSITION;};struct v {float4 pos:SV_POSITION;};
v Vert(a i){v o;o.pos=TransformObjectToHClip(i.pos.xyz);return o;}
half4 Frag(v i):SV_Target{return 0;}
ENDHLSL
}}
}
