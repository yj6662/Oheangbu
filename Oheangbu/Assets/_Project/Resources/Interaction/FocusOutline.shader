Shader "Oheangbu/Interaction/FocusOutline" {
 Properties { _OutlineColor("Outline",Color)=(.78,.68,.43,1) _Width("Pixel width",Float)=2 _Radial("Open prop silhouette",Float)=0 _CenterWS("World center",Vector)=(0,0,0,0) }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20"}
 Pass {
 Tags {"LightMode"="SRPDefaultUnlit"}
 Stencil {Ref 64 ReadMask 64 WriteMask 0 Comp NotEqual Pass Keep}
 Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _OutlineColor; float4 _CenterWS; float _Width; float _Radial;
 CBUFFER_END
 struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
 struct Varyings {float4 positionCS:SV_POSITION;};
 Varyings Vert(Attributes v){
 Varyings o;float3 ws=TransformObjectToWorld(v.positionOS.xyz);float3 n=TransformObjectToWorldNormal(v.normalOS);
 float4 p=TransformWorldToHClip(ws);float4 q=TransformWorldToHClip(ws+n);
 float4 center=TransformWorldToHClip(_CenterWS.xyz);
 float2 direction=lerp(q.xy/q.w-p.xy/p.w,p.xy/p.w-center.xy/center.w,_Radial);direction*= _ScreenParams.xy;
 direction=direction/max(length(direction),.0001);
 p.xy+=direction*(_Width*2/_ScreenParams.xy)*p.w;o.positionCS=p;return o;}
 half4 Frag(Varyings i):SV_Target{return _OutlineColor;}
 ENDHLSL
 }
 }
}
