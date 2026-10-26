// D308-150 V10-V11 free showcase (나 / 고 / 각): ONE premultiplied particle / ribbon shader for fire, glow, ink and smoke.
// Blend One OneMinusSrcAlpha: rgb = what the particle adds (may be HDR), alpha = how much of the background it covers.
//   _AddMix 1 = pure additive light (fire core, glow); 0 = covering ink / smoke that may also carry a burning edge (_EdgeColor).
// Erosion: the vertex colour's alpha is the life left: the mask (texture alpha x noise) is eroded from the outside in instead of a flat fade.
// Ramp: _UseRamp 1 colours the particle by its mask ("heat": edge 0 .. core 1) from _RampMap (fire / jade ramps).
// Streams: POSITION, COLOR, TEXCOORD0.xy = uv, TEXCOORD0.z = stable random (particles; 0 for meshes / trails).
Shader "Oheangbu/FreeShowcase/Particle"
{
    Properties
    {
        _BaseMap ("Shape (alpha)", 2D) = "white" {}
        _NoiseMap ("Noise", 2D) = "gray" {}
        _RampMap ("Ramp", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity (HDR)", Float) = 1
        _AddMix ("Additive mix", Range(0,1)) = 1
        _UseRamp ("Use ramp", Range(0,1)) = 0
        _RampScale ("Ramp scale", Float) = 1.5
        _NoiseAmt ("Noise amount", Range(0,1)) = 0.5
        _NoiseScale ("Noise scale", Float) = 1
        _NoiseScroll ("Noise scroll (xy)", Vector) = (0, -0.4, 0, 0)
        _Erode ("Erode softness", Range(0.01,1)) = 0.2
        _EdgeColor ("Burning edge colour (HDR)", Color) = (0,0,0,0)
        _EdgeWidth ("Edge width", Range(0.01,1)) = 0.15
        _SoftFade ("Soft particle distance", Float) = 0.4
        _Opacity ("Opacity", Range(0,4)) = 1
        // 2026-10-27 common material pass (showcase120_plan.md step 2). Every new property defaults to "no effect" (the old look, bit for bit).
        _FlipTex ("Fire / smoke flipbook (8x8: R heat, G density, A mask)", 2D) = "black" {}
        _FsKind ("Kind (0 plain, 1 fire, 2 smoke / dust puff)", Float) = 0
        _Flip ("Flipbook on (set per particle renderer by a property block)", Float) = 0
        _SmokeColor ("Fire flipbook: smoke colour", Color) = (0.045, 0.036, 0.032, 1)
        _WaterMode ("Clear water (0 off, 1 on)", Float) = 0
        _WaterDeep ("Clear water: body colour", Color) = (0.03, 0.26, 0.36, 1)
        // 2026-10-27 per-glyph polish: how far up the fire ramp the flipbook's heat walks (1 = the material pass; < 1 = redder, less yellow-white)
        _FlipHeat ("Fire flipbook: heat reach", Float) = 1
        // 2026-10-27 per-glyph polish: clear water's white fringe (1 = the material pass; < 1 = the white only on a thin rim)
        _WaterRim ("Clear water: rim power", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "FsForward"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
            TEXTURE2D(_RampMap); SAMPLER(sampler_RampMap);
            TEXTURE2D(_FlipTex); SAMPLER(sampler_FlipTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _Tint;
                float _Intensity, _AddMix, _UseRamp, _RampScale, _NoiseAmt, _NoiseScale, _Erode, _EdgeWidth, _SoftFade, _Opacity;
                float4 _NoiseScroll;
                float4 _EdgeColor;
                float _FsKind, _Flip, _WaterMode, _FlipHeat, _WaterRim;
                float4 _SmokeColor, _WaterDeep;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float3 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float3 uv : TEXCOORD0; float4 screen : TEXCOORD1; float eyeDepth : TEXCOORD2; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.color = v.color;
                o.uv = float3(TRANSFORM_TEX(v.uv.xy, _BaseMap), v.uv.z);
                o.screen = ComputeScreenPos(p.positionCS);
                o.eyeDepth = -p.positionVS.z;
                return o;
            }

            // 2026-10-27: a fire / smoke flipbook cell (the particle's texture-sheet animation already moved uv into the cell)
            half4 fragFlip (Varyings i)
            {
                float4 f = SAMPLE_TEXTURE2D(_FlipTex, sampler_FlipTex, i.uv.xy);
                float2 suv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float soft = saturate((scene - i.eyeDepth) / max(_SoftFade, 1e-3));
                float t = 1.0 - i.color.a;                       // life used up: a gentle erosion on top of the flipbook's own dissipation
                float n = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, i.uv.xy * 3.0 * _NoiseScale + _NoiseScroll.xy * _Time.y + i.uv.z * 7.31).r;
                float keep = saturate((f.a * (0.8 + 0.4 * n) - t * 0.8) / 0.3);
                if (keep <= 0.001) discard;
                if (_FsKind > 1.5)
                {
                    // smoke / dust / steam puff: the density channel is the billowing body, coloured like the plain puff
                    float d = f.g * keep;
                    float cover = d * soft * _Tint.a * _Opacity;
                    float3 col = i.color.rgb * _Tint.rgb * _Intensity;
                    return half4(col * cover, saturate(cover * (1.0 - _AddMix)));
                }
                // fire: heat -> the fire ramp (emission, HDR); the density around it is dark smoke that covers (ink world: fire with soot)
                float heat = f.r * keep;
                // r2 (material pass r1 read as white blobs): the heat walks only the lower ~3/4 of the fire ramp (red -> orange -> yellow; white only in
                // the hottest pockets), a little less light, and a lighter, softer soot
                float3 ramp = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, float2(saturate(heat * 0.62 * _FlipHeat + 0.14), 0.5)).rgb;      // r3: r2 read pale yellow - redder, more orange
                float3 fire = ramp * i.color.rgb * _Tint.rgb * _Intensity * 0.9 * saturate(heat * 1.7) * soft * _Tint.a * _Opacity;
                float smoke = saturate(f.g * keep - heat * 1.3) * soft * (1.0 - _AddMix) * _Opacity * 0.7;
                return half4(fire + _SmokeColor.rgb * smoke, saturate(smoke * _SmokeColor.a + heat * soft * (1.0 - _AddMix) * 0.35));
            }

            half4 frag (Varyings i) : SV_Target
            {
                if (_Flip > 0.5) return fragFlip(i);
                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv.xy);
                float2 nuv = i.uv.xy * _NoiseScale + _NoiseScroll.xy * _Time.y + i.uv.z * 7.31;
                float n = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, nuv).r;
                float mask = saturate(tex.a * (1.0 - _NoiseAmt + _NoiseAmt * n * 2.0));
                float t = 1.0 - i.color.a;                       // life used up = how far the erosion has eaten in
                float over = mask - t;
                float a = saturate(over / _Erode);
                if (a <= 0.001) discard;

                float3 col = tex.rgb;
                if (_UseRamp > 0.5) col = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, float2(saturate(over * _RampScale), 0.5)).rgb;
                col *= i.color.rgb * _Tint.rgb * _Intensity;

                // soft particles: fade where the particle meets the scene's surfaces
                float2 suv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float soft = saturate((scene - i.eyeDepth) / max(_SoftFade, 1e-3));

                float cover = a * soft * _Tint.a * _Opacity;
                float3 rgb = col * cover;
                // a burning edge just inside the erosion front (ink that burns at its rim)
                float edge = (1.0 - saturate(over / _EdgeWidth)) * a * soft;
                if (_WaterMode > 0.5)
                {
                    // 2026-10-27 clear water (the project has no opaque texture, so no grab refraction): see-through teal in the facing middle, a more
                    // opaque meniscus at the mask's rims and at grazing angles (the water spheres write fresnel into the vertex colour), sharp moving
                    // glints from the ridged noise, a thin dim foam edge. (Batches 1 - 4 read icy / milky: wide white edges + high cover.)
                    float fr = saturate((max(i.color.r, max(i.color.g, i.color.b)) - 0.6) / 1.0);
                    float thick = saturate(over * 2.2);
                    float rim = 1.0 - thick;
                    float n2 = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, i.uv.xy * _NoiseScale * 2.3 + _NoiseScroll.xy * _Time.y * 1.7 + i.uv.z * 3.1).b;
                    // r2 (material pass r1: white flecks everywhere read icy): rarer, smaller glints, a deeper teal body, the ramp's light only at the rims
                    float glint = pow(saturate(n2 * 1.12 - 0.12), 18.0) * 1.5;
                    // r3 (r2 read grey and murky on the red ground: too little of the ramp's glossy streaks): keep the batches' streaked light, lay a
                    // saturated teal-blue depth under it, open only the thick facing middle a little (the background tinted, not hidden)
                    float clarity = lerp(0.55, 1.0, saturate(rim * 0.9 + fr * 0.9));
                    float bodyA = cover * (1.0 - _AddMix) * clarity;
                    float3 tintW = _WaterDeep.rgb * (0.8 + 0.9 * thick);
                    // 2026-10-27 per-glyph polish (아 오 우 언 운 read icy - a wide white fringe round every eroded sheet): the ramp's white only on a thin rim,
                    // the sheet's body walks into the teal sooner (_WaterRim 1 = the material pass)
                    if (_UseRamp > 0.5 && _WaterRim < 0.999)
                        col = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, float2(pow(saturate(over * _RampScale), _WaterRim), 0.5)).rgb * i.color.rgb * _Tint.rgb * _Intensity;
                    float3 w = col * cover * 0.85 + tintW * bodyA + glint * thick * cover * float3(0.9, 0.97, 1.0) * max(1.0, _Intensity * 0.5);
                    w += _EdgeColor.rgb * edge * 0.3;
                    return half4(w, saturate(bodyA));
                }
                rgb += _EdgeColor.rgb * edge;
                return half4(rgb, saturate(cover * (1.0 - _AddMix)));
            }
            ENDHLSL
        }
    }
}
