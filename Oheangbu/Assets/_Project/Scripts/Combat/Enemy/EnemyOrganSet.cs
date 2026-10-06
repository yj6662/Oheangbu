using System;
using UnityEngine;

namespace Oheangbu.Combat
{
    public enum EnemyOrganRole { Eye, Chest, Weapon, Mouth, Spine, Core }

    // #308 기관 표면 모드(SPEC-TELEGRAPH-ORGAN-308, D308-4) — 예고가 서는 표면:
    //   Part   = 따로 떨어진 모델 부위 전체(TargetRenderer = 부위 MeshRenderer, 영역 = 기관 구 안 부위 전체)
    //   Mask   = 몸 표면 가운데 마스크 텍스처(Mask, 몸 UV0) × 기관 구
    //   Sphere = 몸 표면 기관 구 안 전체(마스크 1) — 사용자가 고르지 않은 방식: 실패 대체로만 쓰고 보고한다(Temporary Exceptions)
    public enum EnemyOrganSurfaceMode { Part, Mask, Sphere }

    // #308 D308-4d 마석 오염 — 결정이 박힌 자리에서 몸 표면(ContamRenderer)으로 번진 광물 결. 평소에도 보이고(무광, 무채 = 검보라·묵색),
    // 예고 창 안에서만 속성색 LDR이 결을 따라 결정 쪽에서 바깥으로 번진다. 표현 전용 데이터(판정·AI는 모른다):
    //   None    = 오염 없음
    //   Texture = 몸 UV0 R8 선형 마스크(ContamMask): 측지 거리로 다듬은 결(contam308.py) — 가까운 다른 팔다리로 번지지 않는다
    //   Sphere  = UV0가 겹치거나 없는 몸의 대체: 결정 중심에서 월드 거리(ContamRadius)로 셰이더가 결을 만든다(보고 대상)
    public enum EnemyContaminationMode { None, Texture, Sphere }

    // #306 속성 기관 앵커(SPEC-PLAYTEST-306 #12) — 표현 전용 데이터. 판정·피해·AI는 이 컴포넌트를 모른다.
    // 앵커 = 뼈(Humanoid는 GetBoneTransform, Generic은 뼈 이름) + 로컬 오프셋. AttackKeys = 이 기관이 빛나는 공격 키
    // ("*" = 모든 속성 공격). Spine 역할 기관들은 배열 순서대로 빛이 흘러가는 사슬(뿌리 분출 Body_06→Body_18)이다.
    // Core 역할 기관들은 같은 공격 키에서 함께 켜진다(버력 장사 두 어깨, #308).
    // 앵커가 비었으면 첫 조회 때 HumanBone → BoneName 순으로 한 번 찾는다(없으면 이 오브젝트 + LocalOffset).
    // #308 표면 데이터(저작 단계 OrganSurface308·TelegraphOrgans306·MineBoss306이 미리 계산): 기관 구 = 앵커·LocalOffset 중심, Radius(m) 반지름.
    // TargetRenderer가 비면 그 기관은 빛나지 않는다(덧칠할 표면이 없다 — 새 오브젝트를 띄우지 않는다).
    [DisallowMultipleComponent]
    public sealed class EnemyOrganSet : MonoBehaviour
    {
        public const string KeyAny = "*", KeyProjectile = "projectile", KeyGround = "ground", KeyMelee = "melee";
        public const string KeyBolt = "bolt", KeyRoot = "root", KeyWave = "wave";

        [Serializable]
        public sealed class Organ
        {
            public string Id;
            public Transform Anchor;
            public Vector3 LocalOffset;
            [Tooltip("기관 구 반지름(m) — 덧칠 영역. 부위(Part) 기관은 부위 경계 구 반지름 이상")]
            [Min(.01f)] public float Radius = .12f;
            public EnemyOrganRole Role;
            public string[] AttackKeys = { KeyAny };
            [Tooltip("앵커 해석 — Humanoid 뼈(LastBone = 쓰지 않음)")] public HumanBodyBones HumanBone = HumanBodyBones.LastBone;
            [Tooltip("앵커 해석 — Generic 뼈 이름(Legacy298_ 접두 뼈는 건너뛴다)")] public string BoneName;

            [Header("#308 표면")]
            [Tooltip("덧칠이 붙는 렌더러(서브메시 1개만) — 부위 MeshRenderer 또는 몸 SkinnedMeshRenderer")]
            public Renderer TargetRenderer;
            public EnemyOrganSurfaceMode Mode;
            [Tooltip("Mask 모드 — 기관 영역 마스크(TargetRenderer UV0, R8 선형). 한 렌더러의 Mask 기관은 같은 텍스처를 쓴다")]
            public Texture2D Mask;
            [Tooltip("방어 성공 먹 획이 닿는 표면점(앵커 기준)")]
            public Vector3 SurfaceLocalPoint;
            [Tooltip("표면 바깥 법선(앵커 기준) — 보이는 기관 고르기. 0 = 저작 안 됨(몸 중심 → 기관 방향으로 대신)")]
            public Vector3 SurfaceLocalNormal;

