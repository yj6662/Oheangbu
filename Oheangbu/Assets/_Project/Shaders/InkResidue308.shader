// 잔먹 장 — 술식 잔먹 · 공중 방울 · 플레이어 발자국 (SPEC-SPELL-DEPLOY-308 §10, D308-10 / D308-10b) [TEST].
// InkResidueField308이 고정 용량 링 버퍼를 Graphics.RenderMeshInstanced로 그린다(바닥 1회 + 공중 방울 1회).
// 인스턴스 값(MaterialPropertyBlock 배열):
//   _StampA = (태어난 시각 s, 수명 s, 아틀라스 칸, 농도)
//   _StampB = (마르기 시작 시각 s — 유지 자국은 술식이 끝난 시각, 좌우 반전 0/1, 공중 1 / 바닥 0, 공중 꼬리 방울 1 / 그 밖 0)
//   공중 꼬리 방울(forms3 D10): 인스턴스 행렬의 X 열 = 진행 방향 × 그릴 길이, Z 열 = 폭. 사각을 화면에서 그 방향으로 눕힌다
//   (칸의 머리가 U 1 쪽이라 머리가 앞, 꼬리가 뒤). 눈 쪽으로 날아오는 방울은 길이가 줄어 둥글게 보인다(폭보다 짧아지지 않는다).
//   _StampC = (칸 안 uv 시작 xy, 크기 zw) — 큰 자국을 .35 m 이하 조각으로 나눠 깔 때의 조각 영역(0 = 칸 전체)
// 마름: 나이 = (_OhResidueNow - max(태어난 시각, 마르기 시작)) / 수명. 아틀라스 B(마르는 순서)가 나이보다 낮은 곳부터 사라진다 —
//   가장자리부터 안으로 줄어들 뿐 커지거나 번지지 않는다. 색은 먹 → 회색 → 없음.
// 발광 0: Emission·가산 없음, 최종 최대 채널 ≤ .85. 발자국은 중성 담묵(속성 담채 없음, 맥동 없음)이라 오염(번지는 검보라)·발광으로 읽히지 않는다.
// 그리는 자리: 큐 2050(Geometry+50) 블렌드, ZWrite Off. 깊이·법선·그림자 패스 없음 — InkWash297이 땅과 함께 씻고 풀(AlphaTest)이 위를 덮는다
//   (바닥 덮개 먹선 교훈: 불투명 큐 2050 블렌드 + 깊이/법선 패스 끔).
// 블렌드 = 곱(Blend DstColor Zero): 자국은 바닥을 어둡게만 한다. 어떤 바닥에서도 바닥보다 밝아질 수 없으므로 어두운 바닥의
//   마른 회색 자국이 "빛나는 자국"으로 읽히는 일이 구조적으로 없다(Spec Q5: 어두운 바닥에서는 안 보인다). 색 값은 곱 계수다.
Shader "Oheangbu/InkResidue308"
{
    Properties
    {
        _Atlas("Deploy atlas (R distance field, G wet, B dry order; linear)", 2D) = "gray" {}
        _InkColor("Residue ink", Color) = (0.13, 0.12, 0.11, 1)
        _WetColor("Pooled ink", Color) = (0.06, 0.055, 0.05, 1)
        _DryColor("Dry grey", Color) = (0.42, 0.41, 0.39, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Geometry+50"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "InkResidue"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma editor_sync_compilation
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "InkCommon308.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uvAge      : TEXCOORD0;   // xy uv, z age 0..1, w opacity
                float2 cellAlive  : TEXCOORD1;   // x atlas cell, y alive
            };

            TEXTURE2D(_Atlas);
            SAMPLER(sampler_Atlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _Atlas_ST;
                half4 _InkColor;
                half4 _WetColor;
                half4 _DryColor;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(OhResidueProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _StampA)
                UNITY_DEFINE_INSTANCED_PROP(float4, _StampB)
                UNITY_DEFINE_INSTANCED_PROP(float4, _StampC)
            UNITY_INSTANCING_BUFFER_END(OhResidueProps)

            // global clock written by InkResidueField308 (outside the material CBUFFER so the global is not shadowed)
            float _OhResidueNow;

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float4 a = UNITY_ACCESS_INSTANCED_PROP(OhResidueProps, _StampA);
                float4 b = UNITY_ACCESS_INSTANCED_PROP(OhResidueProps, _StampB);
                float4 c = UNITY_ACCESS_INSTANCED_PROP(OhResidueProps, _StampC);
                float start = max(a.x, b.x);
                float life = max(a.y, 0.01);
                float age = saturate((_OhResidueNow - start) / life);
                float alive = step(a.x, _OhResidueNow) * step(_OhResidueNow, start + life) * step(0.001, a.y);

                float3 ws;
                if (b.z > 0.5)
                {
                    // airborne drop / contact star: a camera-facing quad around the instance origin
                    float3 centre = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                    float3 along = TransformObjectToWorldDir(float3(1.0, 0.0, 0.0), false);
                    float size = length(along);
                    float3 camRight = UNITY_MATRIX_V[0].xyz;
                    float3 camUp = UNITY_MATRIX_V[1].xyz;
                    if (b.w > 0.5)
                    {
                        // forms3 (D10) a tailed drop: the quad lies along its travel direction as the camera sees it
                        float width = length(TransformObjectToWorldDir(float3(0.0, 0.0, 1.0), false));
                        float2 onScreen = float2(dot(along, camRight), dot(along, camUp));
                        float seen = length(onScreen);
                        float2 dir = seen > 0.0001 ? onScreen / seen : float2(0.0, -1.0);
                        float3 head = camRight * dir.x + camUp * dir.y;
                        float3 side = camUp * dir.x - camRight * dir.y;
                        ws = centre + head * (v.positionOS.x * max(seen, width)) + side * (v.positionOS.z * width);
                    }
                    else
                    {
                        ws = centre + (camRight * v.positionOS.x + camUp * v.positionOS.z) * size;
                    }
                }
                else
                {
                    ws = TransformObjectToWorld(v.positionOS.xyz);
                }
                float4 cs = TransformWorldToHClip(ws);
                // expired or unused slots leave the clip volume
                if (alive < 0.5) cs = float4(2.0, 2.0, 2.0, 1.0);
                o.positionCS = cs;
                float2 uv = v.uv;
                // a big mark is laid as pieces of at most .35 m, each with its own ground ray: c = the piece's part of the cell
                uv = c.xy + uv * (c.z > 0.0 ? c.zw : float2(1.0, 1.0));
                uv.x = lerp(uv.x, 1.0 - uv.x, step(0.5, b.y));
                o.uvAge = float4(uv, age, saturate(a.w));
                o.cellAlive = float2(a.z, alive);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 rect = OhCellRect(i.cellAlive.x);
                half3 cell = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, rect.xy + saturate(i.uvAge.xy) * rect.zw).rgb;
                float age = i.uvAge.z;
                // the distance field gives a hard ink edge at any size
                float edge = saturate((cell.r - 0.5) / max(fwidth(cell.r), 0.0005) + 0.5);
                // dries from the rim inward: low dry-order goes first, nothing ever grows
                float dry = saturate((cell.b - (age * 1.06 - 0.06)) * 14.0);
                float alpha = edge * dry * i.uvAge.w * (1.0 - smoothstep(0.78, 1.0, age));
                clip(alpha - 0.004);
                half3 ink = lerp(_InkColor.rgb, _WetColor.rgb, saturate(cell.g) * (1.0 - age));
                half3 rgb = lerp(ink, _DryColor.rgb, smoothstep(0.35, 1.0, age));
                // multiply: 1 = the ground untouched, the ink value where the mark is whole. Never above 1, so never brighter.
                half3 factor = lerp(half3(1.0, 1.0, 1.0), min(rgb, OH_INK_CEILING_OUT), saturate(alpha));
                return half4(factor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
