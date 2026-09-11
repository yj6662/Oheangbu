Shader "Oheangbu/VFX/RectShield"
{
 Properties { _BaseColor("Color",Color)=(2,2,2,.45) }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
  Pass
  {
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   ZTest LEqual
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
   struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
   Varyings vert(Attributes i){Varyings o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color;return o;}
   half4 frag(Varyings i):SV_Target
   {
    float2 q=abs(i.uv-.5)*2;
    float edge=max(q.x,q.y);
    float border=smoothstep(.962,.975,edge)*(1-smoothstep(.991,1,edge));
    float innerLine=smoothstep(.89,.899,edge)*(1-smoothstep(.906,.913,edge));
    float panel=(1-smoothstep(.99,1,edge))*.34;
    half4 c=_BaseColor*i.color;c.rgb*=lerp(.7,1.5,border);c.a*=max(panel,max(border,innerLine*.5));return c;
   }
   ENDHLSL
  }
 }
}
