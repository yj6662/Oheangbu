using Oheangbu.App.SpellVFX120;
using UnityEngine;

namespace Oheangbu.App
{
    // 패링 성공 접점 도장 [SPEC-SPELL-VFX-OVERHAUL-308 11.41 §4]: 투사체가 방어막에 부딪혀 꺼지는 지점에 먹 고리 하나가
    // 찍혀 번지고, 먹에서 방어막 속성색으로 반쯤 스민 뒤 스러진다(조각이 튀지 않는다 · 빛나지 않는다).
    // 원본 접점(KTP)·저작 접점·전개 층이 없을 때만 쓰이는 대체 경로다. 그림은 InkStamp308(에셋 의존 없음).
    // 이 타입의 인스턴스는 더 이상 만들어지지 않는다(KtpContactAudit의 "조각 버스트 없음" 검사가 그대로 성립).
    public sealed class ParryBurstEffect : MonoBehaviour
    {
        private const float Diameter = 0.7f;       // 다 번졌을 때 지름(m) × scale
        private const float StartFraction = 0.6f;  // 찍히는 순간 크기(다 번진 크기 대비)
        private const float Lifetime = 0.32f;
        private const float Alpha = 0.6f;
        private const float Soak = 0.5f;           // 먹 → 속성색 절반
        private const float EyeClear = 0.45f;      // 눈에서 이보다 가까우면 그리지 않는다
        private static readonly Color Ink = new Color(0.075f, 0.065f, 0.058f);

        // scale: 아웃컴 소피드백 [SPELL-FIDELITY §4.6] — Success=1 / Half=축소(반쪽의 응답)
        public static void Spawn(Vector3 position, Color color, Camera cam, float scale = 1f)
        {
            Quaternion facing = Quaternion.identity;
            if (cam != null)
            {
                Vector3 view = position - cam.transform.position;
                if (view.sqrMagnitude < EyeClear * EyeClear) return;
                // 카메라를 바라보는 평면에 찍는다 — 어느 각도에서든 「막에 부딪혔다」로 읽히게
                facing = Quaternion.LookRotation(view);
            }
            float s = Mathf.Max(0.1f, scale);
            InkStamp308.Spawn(InkStamp308.Shape.Ring, position, facing, Diameter * StartFraction * s,
                Ink, color, Alpha, Lifetime, Soak, 1f / StartFraction);
        }
    }
}
