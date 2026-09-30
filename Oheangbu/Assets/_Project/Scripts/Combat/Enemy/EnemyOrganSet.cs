using System;
using UnityEngine;

namespace Oheangbu.Combat
{
    public enum EnemyOrganRole { Eye, Chest, Weapon, Mouth, Spine, Core }

    // #306 속성 기관 앵커(SPEC-PLAYTEST-306 #12) — 표현 전용 데이터. 판정·피해·AI는 이 컴포넌트를 모른다.
    // 앵커 = 뼈(Humanoid는 GetBoneTransform, Generic은 뼈 이름) + 로컬 오프셋. AttackKeys = 이 기관이 빛나는 공격 키
    // ("*" = 모든 속성 공격). Spine 역할 기관들은 배열 순서대로 빛이 흘러가는 사슬(뿌리 분출 Body_06→Body_18)이다.
    // 앵커가 비었으면 첫 조회 때 HumanBone → BoneName 순으로 한 번 찾는다(없으면 이 오브젝트 + LocalOffset).
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
            [Min(.01f)] public float Radius = .12f;
            public EnemyOrganRole Role;
            public string[] AttackKeys = { KeyAny };
            [Tooltip("앵커 해석 — Humanoid 뼈(LastBone = 쓰지 않음)")] public HumanBodyBones HumanBone = HumanBodyBones.LastBone;
            [Tooltip("앵커 해석 — Generic 뼈 이름(Legacy298_ 접두 뼈는 건너뛴다)")] public string BoneName;
        }

        public Organ[] Organs = Array.Empty<Organ>();
        [NonSerialized] private bool _resolved;

        public int Count => Organs != null ? Organs.Length : 0;

        public Organ Get(int index) { Resolve(); return Organs[index]; }

        public Vector3 WorldPoint(int index)
        {
            Resolve();
            var organ = Organs[index];
            var anchor = organ.Anchor != null ? organ.Anchor : transform;
            return anchor.TransformPoint(organ.LocalOffset);
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
