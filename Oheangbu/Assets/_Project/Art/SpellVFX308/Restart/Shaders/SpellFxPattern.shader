// D308-153b route P (SPEC-SPELL-FX-RESTART-308 section 7): the u (water) ring drawn as Korean traditional pattern instead of water. One unlit URP shader, three modes (_Mode):
//   0 WALL   procedural seigaiha (청해파) scales on the ring band. uv0 = (u metres along the ring, v metres of ARC LENGTH up the (curling) band from its foot), uv1.x = the band's total arc length H,
//            uv1.y = local erosion 0..1 (r5: max'ed with _Erode; 0 from an older part).
//            With w = v - H (<= 0, metres below the wall top) the scales are laid out from the TOP down: row k has its circle centres at w = -R - k*R/2 and is shifted by R on odd rows
//            (circle radius R = _ScaleR, a scale every 2R along u). A pixel belongs to the FRONT-most circle that covers it (larger k = lower row = in front). A pixel no circle of
//            rows k >= 0 covers is clipped: that makes the band's top edge the scalloped arcs of the top row (never a straight cut). The cream rim is "covered here but not 4 cm higher".
//            _Erode removes whole scales by hash(cell, row) (they shrink, then go), the ones behind show through, so the wall comes apart scale by scale.
//   1 SPRITE the channel-packed pattern card (R = white line art, G = filled inside, B = filled grown outward = cream rim). Alpha = B, dissolved by a noise at vertex colour r (erode).
//   2 INK    a black brush ribbon: _MainTex alpha x vertex colour alpha, transparent (the part sets _SrcBlend / _DstBlend / _ZWrite / queue 3000 for this mode).
// Alpha-clip (cutout) with ZWrite for modes 0 and 1; DepthOnly / DepthNormals carry the same clip so the fog and the ink-wash post read the real silhouette. No lighting, no fog of its own.
// Colours are sRGB values in the material (Unity converts Color properties to linear). Look only.
Shader "Oheangbu/SpellFx/Pattern"
{
    Properties
    {
        [HideInInspector] _Mode ("Mode 0 wall / 1 sprite / 2 ink", Float) = 0
        _MainTex ("Sprite masks (R line, G fill, B rim) or ink stroke (rgba)", 2D) = "white" {}
        _ScaleR ("Seigaiha scale radius (m)", Float) = 0.22
        _Cells ("Scales round the ring (u wraps at _Cells * 2R)", Float) = 0
        _Erode ("Erode 0..1 (wall)", Range(0, 1)) = 0
        _RimBoost ("Cream rim HDR boost", Float) = 1.2
        _RimW ("Top rim width (m)", Float) = 0.04
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Indigo ("Indigo (scale rim side)", Color) = (0.14, 0.28, 0.44, 1)
        _IndigoDeep ("Indigo deep (scale centre)", Color) = (0.07, 0.14, 0.26, 1)
        _Cream ("Cream", Color) = (0.93, 0.90, 0.80, 1)
        _LineCol ("Line white", Color) = (0.95, 0.96, 0.95, 1)
        _InkCol ("Ink", Color) = (0.02, 0.02, 0.025, 1)
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }      // r2: after the opaques and the ink-wash / fog post (r1 AlphaTest + depth passes: the main look repainted the wall as a dark ink silhouette)

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _Mode, _ScaleR, _Cells, _Erode, _RimBoost, _RimW, _Cutoff, _SrcBlend, _DstBlend, _ZWrite;
            half4 _Indigo, _IndigoDeep, _Cream, _LineCol, _InkCol;
        CBUFFER_END

        struct Attributes { float4 pos : POSITION; float3 nrm : NORMAL; float4 col : COLOR; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
        struct Varyings { float4 pos : SV_POSITION; float3 nrmWS : TEXCOORD0; float4 col : COLOR; float2 uv : TEXCOORD1; float2 uv1 : TEXCOORD2; };

        // the part builds every mesh in WORLD space under a root at the origin, so the object-to-world matrix is identity
        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.pos = TransformObjectToHClip(i.pos.xyz);
            o.nrmWS = TransformObjectToWorldNormal(i.nrm);
            o.col = i.col; o.uv = i.uv; o.uv1 = i.uv1;
            return o;
        }

        float Hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float VNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
            return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
        }

        // ---- seigaiha lattice ----
        // Finds the front-most surviving scale covering (u, w) (w = metres below the wall top, <= 0). Row k = 0 is the top row. Returns false when no scale covers it (the clipped gap).
        // d = distance from the scale centre / its (shrinking) radius, 0..1; cell = (cell index mod _Cells, row) for the hash.
        bool ScaleAt(float u, float w, float erode, out float d, out float2 cell)
        {
            float R = _ScaleR;
            d = 1; cell = 0;
            float q = -2.0 * w / R;                  // a circle of row k covers w when k lies in (q - 4, q)
            float k0 = floor(max(q, 0.0));
            [unroll] for (int s = 0; s >= -4; s--)
            {
                float k = k0 + s;                    // largest k first = the lowest row = in front
                if (k < 0.0) continue;
                float ox = step(0.5, frac(k * 0.5)) * R;                       // odd rows are shifted half a cell (R)
                float ci = floor((u - ox) / (2.0 * R) + 0.5);
                float cx = ci * 2.0 * R + ox;
                float cy = -R - k * 0.5 * R;
                float cim = _Cells > 0.5 ? ci - _Cells * floor(ci / _Cells) : ci;
                // erosion: whole scales by their hash (the top rows a little earlier), shrinking to nothing; erode 0 touches nothing
                float h = 0.7 * Hash21(float2(cim, k)) + 0.3 * saturate(k / 18.0);
                float p = saturate((erode * 1.25 - h) / 0.25);
                float sf = 1.0 - p;
                if (sf < 0.05) continue;
                float dd = length(float2(u - cx, w - cy)) / (R * sf);
                if (dd < 1.0) { d = dd; cell = float2(cim, k); return true; }
            }
            return false;
        }

        // ---- mode 0: the wall. alive = false -> clip ----
        bool WallColor(Varyings i, out half3 col)
        {
            col = 0;
            float u = i.uv.x, w = i.uv.y - i.uv1.x;
            float px = max(length(float2(fwidth(u), fwidth(w))), 1e-4);       // pixel size in metres on the wall (computed before any branch)
            float er = max(_Erode, i.uv1.y);                                    // r5: uv1.y = a local erosion from the part (a hit wave comes apart on its own); 0 elsewhere
            float d; float2 cell;
            if (!ScaleAt(u, w, er, d, cell)) return false;
            float dUp; float2 cellUp;
            bool coveredUp = ScaleAt(u, w + _RimW, er, dUp, cellUp);          // 4 cm higher: if nothing is there, this pixel is on the silhouette rim

            float R = _ScaleR;
            float aa = px / R;                                                  // one pixel in d units
            float ring = d * 3.0;                                               // r4: two lines per scale (r3: three - the wall read white)                                               // rings at d = 0.25 / 0.5 / 0.75
            float rf = abs(ring - round(ring));
            float aaR = aa * 3.0;                                               // one pixel in ring units
            float lw = max(0.035, 0.35 * aaR);      // r3: thinner at game size                                   // thin line, never thinner than ~1.5 px
            float lineM = (ring > 0.55 && ring < 2.45) ? 1.0 - smoothstep(lw, lw + aaR, rf) : 0.0;
            lineM *= saturate(1.6 - aaR * 2.5);                                 // r4: far / small scales drop their inner lines (indigo + cream outline only)
            float ow = max(0.045, 1.2 * aa) * (1.0 + 1.5 * er);           // the cream outline of a scale (thicker while the wall comes apart)
            float outM = smoothstep(1.0 - ow - aa, 1.0 - ow, d);

            float hs = Hash21(cell + 17.0);
            float mottle = VNoise(float2(u, w) * 13.0) * 0.6 + VNoise(float2(u, w) * 41.0) * 0.4;
            half3 fill = lerp(_IndigoDeep.rgb, _Indigo.rgb, smoothstep(0.1, 1.0, d));       // darker toward the scale's centre (bottom of the visible arc)
            fill *= 0.86 + 0.28 * mottle;
            fill *= 0.94 + 0.12 * hs;
            col = lerp(fill, _LineCol.rgb, lineM * 0.92);
            col = lerp(col, _Cream.rgb, outM);
            float rim = coveredUp ? 0.0 : 1.0;
            col = lerp(col, _Cream.rgb * _RimBoost, rim);
            return true;
        }

        // ---- mode 1: the pattern card ----
        bool SpriteColor(Varyings i, out half3 col)
        {
            col = 0;
            half4 m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
            float erode = i.col.r, seed = i.col.g;
            if (m.b < _Cutoff) return false;
            float nz = VNoise(i.uv * 11.0 + seed * 31.0) * 0.65 + VNoise(i.uv * 29.0 + seed * 7.0) * 0.35;
            float thr = erode * 1.15;
            if (nz < thr) return false;
            float edge = 1.0 - smoothstep(thr, thr + 0.07, nz);                // a thin cream edge where it is being eaten
            float mottle = VNoise(i.uv * 9.0 + seed * 13.0);
            half3 indigo = lerp(_IndigoDeep.rgb, _Indigo.rgb, 0.55 + 0.45 * i.uv.y) * (0.86 + 0.28 * mottle);
            half3 creamHot = _Cream.rgb * _RimBoost;
            col = lerp(creamHot, indigo, smoothstep(0.35, 0.65, m.g));
            col = lerp(col, _LineCol.rgb, smoothstep(0.30, 0.65, m.r));
            col = lerp(col, creamHot, edge * 0.85);
            return true;
        }

        // the cutout decision shared by the colour pass and the depth passes
        bool Alive(Varyings i, out half3 col)
        {
            col = 0;
            if (_Mode > 1.5) return false;                                     // ink never writes depth
            // r3: if/else, never ?: - HLSL evaluates both arms of ?:, so SpriteColor ran after WallColor and zeroed its out col (r1 - r2: a black wall)
            if (_Mode < 0.5) return WallColor(i, col);
            return SpriteColor(i, col);
        }
        ENDHLSL

        Pass
        {
            Name "Pattern"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings i) : SV_Target
            {
                half3 col;
                if (_Mode > 1.5)
                {
                    half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                    return half4(_InkCol.rgb, t.a * i.col.a);
                }
                if (!Alive(i, col)) { clip(-1); return 0; }
                return half4(col, 1);
            }
            ENDHLSL
        }
        // r2: no DepthOnly / DepthNormals passes - the post must not see the pattern as scene geometry (the forward pass still writes depth for its own sorting)
    }
    FallBack Off
}
