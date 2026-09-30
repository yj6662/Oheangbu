Shader "Oheangbu/Interaction/WorldLetter" {
Properties {[PerRendererData] _MainTex("Font",2D)="white"{} }
SubShader {Tags {"Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True"}
Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
Pass {CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
struct a {float4 vertex:POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;};
struct v {float4 pos:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;};
sampler2D _MainTex;
v vert(a i){v o;o.pos=UnityObjectToClipPos(i.vertex);o.color=i.color;o.uv=i.uv;return o;}
fixed4 frag(v i):SV_Target{fixed4 c=i.color;c.a*=tex2D(_MainTex,i.uv).a;return c;}
ENDCG}
}}
