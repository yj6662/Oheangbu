#ifndef OHEANGBU_INK_NOISE_3D_INCLUDED
#define OHEANGBU_INK_NOISE_3D_INCLUDED

// 월드 좌표 3D 값 노이즈 + 2옥타브 Fbm — 시간항 없음(먹은 마르면 고정된다).
// 레거시 MandateOfInk S_ToonLitTemp.shader:122-156 이식(예준 자작, 라이선스 무관).
// 오브젝트가 회전해도 표면에 들러붙은 붓자국처럼 고정된다(UV 왜곡 회피). 신규 환경 셰이더 3종만 include —
// 기존 술식 셰이더의 2D InkFbm(InkStroke.shader:101-121)과는 도메인이 달라 섞지 않는다.

float OhHash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float OhValueNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = OhHash3(i + float3(0, 0, 0));
    float n100 = OhHash3(i + float3(1, 0, 0));
    float n010 = OhHash3(i + float3(0, 1, 0));
    float n110 = OhHash3(i + float3(1, 1, 0));
    float n001 = OhHash3(i + float3(0, 0, 1));
    float n101 = OhHash3(i + float3(1, 0, 1));
    float n011 = OhHash3(i + float3(0, 1, 1));
    float n111 = OhHash3(i + float3(1, 1, 1));
    float nx00 = lerp(n000, n100, f.x);
    float nx10 = lerp(n010, n110, f.x);
    float nx01 = lerp(n001, n101, f.x);
    float nx11 = lerp(n011, n111, f.x);
    float nxy0 = lerp(nx00, nx10, f.y);
    float nxy1 = lerp(nx01, nx11, f.y);
    return lerp(nxy0, nxy1, f.z);
}

// 대략 0~1
float OhFbm3(float3 p)
{
    float sum = OhValueNoise3(p) * 0.65;
    sum += OhValueNoise3(p * 2.13 + 11.7) * 0.35;
    return sum;
}

#endif
