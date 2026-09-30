Shader "Oheangbu/Reworld292/River294"
{
 Properties
 {
  _Deep("Deep green ink",Color)=(.055,.105,.095,1)
  _Shallow("Shallow riverbed tint",Color)=(.25,.29,.23,1)
  _Sky("Muted sky reflection",Color)=(.48,.56,.55,1)
  _Bed("CC0 stony riverbed",2D)="gray"{}
  _FlowSpeed("Current speed",Float)=.7
 }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20"}
  Pass
  {
   Tags{"LightMode"="UniversalForward"} Cull Off ZWrite On Blend SrcAlpha OneMinusSrcAlpha
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma target 3.5
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_Bed);SAMPLER(sampler_Bed);
   CBUFFER_START(UnityPerMaterial)
   half4 _Deep,_Shallow,_Sky;float _FlowSpeed;
   CBUFFER_END
   struct A{float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;float2 flow:TEXCOORD1;half4 color:COLOR;};
   struct V{float4 clip:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;float2 flow:TEXCOORD3;half4 color:COLOR;};
   V Vert(A i){V o;o.world=TransformObjectToWorld(i.vertex.xyz);o.clip=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normal);o.uv=i.uv;o.flow=i.flow;o.color=i.color;return o;}
   float Hash294(float2 p){float3 q=frac(float3(p.xyx)*.1031);q+=dot(q,q.yzx+33.33);return frac((q.x+q.y)*q.z);}
   float Noise294(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(Hash294(a),Hash294(a+float2(1,0)),f.x),lerp(Hash294(a+float2(0,1)),Hash294(a+1),f.x),f.y);}
   half4 Frag(V i):SV_Target
   {
    float d=distance(_WorldSpaceCameraPos,i.world);float detail=1-smoothstep(25,130,d);
    float speed=lerp(.24,1.5,saturate(i.flow.y*6))*_FlowSpeed;
    float t=i.uv.y-i.flow.x*_Time.y*speed;
    float w1=(Noise294(float2(i.uv.x*.8,t*1.3))*2-1),w2=(Noise294(float2(i.uv.x*1.7+8,t*.7+19))*2-1);
    half3 n=normalize(lerp(half3(0,1,0),i.normal,.3)+half3(w1*.13,0,w2*.13)*detail);
    half3 view=normalize(_WorldSpaceCameraPos-i.world);half fresnel=pow(1-saturate(dot(n,view)),4);
    float depth=i.color.r*1.5;
    half3 bed=SAMPLE_TEXTURE2D(_Bed,sampler_Bed,i.world.xz*.55+half2(w1,w2)*.012*detail).rgb;
    half grey=dot(bed,half3(.2126,.7152,.0722));bed=lerp(grey.xxx,bed,.35)*_Shallow.rgb*2.4;
    half3 colour=lerp(bed,_Deep.rgb,1-exp(-depth*1.4));
    Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
    colour*=.68+.35*saturate(dot(n,sun.direction))*sun.shadowAttenuation;
    colour=lerp(colour,_Sky.rgb,(.24+fresnel*.55)*lerp(.55,1,sun.shadowAttenuation));
    half spec=pow(saturate(dot(n,normalize(view+sun.direction))),100)*.26*sun.shadowAttenuation*detail;
    colour+=min(sun.color,1.5)*spec;
    // Small broken streaks at shallow, steep riffles; no continuous white shoreline outline.
    float riffle=smoothstep(.045,.15,i.flow.y)*(1-smoothstep(.20,.75,depth));
    float streak=smoothstep(.28,.72,w1*.55+w2*.45)*smoothstep(.1,.32,depth)*detail;
    colour=lerp(colour,_Sky.rgb*.85,streak*riffle*.7);
    float edge=smoothstep(0,.85,i.color.b);
    return half4(colour,edge*lerp(.50,.96,saturate(depth*2)));
   }
   ENDHLSL
  }
  Pass
  {
   Name "DepthOnly" Tags{"LightMode"="DepthOnly"} ColorMask R Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A{float4 vertex:POSITION;half4 color:COLOR;};struct V{float4 clip:SV_POSITION;half4 color:COLOR;};
   V Vert(A i){V o;o.clip=TransformObjectToHClip(i.vertex.xyz);o.color=i.color;return o;}
   half4 Frag(V i):SV_Target{clip(i.color.b-.3);return 0;}
   ENDHLSL
  }
 }
}
