using System;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.Data.Spell
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.10 (D308-83a) [every number TEST]: the ink of a cast's swing. One entry per swing state
    // (SpellSwingProfile308.Entry.State): the stroke the brush tip leaves in the air and the prefab whose parts follow the tip
    // ("Tip") and are placed once at the apex ("Apex" at the tip, "Ground" on the ground under it).
    // Presentation only: nothing here is read by recognition, by power or by the swing rule. Ink, not light (ART-INK).
    // Runtime lookup: Resources "SwingInk308/SwingInk308Profile". Missing = no swing ink.
    [CreateAssetMenu(menuName = "Oheangbu/Spell/Swing Ink 308", fileName = "SwingInk308Profile")]
    public sealed class SwingInk308Profile : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("휘두르기 상태 이름(SpellSwingProfile308의 State와 같다).")]
            public string State;
            [Tooltip("자식 Tip / Apex / Ground를 가진 프리팹(비우면 획만).")]
            public GameObject Prefab;
            [Tooltip("붓끝 획의 재질(Oheangbu/SwingInk308/InkArc). 비우면 획 없음.")]
            public Material Stroke;
            [Tooltip("획의 굵기(m).")]
            [Range(.02f, 1.2f)] public float StrokeWidth;
            [Tooltip("획이 남아 있는 시간(초).")]
            [Range(.1f, 2f)] public float StrokeSeconds;
            [Tooltip("규칙의 정점이 0인 분류(패링 · 결계 · 강화)에서 그림의 정점(떼고 나서, 초).")]
            [Min(0)] public float VisualApexSeconds;
            [Tooltip("정점 뒤에도 붓끝을 따라가며 먹을 뿌리는 시간(초).")]
            [Min(0)] public float FollowAfterApex;
            // D308-91 (11.35) [TEST]: this motion's soak on top of the cast's Soak row. 0 = the row as it is (old assets read 0)
            [Tooltip("이 동작에서 스밈 최대 비율에 더하는 값(0 = 그대로).")]
            [Range(0, 1)] public float SoakMaxAdd;
            [Tooltip("이 동작에서 스밈이 꼬리 쪽으로 닿는 길이에 더하는 값(획 시간, 초. 0 = 그대로).")]
            [Min(0)] public float SoakReachAdd;
            [Tooltip("이 동작에서 정점 뒤 마름을 늦추는 정도(마름 배율 x (1 - 값), 0 = 그대로).")]
            [Range(0, .9f)] public float SoakLinger;
        }

        public Entry[] States = Array.Empty<Entry>();

        [Header("끄는 조건")]
        [Tooltip("눈에서 이 거리(m) 안의 붓끝은 건너뛴다.")]
        [Min(0)] public float HideWithinCamera = .45f;
        [Tooltip("D308-84 circle bridge: ground-run blot size = bridge width x this (TEST)")] [Min(0)] public float RunBlotScale = 1.6f;
        [Tooltip("D308-84 circle bridge: blots this high over the ground (no z-fight)")] [Min(0)] public float RunLift = .03f;
        [Tooltip("D308-84 circle bridge: blots laid per bridge point (sw_o: 1 read faint)")] [Range(1, 4)] public int RunBlotsPerPoint = 2;
        [Tooltip("D308-84 cone bridge: the ground ink runs this far down the cone before the fan rises (m, TEST)")] [Min(0)] public float ConeRunMeters = 2.5f;
        [Tooltip("정점 먹이 날아가는 방향: 시전자 정면 + 붓끝 진행 방향 x 이 값 (0 = 정면만)")]
        [Range(0, 1)] public float ApexLean = .35f;
        [Tooltip("붓끝이 한 프레임에 이 거리(m)보다 멀리 옮겨지면(순간 이동) 획을 끊는다.")]
        [Min(.5f)] public float ClearJump = 3f;
        [Tooltip("붓끝이 이만큼(m) 움직일 때마다 획에 점을 하나 놓는다.")]
        [Range(.005f, .2f)] public float MinVertexDistance = .03f;

        public bool IsValid
        {
            get
            {
                if (States == null || States.Length == 0 || float.IsNaN(HideWithinCamera + ClearJump + MinVertexDistance)) return false;
                foreach (var e in States)
                    if (string.IsNullOrEmpty(e.State) || float.IsNaN(e.StrokeWidth + e.StrokeSeconds + e.VisualApexSeconds + e.FollowAfterApex) || e.StrokeSeconds <= 0f) return false;
                return true;
            }
        }

        /// <summary>Index into States, or -1.</summary>
        public int IndexOf(string state)
        {
            if (string.IsNullOrEmpty(state) || States == null) return -1;
            for (int i = 0; i < States.Length; i++) if (States[i].State == state) return i;
            // D308-84: a position variant inks like the motion it varies (thrown like the sweep, landed like the slam)
            foreach (var pair in Variants) if (pair.From == state && pair.To != state) return IndexOf(pair.To);
            return -1;
        }

        [Serializable] public struct Variant { public string From, To; }
        [Tooltip("위치별 동작(D308-84)이 어느 먹을 쓰는지. 표에 없는 상태 이름이 오면 이것으로 찾는다.")]
        public Variant[] Variants = { new Variant { From = "SwingThrow", To = "SwingSweep" }, new Variant { From = "SwingStrikeFar", To = "SwingSlam" } };

        // D308-90 (SPEC-SPELL-VFX-OVERHAUL-308 11.34 20th pass) [every number TEST]: from the apex on, the swung arc takes the
        // spell's pigment - from the tip back toward the tail - and stays a little longer. No row for a cast = the arc as before.
        [Serializable]
        public struct Soak
        {
            [Tooltip("이 속성의 술식에만.")]
            public Element Element;
            [Tooltip("이 글자들에만(비우면 그 속성 전체).")]
            public string Letters;
            [Tooltip("스미는 안료(sRGB, swingink가 그 술식 프로필의 Pigment를 넣는다). 빛이 아니라 먹: 1을 넘기지 않는다.")]
            public Color Pigment;
            [Tooltip("가장 많이 스밀 때의 안료 비율.")]
            [Range(0, 1)] public float Max;
            [Tooltip("정점부터 스밈이 닿는 데까지 걸리는 시간(초).")]
            [Min(.01f)] public float Seconds;
            [Tooltip("붓끝에서 꼬리 쪽으로 닿는 길이(정점 전 획 시간, 초).")]
            [Min(0)] public float Reach;
            [Tooltip("스밈 앞머리의 부드러움(획 시간, 초).")]
            [Min(.01f)] public float Soft;
            [Tooltip("정점 뒤 획이 마르는 속도 배율(1 = 그대로, 작을수록 오래 남는다).")]
            [Range(.1f, 1f)] public float AgingAfterApex;

            public bool Usable => !float.IsNaN(Max + Seconds + Reach + Soft + AgingAfterApex) && !float.IsInfinity(Max + Seconds + Reach + Soft + AgingAfterApex)
                && Seconds > 0f && Soft > 0f && AgingAfterApex > 0f;
        }

        [Header("D308-90 호 스밈")]
        public Soak[] Soaks = Array.Empty<Soak>();

        /// <summary>Index into Soaks of the first usable row for this cast, or -1 (a bad row is skipped, not fatal).</summary>
        public int SoakFor(Element element, char letter)
        {
            if (Soaks == null) return -1;
            for (int i = 0; i < Soaks.Length; i++)
            {
                var s = Soaks[i];
                if (s.Element != element || !s.Usable) continue;
                if (string.IsNullOrEmpty(s.Letters) || s.Letters.IndexOf(letter) >= 0) return i;
            }
            return -1;
        }
    }
}
