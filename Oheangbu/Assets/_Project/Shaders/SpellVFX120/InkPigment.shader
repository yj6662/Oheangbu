Shader "Oheangbu/VFX120/InkPigment"
{
    Properties
    {
        [MainTexture] _BaseMap("Pattern (alpha)", 2D) = "white" {}
        [MainColor] _BaseColor("Pigment", Color) = (.2,.3,.3,1)
        _Alpha("Opacity", Float) = 1
        _Erode("Drying", Range(0,1)) = 0
        _Age("Age", Float) = 0
        _Pattern("Pattern blend", Float) = 0
        _Soft("Soft particle disc", Float) = 0
        _Body("Solid body ink shading", Float) = 0
        _Metal("Worked metal glint", Float) = 0
        _ZWrite("Depth write", Float) = 0
        _Fluid("Material motion: water 1, flame 2, sand 3, mist 4", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1; float3 local:TEXCOORD2; float3 positionWS:TEXCOORD3; half4 color:COLOR; };
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseColor;
            float _Alpha, _Erode, _Age, _Pattern, _Soft, _Body, _Metal, _ZWrite, _Fluid;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v);
                if(_Fluid>.5 && _Fluid<1.5)
                    v.positionOS.y += sin(v.uv.x*32+_Age*5)*sin(v.uv.y*3.14159)*.018;
                if(_Fluid>1.5 && _Fluid<2.5)
                    v.positionOS.x += sin(v.uv.y*15-_Age*9+v.positionOS.z*5)*v.uv.y*.025;
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.local=v.positionOS.xyz; o.color=v.color; return o;
            }
            float hash(float3 p) { return frac(sin(dot(floor(p),float3(127.1,311.7,74.7)))*43758.5453); }
            half4 frag(Varyings i):SV_Target
            {
                float pattern=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).a;
                float tooth=hash(i.local*58);
                float grain=.87+.13*tooth;
                float lit=.64+.30*saturate(dot(normalize(i.normalWS),normalize(float3(-.4,.8,-.3))));
                float mask=lerp(1,pattern,_Pattern);
                float disc=saturate(1-length(i.uv-.5)*2);
                mask*=lerp(1,disc*disc,_Soft);
                float alpha=mask*_Alpha*smoothstep(_Erode-.12,_Erode+.12,tooth)*(.82+.18*grain);
                // Understates highlights; translucent ink accumulates by alpha, never additive white.
                float3 rgb=_BaseColor.rgb*lerp(lit,1,saturate(_Pattern+_Soft))*grain*i.color.rgb;
                if(_Fluid>.5 && _Fluid<1.5)
                {
                    // Curling seawater motif: pale broken foam over a translucent ink wash.
                    float flow=sin(i.uv.x*43+sin(i.uv.y*19-_Age*4)*1.8+_Age*2);
                    float crest=smoothstep(.30,.49,i.uv.y)*(1-smoothstep(.68,.91,i.uv.y));
                    float foam=crest*smoothstep(-.1,.68,flow)*(.65+.35*tooth);
                    float edge=smoothstep(0,.06,i.uv.x)*(1-smoothstep(.94,1,i.uv.x));
                    float base=smoothstep(0,.09,i.uv.y)*(1-smoothstep(.93,1,i.uv.y));
                    rgb=lerp(_BaseColor.rgb*.9,float3(.25,.47,.48),.38+crest*.18);
                    rgb=lerp(rgb,float3(.71,.79,.73),foam*.88)*grain;
                    alpha=_Alpha*edge*base*(.52+foam*.42)*smoothstep(_Erode-.12,_Erode+.12,tooth);
                }
                if(_Fluid>1.5 && _Fluid<2.5)
                {
                    float flare=.5+.5*sin(i.uv.y*19-_Age*10+i.uv.x*7);
                    float heat=saturate((1-i.uv.y)*.8+flare*.35);
                    rgb=lerp(_BaseColor.rgb*.7,float3(.75,.39,.13),heat*.75)*grain;
                    alpha*=.5+.45*flare;
                }
                if(_Fluid>2.5 && _Fluid<3.5)
                {
                    float swirl=.5+.5*sin(i.uv.x*31+i.uv.y*15-_Age*5);
                    float edge=saturate(sin(saturate(i.uv.x)*3.14159)*sin(saturate(i.uv.y)*3.14159)*3);
                    alpha*=edge*(.20+.42*swirl);
                    rgb=lerp(_BaseColor.rgb,float3(.34,.24,.14),swirl*.5);
                    float ridge=1-smoothstep(.05,.45,abs(i.uv.y-.48));
                    rgb=lerp(rgb,rgb*.36,ridge*.6);
                }
                if(_Fluid>3.5) alpha*=.16;
                float3 view=normalize(_WorldSpaceCameraPos-i.positionWS);
                float rim=pow(1-saturate(abs(dot(normalize(i.normalWS),view))),3);
                rgb=lerp(rgb,rgb*.22,rim*_Body*.8);
                float glint=pow(saturate(dot(reflect(-normalize(float3(-.4,.8,-.3)),normalize(i.normalWS)),view)),45);
                rgb*=lerp(1,.38+.8*saturate(dot(normalize(i.normalWS),normalize(float3(-.4,.8,-.3)))),_Metal);
                rgb+=_BaseColor.rgb*glint*_Metal*.45;
                if(_Body>.5) { clip(_Alpha-.001); clip(tooth-(1-saturate(_Alpha))); alpha=1; }
                // Mesh particles also carry lifetime alpha in COLOR. Ignoring it left
                // dying leaf/shard silhouettes fully opaque until they disappeared.
                alpha*=i.color.a;
                clip(alpha-.006);
                return half4(min(rgb,.92),alpha);
            }
            ENDHLSL
        }
    }
}