            [Header("#308 마석 오염(D308-4d)")]
            [Tooltip("오염이 번지는 몸 렌더러(서브메시 1개만) — 비면 오염 없음")]
            public Renderer ContamRenderer;
            public EnemyContaminationMode ContamMode;
            [Tooltip("Texture 모드 — 몸 UV0 R8 선형 마스크(값 = 결 × 결정 가까움)")]
            public Texture2D ContamMask;
            [Tooltip("오염 반지름(m) — Texture는 굽기에 쓴 측지 반지름(기록), Sphere는 셰이더의 월드 거리 반지름")]
            [Min(0f)] public float ContamRadius;
            [Tooltip("오염 중심 = 결정 축이 몸 표면과 만나는 자리(앵커 기준)")]
            public Vector3 ContamLocalPoint;

            public bool HasSurface => SurfaceLocalNormal.sqrMagnitude > 1e-8f;
            public bool HasContamination => ContamMode != EnemyContaminationMode.None && ContamRenderer != null && ContamRadius > 0f
                && (ContamMode != EnemyContaminationMode.Texture || ContamMask != null);
        }

        public Organ[] Organs = Array.Empty<Organ>();
        [NonSerialized] private bool _resolved;

        public int Count => Organs != null ? Organs.Length : 0;

        public Organ Get(int index) { Resolve(); return Organs[index]; }

        public Transform AnchorOf(int index)
        {
            Resolve();
            var organ = Organs[index];
            return organ.Anchor != null ? organ.Anchor : transform;
        }

        public Vector3 WorldPoint(int index)
        {
            Resolve();
            var organ = Organs[index];
            var anchor = organ.Anchor != null ? organ.Anchor : transform;
            return anchor.TransformPoint(organ.LocalOffset);
        }

        // #308 표면점(월드) — 저작 안 된 기관은 구 중심
        public Vector3 SurfacePoint(int index)
        {
            Resolve();
            var organ = Organs[index];
            var anchor = organ.Anchor != null ? organ.Anchor : transform;
            return anchor.TransformPoint(organ.HasSurface ? organ.SurfaceLocalPoint : organ.LocalOffset);
        }

        // #308 바깥 법선(월드, 단위) — 저작 안 된 기관은 이 오브젝트(몸 루트) 위 1 m에서 기관 쪽 방향, 그것도 0이면 몸 앞
        public Vector3 SurfaceNormal(int index)
        {
            Resolve();
            var organ = Organs[index];
            var anchor = organ.Anchor != null ? organ.Anchor : transform;
            Vector3 n = organ.HasSurface ? anchor.TransformDirection(organ.SurfaceLocalNormal)
                : anchor.TransformPoint(organ.LocalOffset) - (transform.position + Vector3.up);
            return n.sqrMagnitude > 1e-8f ? n.normalized : transform.forward;
        }

        // #308 D308-4d 오염 중심(월드) — 결정이 몸에 박힌 자리
        public Vector3 ContamPoint(int index)
        {
            Resolve();
            var organ = Organs[index];
            var anchor = organ.Anchor != null ? organ.Anchor : transform;
            return anchor.TransformPoint(organ.ContamLocalPoint);
        }

        public bool Matches(int index, string key)
        {
            var keys = Organs[index].AttackKeys;
            if (keys == null || string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < keys.Length; i++) if (keys[i] == KeyAny || keys[i] == key) return true;
            return false;
        }

        // 빛날 기관 목록(인덱스) — results는 호출자 버퍼(할당 없음). 반환 = 채운 개수
        public int Collect(string key, int[] results)
        {
            Resolve();
            int n = 0;
            for (int i = 0; i < Count && n < results.Length; i++) if (Organs[i] != null && Matches(i, key)) results[n++] = i;
            return n;
        }

        // 편집기·런타임 공용 — 비어 있는 앵커만 채운다(저작 값 우선)
        public void Resolve(bool force = false)
        {
            if (_resolved && !force) return;
            _resolved = true;
            if (Organs == null) return;
            Animator animator = null; Transform[] bones = null;
            foreach (var organ in Organs)
            {
                if (organ == null || (organ.Anchor != null && !force)) continue;
                if (organ.HumanBone != HumanBodyBones.LastBone)
                {
                    if (animator == null) animator = GetComponentInChildren<Animator>(true);
                    if (animator != null && animator.avatar != null && animator.isHuman) organ.Anchor = animator.GetBoneTransform(organ.HumanBone);
                }
                if (organ.Anchor == null && !string.IsNullOrEmpty(organ.BoneName))
                {
                    if (bones == null) bones = GetComponentsInChildren<Transform>(true);
                    foreach (var bone in bones) if (bone.name == organ.BoneName) { organ.Anchor = bone; break; }
                }
            }
        }
    }
}
