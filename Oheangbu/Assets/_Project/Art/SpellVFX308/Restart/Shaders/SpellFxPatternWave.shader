// D308-153b route P, rebuilt from the concept uks_1 (2026-10-29, Fable): the u (water) ring as a Korean-pattern WAVE SCREEN. One unlit URP shader, two modes (_Mode):
//   0 WALL   a band round the caster. uv0 = (u = metres along the ring from the aim, y = metres up the sheet from the GROUND, arc length when the sheet curls). The whole
//            wave is a 2-D silhouette cut in that (u, y) space, evaluated per pixel:
//              band       y < H(u) = base height (front / side / camera side, _Base) + rounded swells (one per crest, _Crest[k] = u centre, amp, back half-width, s*rho);
//              hook       at every crest a tube of radius rho curls sideways (direction s) from the apex over the hollow (120 deg), tapering: a big seigaiha scale laid on its side;
//              tip        a roundel at the hook's end with a spiral line (the cloud-scroll curl of the concept).
//            Inside the band the seigaiha (청해파) lattice is anchored to the FOOT (row k centre y = k R/2 - 0.3 R, odd rows shifted R), so columns of different heights never shear
//            it; a pixel belongs to the front-most (lowest) scale that covers it. _Erode (and a local erosion per crest, _CrestX[k].x over +-_CrestX[k].y metres) removes whole
//            scales by hash; the hooks and tips dissolve by noise. The cream rim is the union's signed distance < _RimW. Colours are sRGB in the material.
//   2 INK    a black brush ribbon: _MainTex alpha x vertex colour alpha, transparent (the part sets blend / zwrite / queue 3000).
// Rules kept (r1 - r3 findings of the old route): no `out`-param calls inside ?:; Queue Transparent-1, the forward pass only, no DepthOnly / DepthNormals passes (the ink-wash
// and fog post must not see the sheet as scene geometry). Look only.
Shader "Oheangbu/SpellFx/PatternWave"
{
    Properties
    {
        [HideInInspector] _Mode ("Mode 0 wall / 2 ink", Float) = 0
        _MainTex ("Ink stroke (rgba)", 2D) = "white" {}
        _ScaleR ("Seigaiha scale radius (m)", Float) = 0.3
        _Cells ("Scales round the ring", Float) = 0
        _Shift ("Lattice drift along u (m)", Float) = 0
        _Erode ("Erode 0..1", Range(0, 1)) = 0
        _RimW ("Silhouette rim width (m)", Float) = 0.045
        _RimBoost ("Cream rim boost", Float) = 1.15
        _Base ("Base height front / side / back, rise x sink", Vector) = (1.45, 1.2, 0.45, 1)
        _Ring ("Ring radius, length", Vector) = (2.2, 13.82, 0, 0)
        _CrestN ("Crest count", Float) = 0
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
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        #define MAXCREST 24
        #define HOOK_PHI 2.356        // the hook sweeps 135 deg from the apex over the hollow (r2: 120 read as a small ear)

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _Mode, _ScaleR, _Cells, _Shift, _Erode, _RimW, _RimBoost, _CrestN, _SrcBlend, _DstBlend, _ZWrite;
            float4 _Base, _Ring;
            half4 _Indigo, _IndigoDeep, _Cream, _LineCol, _InkCol;
        CBUFFER_END
        // per-crest arrays (set every tick by the part with SetVectorArray of exactly MAXCREST entries; unused entries have amp 0 and rho 0)
        float4 _Crest[MAXCREST];      // u centre (m), amp (m), back half-width (m), s * rho (signed hook radius, m)
        float4 _CrestX[MAXCREST];     // local erosion 0..1, its half-width (m), seed, unused

        struct Attributes { float4 pos : POSITION; float3 nrm : NORMAL; float4 col : COLOR; float2 uv : TEXCOORD0; };
        struct Varyings { float4 pos : SV_POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.pos = TransformObjectToHClip(i.pos.xyz);      // the part builds every mesh in WORLD space under a root at the origin
            o.col = i.col; o.uv = i.uv;
            return o;
        }

        float Hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float VNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
            return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
        }

        // the base band height above the ground at u (front 0, the camera side at +-L/2)
        float BaseH(float u)
        {
            float a = abs(u) / (_Ring.y * 0.5);
            float back = smoothstep(0.55, 1.0, a);
            float h = lerp(lerp(_Base.x, _Base.y, smoothstep(0.15, 0.55, a)), _Base.z, back);
            return h * _Base.w;
        }

        // ---- seigaiha lattice anchored to the foot: the front-most (lowest) surviving scale covering (u, y). False = no scale (clipped). d = distance / radius 0..1 ----
        bool ScaleAt(float u, float y, float erode, out float d, out float2 cell)
        {
            float R = _ScaleR;
            d = 1; cell = 0;
            float kmin = ceil((y - 0.7 * R) * 2.0 / R);
            [unroll] for (int s = 0; s <= 4; s++)
            {
                float k = kmin + s;
                if (k < 0.0) continue;
                float ox = step(0.5, frac(k * 0.5)) * R;
                float ci = floor((u - ox) / (2.0 * R) + 0.5);
                float cx = ci * 2.0 * R + ox;
                float cy = k * 0.5 * R - 0.3 * R;
                float cim = _Cells > 0.5 ? ci - _Cells * floor(ci / _Cells) : ci;
                float h = 0.75 * Hash21(float2(cim, k)) + 0.25 * saturate(1.0 - k / 16.0);     // the top rows go a little earlier
                float p = saturate((erode * 1.25 - h) / 0.25);
                float sf = 1.0 - p;
                if (sf < 0.05) continue;
                float dd = length(float2(u - cx, y - cy)) / (R * sf);
                if (dd < 1.0) { d = dd; cell = float2(cim, k); return true; }
            }
            return false;
        }

        // ---- mode 0: the wave screen. false = clip ----
        bool WallColor(Varyings i, out half3 col)
        {
            col = 0;
            float u = i.uv.x, y = i.uv.y;
            float px = max(length(float2(fwidth(u), fwidth(y))), 1e-4);         // pixel size in metres (before any branch)
            float L = _Ring.y;
            float H = BaseH(u);
            float er = _Erode;
            float dHook = -1e3, dTip = -1e3, hookLine = 0, hookTau = 0.1, hookSeed = 0, tipR = 0.1, tipSeed = 0;
            float2 tipP = 0;
            int n = (int)_CrestN;
            [loop] for (int k = 0; k < MAXCREST; k++)
            {
                if (k >= n) break;
                float4 c = _Crest[k]; float4 x = _CrestX[k];
                float du = u - c.x; du -= L * round(du / L);
                float s = c.w >= 0.0 ? 1.0 : -1.0;
                float rho = abs(c.w);
                float wB = max(c.z, 0.05), wF = max(0.95 * rho, 0.5 * wB);        // the hollow side is steep but never a spike (r2)
                float w = (du * s > 0.0) ? wF : wB;
                float q = du / w; float b = saturate(1.0 - q * q);
                H += c.y * b * b;
                if (x.x > 0.0) { float m = saturate(1.0 - abs(du) / max(x.y, 0.05)); er = max(er, x.x * smoothstep(0.0, 0.5, m)); }
                if (rho > 0.02)
                {
                    float Hi = BaseH(c.x) + c.y;
                    float2 p = float2(du * s, y - (Hi - rho));
                    float r = length(p);
                    float phi = PI * 0.5 - atan2(p.y, p.x);                      // 0 at the apex, growing toward the hollow side
                    if (phi > PI) phi -= 2.0 * PI;
                    float f = phi / HOOK_PHI;
                    if (f > -0.3 && f <= 1.0)
                    {
                        float tau = rho * lerp(0.56, 0.2, saturate(f));          // tube thickness, the outer edge is the circle of radius rho
                        float d = min(rho - r, r - (rho - tau));
                        if (d > dHook) { dHook = d; hookLine = (rho - r) / tau; hookTau = tau; hookSeed = x.z; }
                    }
                    float at = PI * 0.5 - HOOK_PHI;
                    float2 tc = float2(cos(at), sin(at)) * rho;
                    float rt = rho * 0.36;                                          // r3: 0.42 made the roundels the loudest thing
                    float dt = rt - length(p - tc);
                    if (dt > dTip) { dTip = dt; tipR = rt; tipP = p - tc; tipSeed = x.z; }
                }
            }
            float dBand = H - y;
            float d = max(dBand, max(dHook, dTip));
            if (d <= 0.0) return false;
            float rim = 1.0 - smoothstep(_RimW - px, _RimW + px, d);                // the silhouette's cream edge
            half3 creamHot = _Cream.rgb * _RimBoost;
            float R = _ScaleR;
            float aa = px / R;

            if (dHook > 0.0 || dTip > 0.0)
            {
                bool hook = dHook >= dTip;
                float2 nz = hook ? float2(u, y) * 9.0 + hookSeed * 31.0 : float2(u, y) * 9.0 + tipSeed * 31.0;
                float noise = VNoise(nz) * 0.65 + VNoise(nz * 2.7 + 5.0) * 0.35;
                float thr = er * 1.15;
                if (noise < thr) return false;
                float eat = 1.0 - smoothstep(thr, thr + 0.07, noise);
                float mottle = VNoise(float2(u, y) * 13.0);
                float lineM, shade;
                if (hook)
                {
                    float ring = hookLine * 3.0;                                    // two lines across the tube, concentric with it
                    float rf = abs(ring - round(ring));
                    float aaH = px * 3.0 / max(hookTau, 1e-3);                     // one pixel in tube-line units
                    float lw = max(0.07, 0.9 * aaH);
                    lineM = (ring > 0.5 && ring < 2.5) ? 1.0 - smoothstep(lw, lw + aaH, rf) : 0.0;
                    shade = 1.0 - hookLine;
                }
                else
                {
                    float rr = length(tipP) / tipR;
                    float a = atan2(tipP.y, tipP.x);
                    float sp = frac(rr * 1.7 - a / (2.0 * PI));
                    float aaT = px * 1.7 / tipR;                                   // one pixel in spiral units
                    float lw = clamp(0.03 * 1.7 / tipR, 0.12, 0.4);
                    lineM = (1.0 - smoothstep(lw, lw + aaT, min(sp, 1.0 - sp))) * smoothstep(0.08, 0.2, rr);
                    shade = rr;
                }
                half3 fill = lerp(_IndigoDeep.rgb, _Indigo.rgb, smoothstep(0.0, 1.0, shade)) * (0.88 + 0.24 * mottle);
                col = lerp(fill, _LineCol.rgb, lineM * 0.92);
                col = lerp(col, creamHot, max(rim, eat * 0.85));
                return true;
            }

            // the band: the lattice
            float dS; float2 cell;
            if (!ScaleAt(u + _Shift, y, er, dS, cell)) return false;
            float ring = dS * 3.0;                                                  // two lines per scale
            float rf = abs(ring - round(ring));
            float aaR = aa * 3.0;
            float lw = max(0.035, 0.35 * aaR);
            float lineM = (ring > 0.55 && ring < 2.45) ? 1.0 - smoothstep(lw, lw + aaR, rf) : 0.0;
            lineM *= saturate(1.6 - aaR * 2.5);                                     // far / small scales drop their inner lines
            float ow = max(0.045, 1.2 * aa) * (1.0 + 1.5 * er);                   // the cream outline of a scale
            float outM = smoothstep(1.0 - ow - aa, 1.0 - ow, dS);
            float hs = Hash21(cell + 17.0);
            float mottle2 = VNoise(float2(u, y) * 13.0) * 0.6 + VNoise(float2(u, y) * 41.0) * 0.4;
            half3 fill2 = lerp(_IndigoDeep.rgb, _Indigo.rgb, smoothstep(0.1, 1.0, dS));
            fill2 *= 0.86 + 0.28 * mottle2;
            fill2 *= 0.94 + 0.12 * hs;
            col = lerp(fill2, _LineCol.rgb, lineM * 0.92);
            col = lerp(col, _Cream.rgb, outM);
            col = lerp(col, creamHot, rim);
            return true;
        }
        ENDHLSL

        Pass
        {
            Name "PatternWave"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings i) : SV_Target
            {
                if (_Mode > 1.5)
                {
                    half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                    return half4(_InkCol.rgb, t.a * i.col.a);
                }
                half3 col;
                bool alive = WallColor(i, col);
                if (!alive) { clip(-1); return 0; }
                return half4(col, 1);
            }
            ENDHLSL
        }
        // no DepthOnly / DepthNormals passes on purpose (see the header)
    }
    FallBack Off
}
