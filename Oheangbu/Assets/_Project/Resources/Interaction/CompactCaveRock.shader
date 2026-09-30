Shader "Oheangbu/Compact/CaveRock" {
 Properties { _BaseColor("Rock pigment",Color)=(.29,.31,.30,1) _BaseMap("Base",2D)="white"{} _Cutoff("Cutoff",Float)=.5 _Ambient("Cave fill",Range(0,1))=.24 [HideInInspector] _Cull("Cull",Float)=0 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 Cull [_Cull]
 Pass {
 Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
 #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
 #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor;float4 _BaseMap_ST;float _Cutoff;float _Ambient;
 CBUFFER_END
 struct A {float4 p:POSITION;float3 n:NORMAL;};struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;};
 V vert(A i){V o;o.w=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);return o;}
 float hash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
 float noise(float3 p){float3 q=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(lerp(hash(q),hash(q+float3(1,0,0)),f.x),lerp(hash(q+float3(0,1,0)),hash(q+float3(1,1,0)),f.x),f.y),lerp(lerp(hash(q+float3(0,0,1)),hash(q+float3(1,0,1)),f.x),lerp(hash(q+float3(0,1,1)),hash(q+1),f.x),f.y),f.z);}
 half4 frag(V i, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target {
 float3 n=normalize(i.n)*IS_FRONT_VFACE(face,1,-1);float broad=noise(i.w*.72);float strata=sin(i.w.y*3+i.w.x*.58+noise(i.w*1.1)*6);
 float seam=1-smoothstep(.015,.12,abs(strata));float dry=noise(i.w*float3(3,.65,3));
 float chips=noise(i.w*2.3);
 float pigment=.5+broad*.55+dry*.25+chips*.24-seam*.10*smoothstep(.3,.65,dry);
 float e=.07;float3 q=i.w*1.7;float f=noise(q);float3 grad=float3(noise(q+float3(e,0,0))-f,noise(q+float3(0,e,0))-f,noise(q+float3(0,0,e))-f)/e;
 n=normalize(n-(grad-n*dot(n,grad))*.55);
 Light sun=GetMainLight(TransformWorldToShadowCoord(i.w));
 float3 illumination=_Ambient*float3(.78,.87,1)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation*.28;
 #if defined(_ADDITIONAL_LIGHTS)
 InputData inputData=(InputData)0;inputData.positionWS=i.w;inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);
 uint count=GetAdditionalLightsCount();LIGHT_LOOP_BEGIN(count)
 Light l=GetAdditionalLight(lightIndex,i.w);illumination+=l.color*l.distanceAttenuation*l.shadowAttenuation*(.1+.9*saturate(dot(n,l.direction)));
 LIGHT_LOOP_END
 #endif
 return half4(_BaseColor.rgb*pigment*illumination,1);
 }
 ENDHLSL
 }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 UsePass "Universal Render Pipeline/Lit/DepthNormals"
 }
}
