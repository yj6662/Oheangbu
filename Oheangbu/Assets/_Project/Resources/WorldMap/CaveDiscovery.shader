Shader "Oheangbu/UI/WalkedCave"
{
    Properties
    {
        [PerRendererData] _MainTex("Cave plan",2D)="white"{}
        _DiscoveryTex("Walked passages",2D)="black"{}
        _StencilComp("Stencil Comparison",Float)=8
        _Stencil("Stencil ID",Float)=0
        _StencilOp("Stencil Operation",Float)=0
        _StencilWriteMask("Stencil Write Mask",Float)=255
        _StencilReadMask("Stencil Read Mask",Float)=255
        _ColorMask("Color Mask",Float)=15
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 world:TEXCOORD1; fixed4 color:COLOR; };
            sampler2D _MainTex,_DiscoveryTex;
            float4 _ClipRect;
            float4 _DiscoveryTex_TexelSize;

            float WalkedEdge(float2 uv)
            {
                float2 cell=frac(uv*_DiscoveryTex_TexelSize.zw);
                float k=tex2D(_DiscoveryTex,uv).r;
                k*=lerp(1,smoothstep(0,.8,cell.x),1-tex2D(_DiscoveryTex,uv-float2(_DiscoveryTex_TexelSize.x,0)).r);
                k*=lerp(1,smoothstep(0,.8,1-cell.x),1-tex2D(_DiscoveryTex,uv+float2(_DiscoveryTex_TexelSize.x,0)).r);
                k*=lerp(1,smoothstep(0,.8,cell.y),1-tex2D(_DiscoveryTex,uv-float2(0,_DiscoveryTex_TexelSize.y)).r);
                k*=lerp(1,smoothstep(0,.8,1-cell.y),1-tex2D(_DiscoveryTex,uv+float2(0,_DiscoveryTex_TexelSize.y)).r);
                return k;
            }
            v2f vert(appdata v) { v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float inside=step(0,i.uv.x)*step(i.uv.x,1)*step(0,i.uv.y)*step(i.uv.y,1);
                fixed4 cave=tex2D(_MainTex,i.uv);
                float known=WalkedEdge(i.uv)*inside;
                fixed3 blank=fixed3(.35,.34,.30);
                #ifndef UNITY_COLORSPACE_GAMMA
                blank=GammaToLinearSpace(blank);
                #endif
                fixed4 result=fixed4(lerp(blank,cave.rgb,known),1)*i.color;
                #ifdef UNITY_UI_CLIP_RECT
                result.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
