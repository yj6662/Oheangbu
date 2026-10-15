using UnityEngine;

namespace Oheangbu.Data.Spell
{
    // #308 (SPEC-SPELL-VFX-OVERHAUL-308 11.25, user request 2026-10-15 "풀림 먼저"): when a swung cast is held at release, the
    // drawn letter comes apart into ink that is pulled into the swinging brush tip. Presentation only - recognition, judgement
    // and the cast clock never read it. No profile in Resources = the commit flash as before.
    [CreateAssetMenu(menuName = "Oheangbu/Spell/Ink Unravel 308 Profile", fileName = "InkUnravel308Profile")]
    public sealed class InkUnravel308Profile : ScriptableObject
    {
        public const string ResourcesPath = "Unravel308/InkUnravel308Profile";

        [Tooltip("월드 공간 파티클 시스템 하나(방출 0 — 코드가 Emit). 먹 방울 판, 늘인 빌보드.")]
        public GameObject ParticlePrefab;
        [Tooltip("획이 다 풀리기까지(초): 입자가 이 동안 차례로 떨어져 나오고 획은 디졸브로 사라진다.")]
        [Range(.05f, .6f)] public float Seconds = .2f;
        [Tooltip("입자 하나가 붓끝에 닿기까지(초) 범위.")]
        public Vector2 FlySeconds = new Vector2(.16f, .26f);
        [Tooltip("글자 하나의 입자 수 상한.")]
        [Range(8, 400)] public int MaxParticles = 140;
        [Tooltip("입자 크기 범위(m).")]
        public Vector2 Size = new Vector2(.05f, .12f);
        [Tooltip("처음 속성색 기운의 세기(0 = 먹색만). 두 칸 안에 먹으로 바뀐다(파티클 색 곡선).")]
        [Range(0f, 1f)] public float Tint = .55f;
        [Tooltip("붓끝 유도의 빠르기(클수록 곧장 붓끝으로).")]
        [Range(1f, 60f)] public float Homing = 22f;

        public bool IsValid => ParticlePrefab != null && FlySeconds.x > 0f && FlySeconds.y >= FlySeconds.x;
    }
}
