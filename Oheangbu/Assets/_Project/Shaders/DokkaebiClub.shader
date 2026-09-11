Shader "Oheangbu/DokkaebiClub"
{
 Properties{
  _BaseMap("Granite color",2D)="white"{}
  _NormalMap("Granite normal",2D)="bump"{}
  _BaseColor("Tint",Color)=(1,1,1,1)
  _Age("Effect clock",Float)=2
  _Ground("Ground",Float)=0
  _Height("Full height",Float)=2.6
  _Leaf("Leaf motion",Float)=0
 }
 SubShader{
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout"}
  HLSLINCLUDE
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_NormalMap);SAMPLER(sampler_NormalMap);
  CBUFFER_START(UnityPerMaterial)
  float4 _BaseMap_ST,_BaseColor;float _Age,_Ground,_Height,_Leaf;
  CBUFFER_END
  struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;};
  struct V{float4 pos:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float4 tangent:TEXCOORD2;float2 uv:TEXCOORD3;float fog:TEXCOORD4;};
  V vert(A a){V o;float3 p=a.positionOS.xyz;p.x+=_Leaf*sin(_Age*2+p.y*4+p.z*8)*.0035*a.uv.y;o.world=TransformObjectToWorld(p);o.pos=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(a.normalOS);o.tangent=float4(TransformObjectToWorldDir(a.tangentOS.xyz),a.tangentOS.w*GetOddNegativeScale());o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.fog=ComputeFogFactor(o.pos.z);return o;}
  void Reveal(V i){float h=(i.world.y-_Ground)/max(.1,_Height);float grain=(sin(i.world.x*71+sin(i.world.z*31))*sin(i.world.y*67))*.012;float shown=saturate((_Age-.2)/1.0);float remain=1-saturate((_Age-3.8)/.8);clip(min(shown,remain)-h-grain);clip(_Age-.201);clip(4.599-_Age);}
  ENDHLSL
  Pass{
   Name "ForwardLit" Tags{"LightMode"="UniversalForward"} Cull Off ZWrite On
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   half4 frag(V i):SV_Target{
    Reveal(i);float3 n=normalize(i.normal);float3 t=i.tangent.xyz;
    if(dot(t,t)>.1){t=normalize(t);float3 b=cross(n,t)*i.tangent.w;float3 map=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,i.uv));map.xy*=.65;n=normalize(t*map.x+b*map.y+n*map.z);}
    Light l=GetMainLight(TransformWorldToShadowCoord(i.world));float3 ambient=max(SampleSH(n),float3(.32,.32,.32));
    float3 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
    color*=ambient+l.color*saturate(dot(n,l.direction))*l.shadowAttenuation*.8;
    float h=(i.world.y-_Ground)/max(.1,_Height);
    float front=min(saturate((_Age-.2)),1-saturate((_Age-3.8)/.8));
    float edge=(1-smoothstep(0,.02,abs(h-front)))*((_Age<1.2||_Age>3.8)?1:0);
    color+=float3(.20,.14,.075)*edge*.15;
    return half4(MixFog(color,i.fog),1);
   }
   ENDHLSL
  }
  Pass{
   Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"} ZWrite On ColorMask 0 Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment depth
   half4 depth(V i):SV_Target{Reveal(i);return 0;}
   ENDHLSL
  }
  Pass{
   Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ZWrite On ColorMask R Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment depth
   half4 depth(V i):SV_Target{Reveal(i);return 0;}
   ENDHLSL
  }
 }
}
