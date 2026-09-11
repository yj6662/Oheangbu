Shader "Oheangbu/FixedWard"
{
 Properties{
  _BaseColor("Color",Color)=(.4,.3,.18,1)
  _Mode("Element",Float)=1
  _WardCenter("Center",Vector)=(0,0,0,0)
  _Radius("Radius",Float)=3
  _Opacity("Opacity",Float)=1
  _Progress("Formation",Float)=1
  _Dissolve("Dissolve",Float)=0
  _EffectTime("Time",Float)=0
  _HitPoint("Hit",Vector)=(0,0,0,0)
  _HitAge("Hit age",Float)=10
 }
 SubShader{
 Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
 Pass{
  Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off ZTest LEqual
  HLSLPROGRAM
  #pragma vertex vert
  #pragma fragment frag
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
  #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
  struct A{float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;float4 color:COLOR;};
  struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;};
  CBUFFER_START(UnityPerMaterial)
  float4 _BaseColor,_WardCenter,_HitPoint;float _Mode,_Radius,_Opacity,_Progress,_Dissolve,_EffectTime,_HitAge;
  CBUFFER_END
  V vert(A a){V o;float3 pos=a.positionOS.xyz;if(_Mode==3&&a.color.a>.5)pos=lerp(a.color.xyz,pos,1-_Dissolve);o.world=TransformObjectToWorld(pos);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(a.normalOS);o.uv=a.uv;return o;}
  float noise(float3 p){return frac(sin(dot(floor(p),float3(12.98,78.23,37.72)))*43758.54);}
  half4 frag(V i):SV_Target{
   float grain=noise(i.world*27),height=i.uv.y;
   clip(_Progress-height);clip(1-_Dissolve-(.72*(1-height)+.28*grain));
   float inside=smoothstep(_Radius-.15,_Radius+.35,distance(_WorldSpaceCameraPos.xz,_WardCenter.xz));
   float alpha=_Opacity*lerp(.18,1,inside);
   float3 n=normalize(i.normal);Light light=GetMainLight();float shade=.6+.4*abs(dot(n,light.direction));
   float3 col=_BaseColor.rgb*shade*(.82+.25*grain);
   if(_Mode==1)col*=.85+.15*sin(i.world.y*39+sin(i.world.x*15));
   if(_Mode==3){float glint=pow(saturate(dot(reflect(-light.direction,n),normalize(_WorldSpaceCameraPos-i.world))),32);col+=glint*.4;}
   if(_Mode==5){float flow=.5+.5*sin(i.uv.x*38+sin(i.uv.y*12-_EffectTime*2));alpha*=.28+.3*flow;col*=1.05+flow*.2;}
   float hit=distance(i.world,_HitPoint.xyz),life=saturate(1-_HitAge/.35);
   float ring=(1-smoothstep(.025,.09,abs(hit-_HitAge*2.1)))*life;
   if(_Mode==2){float crack=step(.93,frac(atan2(i.world.y-_HitPoint.y,i.world.x-_HitPoint.x)*3.7+hit*5));col=lerp(col,col*.2,crack*life*saturate(1-hit));}
   col+=ring*_BaseColor.rgb*.65;
   return half4(col,alpha*_BaseColor.a);
  }
  ENDHLSL
 }
 }
}
