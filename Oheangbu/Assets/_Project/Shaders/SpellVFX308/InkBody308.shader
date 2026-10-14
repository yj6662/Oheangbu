// #308 소환수 먹 몸 (SPEC-SPELL-VFX-OVERHAUL-308 11.13, D308-83b): 검은 먹으로 된 몸.
//  - 윤곽이 번진다: 눈에 비스듬한 면(실루엣 가장자리)을 결 잡음으로 파먹는다(오려내기, 깊이를 쓴다).
//  - 한 부위만 밝다(_AccentFrom 위 — 사슴의 뿔): 뼈빛, 빛나지 않는다.
//  - 가슴의 심(_CorePos, 물체 공간): 평소엔 안료색. _CoreGlow(0..1)와 태어나는 순간의 두 칸만 들어 올리고
//    선형 .45에서 자른다(D308-10c · ART-INK 중등: 순간 발광만, 지속 발광 없음). HDR · 가산 없음.
//  - 나타남 / 사라짐은 기존 소환수 계약 그대로(_Age · _Ground · _Height): 발치의 먹에서 위로 차오르고, 끝에는 결을 따라 흩어진다.
Shader "Oheangbu/SpellVFX308/InkBody"
{
    Properties
    {
        _Ink("Ink", Color) = (0.03, 0.03, 0.035, 1)
        _Wash("Wash (thin ink at the edge)", Color) = (0.16, 0.16, 0.17, 1)
        _WashAmount("Wash Amount", Range(0, 1)) = 0.55
        _Edge("Edge Bleed (how far the outline is eaten)", Range(0, 0.6)) = 0.2
        _GrainScale("Grain Scale", Range(1, 40)) = 9
        _Wobble("Surface Wobble (m)", Range(0, 0.05)) = 0.012
        _Flow("Liquid: swell that runs down the body (m)", Range(0, 0.2)) = 0.06
        _Sag("Liquid: undersides hang and bulge (m)", Range(0, 0.3)) = 0.09
        _Gloss("Liquid: wet sheen (a reflection, not a light)", Range(0, 1)) = 0.6
        _Accent("Accent (bone)", Color) = (0.80, 0.78, 0.72, 1)
        _AccentFrom("Accent From (share of the full height)", Range(0, 1.2)) = 0.8
        _AccentBand("Accent Band", Range(0.005, 0.3)) = 0.05
        _Core("Core Pigment", Color) = (0.14, 0.25, 0.38, 1)
        _CorePos("Core Position (object space)", Vector) = (0, 0, 0, 0)
        _CoreRadius("Core Radius (m)", Range(0.02, 1)) = 0.28
        _CoreGlow("Core Glow (momentary, 0..1)", Range(0, 1)) = 0
        _Age("Effect clock", Float) = 2
        _Ground("Ground", Float) = 0
        _Height("Full height", Float) = 2.6
        _Life("Life (0 = stays)", Float) = 4.6
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Ink, _Wash, _Accent, _Core;
            float4 _CorePos;
            half _WashAmount, _Edge, _GrainScale, _Wobble, _AccentFrom, _AccentBand, _CoreRadius, _CoreGlow, _Flow, _Sag, _Gloss;
            float _Age, _Ground, _Height, _Life;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float3 object : TEXCOORD2; float fog : TEXCOORD3; };

        float Hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
        float Noise3(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x), lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x), lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }
        // the ink's grain: it drifts up and back slowly, so the outline crawls
        float Grain(float3 world) { float3 p = world * _GrainScale + float3(0.0, -_Time.y * 0.9, _Time.y * 0.25); return Noise3(p) * 0.65 + Noise3(p * 2.3 + 5.1) * 0.35; }

        Varyings Vert(Attributes input)
        {
            Varyings output;
            float3 world = TransformObjectToWorld(input.positionOS.xyz);
            float3 normal = TransformObjectToWorldNormal(input.normalOS);
            world += normal * ((Noise3(world * 5.0 + _Time.y * 0.8) - 0.5) * 2.0 * _Wobble);
            // liquid (2026-10-14, user: "a mass of liquid, not a solid"): slow swells travel DOWN the body, and whatever faces the
            // ground hangs lower and bulges - the form is held, but it is always running off itself
            float swell = Noise3(float3(world.x * 2.4, world.y * 1.5 + _Time.y * 0.55, world.z * 2.4));
            world += normal * ((swell - 0.38) * _Flow);
            float under = saturate(-normal.y);
            float bead = Noise3(float3(world.x * 6.0, _Time.y * 0.35, world.z * 6.0));
            world.y -= under * under * (0.35 + bead) * _Sag;
            output.world = world; output.normal = normal; output.object = input.positionOS.xyz;
            output.positionCS = TransformWorldToHClip(world);
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }

        // what is drawn at all: rising out of the ink at the feet, eaten at the outline, scattered along the grain at the end
        half Cut(Varyings input, out half ndv, out half grain)
        {
            float3 view = normalize(GetCameraPositionWS() - input.world);
            ndv = saturate(abs(dot(normalize(input.normal), view)));
            grain = Grain(input.world);
            float h = (input.world.y - _Ground) / max(0.1, _Height);
            float shown = saturate((_Age - 0.2) / 0.9);
            float remain = _Life > 0.0 ? 1.0 - saturate((_Age - (_Life - 0.8)) / 0.8) : 1.0;
            clip(shown * 1.25 - h - (grain - 0.5) * 0.3);
            clip(remain * 1.4 - grain - 0.3 * (1.0 - remain));
            clip(_Age - 0.201);
            clip(ndv - _Edge * (0.3 + grain * 1.1));
            return 1;
        }
        ENDHLSL

        Pass
        {
            Name "InkBody"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            half4 Frag(Varyings input) : SV_Target
            {
                half ndv, grain;
                Cut(input, ndv, grain);
                // matte ink: nearly black, thinner (paler wash) where the surface turns away and a little where the sky is on it
                half thin = pow(1.0 - ndv, 2.0) * _WashAmount + saturate(normalize(input.normal).y) * 0.12;
                half3 colour = lerp(_Ink.rgb, _Wash.rgb, saturate(thin * (0.6 + 0.8 * grain)));
                // the one pale part
                float h = (input.world.y - _Ground) / max(0.1, _Height);
                half accent = smoothstep(_AccentFrom, _AccentFrom + _AccentBand, h + (grain - 0.5) * 0.04);
                colour = lerp(colour, _Accent.rgb * (0.72 + 0.28 * ndv), accent);
                // wet sheen: the sky seen in the ink. The normal is rippled by the same running swell, so the sheen crawls down
                // the body. A reflection of pale paper sky, LDR, well under the momentary-glow ceiling - not an emission.
                float3 wp = input.world; float e = 0.06;
                float s0 = Noise3(float3(wp.x * 5.0, wp.y * 3.0 + _Time.y * 0.9, wp.z * 5.0));
                float3 ripple = float3(Noise3(float3((wp.x + e) * 5.0, wp.y * 3.0 + _Time.y * 0.9, wp.z * 5.0)) - s0, Noise3(float3(wp.x * 5.0, (wp.y + e) * 3.0 + _Time.y * 0.9, wp.z * 5.0)) - s0, Noise3(float3(wp.x * 5.0, wp.y * 3.0 + _Time.y * 0.9, (wp.z + e) * 5.0)) - s0);
                float3 wet = normalize(normalize(input.normal) - ripple * 0.8);
                float3 eye = normalize(GetCameraPositionWS() - wp);
                float3 sky = normalize(float3(0.25, 0.9, 0.2));
                half glint = pow(saturate(dot(wet, normalize(sky + eye))), 12.0);
                half film = pow(1.0 - saturate(abs(dot(wet, eye))), 3.0);
                colour += half3(0.3, 0.3, 0.29) * _Gloss * saturate(glint * 0.8 + film * 0.55);
                // the core: pigment, not light. Lifted only for a moment (_CoreGlow, and two cels as the body completes), then capped
                half core = 1.0 - smoothstep(0.0, _CoreRadius, distance(input.world, TransformObjectToWorld(_CorePos.xyz)));     // metres, whatever the model's own unit
                core *= core;
                half birth = step(1.1, _Age) * saturate(1.0 - (_Age - 1.1) / 0.17);
                half lift = saturate(max(_CoreGlow, birth));
                half3 pigment = _Core.rgb * (1.0 + 1.6 * lift);
                pigment *= min(1.0, 0.45 / max(max(pigment.r, pigment.g), max(pigment.b, 0.001)));     // D308-10c: never above linear .45
                colour = lerp(colour, pigment, saturate(core * (0.85 + 0.5 * lift)) * (1.0 - accent));
                colour = MixFog(colour, input.fog);
                return half4(colour, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { half ndv, grain; Cut(input, ndv, grain); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target { half ndv, grain; Cut(input, ndv, grain); return half4(normalize(input.normal) * 0.5 + 0.5, 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
