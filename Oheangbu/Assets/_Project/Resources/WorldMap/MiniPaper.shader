Shader "Oheangbu/UI/MiniPaper"
{
 Properties
 {
  [PerRendererData] _MainTex("Terrain-derived regional tile",2D)="white"{}
  _ArtTex("Quiet geographic illustration",2D)="white"{}
  _FogTex("Discovery",2D)="black"{}
  _WorldUv("Regional tile world UV",Vector)=(0,0,1,1)
  _PaperColor("Paper",Color)=(.969,.945,.894,1)
  _HasDetail("Terrain tile available",Float)=0
  _PaintedRelief("Painted relief",Float)=0
  _StencilComp("Stencil Comparison",Float)=8
  _Stencil("Stencil ID",Float)=0
  _StencilOp("Stencil Operation",Float)=0
  _StencilWriteMask("Stencil Write Mask",Float)=255
  _StencilReadMask("Stencil Read Mask",Float)=255
  _ColorMask("Color Mask",Float)=15
  [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip",Float)=0
 }
 SubShader
 {
  Tags {"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True"}
  Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
  Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
  Blend SrcAlpha OneMinusSrcAlpha
  ColorMask [_ColorMask]
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   struct appdata {float4 vertex:POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;};
   struct v2f {float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float4 world:TEXCOORD1;};
   sampler2D _MainTex,_ArtTex,_FogTex;float4 _ClipRect,_WorldUv,_FogTex_TexelSize;fixed4 _PaperColor;float _HasDetail,_PaintedRelief;
   v2f vert(appdata v){v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color;o.uv=v.uv;return o;}
   float Known(float2 uv)
   {
    float2 cell=frac(uv*_FogTex_TexelSize.zw);
    float k=1-tex2D(_FogTex,uv).a;
    // Only feather toward the interior of known cells: never disclose an unknown contour.
    k*=lerp(1,smoothstep(0,.3,cell.x),tex2D(_FogTex,uv-float2(_FogTex_TexelSize.x,0)).a);
    k*=lerp(1,smoothstep(0,.3,1-cell.x),tex2D(_FogTex,uv+float2(_FogTex_TexelSize.x,0)).a);
    k*=lerp(1,smoothstep(0,.3,cell.y),tex2D(_FogTex,uv-float2(0,_FogTex_TexelSize.y)).a);
    k*=lerp(1,smoothstep(0,.3,1-cell.y),tex2D(_FogTex,uv+float2(0,_FogTex_TexelSize.y)).a);
    return k;
   }
   fixed4 frag(v2f i):SV_Target
   {
    float2 worldUv=_WorldUv.xy+i.uv*_WorldUv.zw;
    fixed4 art=tex2D(_ArtTex,worldUv),detail=tex2D(_MainTex,i.uv);
    float lum=dot(art.rgb,fixed3(.299,.587,.114));
    fixed3 quiet=_PaperColor.rgb*(1-saturate(1-lum)*.045);
    fixed3 result=lerp(quiet,detail.rgb,Known(worldUv)*saturate(_HasDetail));
    result=lerp(result,lerp(art.rgb,detail.rgb,Known(worldUv)*saturate(_HasDetail)),saturate(_PaintedRelief));
    float inside=step(0,worldUv.x)*step(worldUv.x,1)*step(0,worldUv.y)*step(worldUv.y,1);
    fixed4 c=fixed4(result*i.color.rgb,lerp(art.a,detail.a,saturate(_HasDetail))*inside*i.color.a);
    #ifdef UNITY_UI_CLIP_RECT
    c.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
    #endif
    #ifdef UNITY_UI_ALPHACLIP
    clip(c.a-.001);
    #endif
    return c;
   }
   ENDCG
  }
 }
}
