Shader "Oheangbu/HarvestInkFlow"
{
 Properties { _MainTex("Owned dry brush mask",2D)="white"{} _Tint("Ink",Color)=(.105,.095,.08,.92) _FlowTime("Flow",Float)=0 _Bead("Bead",Float)=0 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
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
   TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
   float4 _Tint;float4 _MainTex_ST;float _FlowTime;float _Bead;
   CBUFFER_END
   struct Attributes{float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
   struct Varyings{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
   Varyings vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;o.color=v.color;return o;}
   half4 frag(Varyings i):SV_Target
   {
    float2 uv=i.uv;
    float paper=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,float2(frac(uv.x*2-_FlowTime*.21),uv.y)).a;
    float edge=1-smoothstep(.29,.5,abs(uv.y-.5));
    float dry=lerp(.60,1,paper);
    float pulse=.85+.15*sin(uv.x*29-_FlowTime*12);
    float thread=edge*dry*pulse;
    float bead=1-smoothstep(.19,.50,length((uv-.5)*float2(1,1.2)));
    float alpha=lerp(thread,bead,saturate(_Bead))*_Tint.a*i.color.a;
    return half4(_Tint.rgb*i.color.rgb,alpha);
   }
   ENDHLSL
  }
 }
}
