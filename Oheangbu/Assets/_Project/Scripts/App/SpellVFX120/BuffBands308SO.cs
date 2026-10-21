using System;
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public enum BandRegion308 { None, Attack, Body, Ink }

    // #308 D308-127 (debate 11 A, debate 14 - TEST): the buff bands on the great brush. Presentation data only: which glyph's buff
    // shows in which of the three places, where those places are on the shaft, and what the bands are made of. Rules never
    // read it (the buffs' own timers are read by AuraBrushBands308, never written).
    //   regions  털 끝 = spells / attack (Attack), 자루 가운데 = body (Body), 손 위 = ink (Ink) - one band each, the newest buff
    //            of a region covers the older one, three at most
    //   places   metres along GripSocket +Y of each band unit's lower end: a 2 cm paper underline, then the 8 cm pigment band
    //            (Sonnet: band >= 8 cm, the gap between two bands >= a band). Measured on SM_GreatBrush308 (debate 14): the right
    //            hand holds .30 behind GripSocket (GreatBrush308Profile.RightHandBehindGripSocket), the left hand GripSocketL at
    //            -.42, the brass neck from +.50, the butt -.60 - so 손 위 is just above the right hand, 자루 가운데 the middle of the
    //            free shaft, 털 끝 just under the neck. Form = while a weapon head is on: the head grows out of the ferrule and
    //            never covers the shaft, and below the hands there are only 12 cm, so the places stay (debate 14 deviation from
    //            "손 아래 구간", which assumed the head would cover the shaft).
    //   검       is a healing tree zone (zone.tree), not a buff the player carries: no band.
    [CreateAssetMenu(menuName = "Oheangbu/Spell/Buff Bands 308", fileName = "BuffBands308")]
    public sealed class BuffBands308SO : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Letter;
            public BandRegion308 Region;
            public Element Element;
        }

        [Serializable]
        public struct Place
        {
            public float Ink, Body, Attack;
            public float Of(BandRegion308 region) => region == BandRegion308.Ink ? Ink : region == BandRegion308.Body ? Body : Attack;
        }

        public Entry[] Entries = Array.Empty<Entry>();
        [Header("places (m along GripSocket +Y, the unit's lower end)")]
        public Place Plain = new Place { Ink = -.22f, Body = .10f, Attack = .38f };
        public Place Form = new Place { Ink = -.22f, Body = .10f, Attack = .38f };
        [Header("size (m)")]
        [Min(.08f)] public float BandLength = .08f;
        [Min(.02f)] public float UnderlineLength = .02f;
        // still round 1: the bamboo nodes stood through a .0195 band with a .0012 groove (dark dots on the band)
        public float ShaftRadius = .017f, BandRadius = .0215f, UnderlineRadius = .0205f, Groove = .0008f;
        [Range(2, 10)] public int Turns = 5;
        [Header("the brush (build_v5.py): what the bands must keep clear of")]
        public float HandHalf = .06f, UpperHand = -.30f, LowerHand = -.42f, Ferrule = .50f, Butt = -.60f;
        [Header("time (s): the band winds on from its lower end, and unwinds when the buff ends")]
        [Min(.05f)] public float EnterSeconds = .6f;
        [Min(.05f)] public float ExitSeconds = .3f;
        [Header("materials (the brush's matte shader, no emission)")]
        public Material[] Pigments = new Material[5];   // by Element
        public Material Paper;

        public bool TryGet(char letter, out Entry entry)
        {
            foreach (var e in Entries)
                if (!string.IsNullOrEmpty(e.Letter) && e.Letter[0] == letter && e.Region != BandRegion308.None) { entry = e; return true; }
            entry = default;
            return false;
        }

        public Material PigmentOf(Element element) => Pigments != null && (int)element < Pigments.Length ? Pigments[(int)element] : null;

        /// <summary>The newest running buff of a region: the latest cast (since), a tie broken by the cast order. -1 = none.</summary>
        public static int Newest(BandRegion308 region, BandRegion308[] regions, bool[] on, float[] since, int[] order, int count)
        {
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                if (!on[i] || regions[i] != region) continue;
                if (best < 0 || since[i] > since[best] || (since[i] == since[best] && order[i] > order[best])) best = i;
            }
            return best;
        }

        /// <summary>The layout gate (Sonnet's sizes, kept clear of the hands, the ferrule and the butt). Empty = it holds.</summary>
        public List<string> LayoutProblems()
        {
            var problems = new List<string>();
            if (BandLength < .08f - 1e-4f) problems.Add("band " + BandLength + " m < .08");
            if (UnderlineLength < .02f - 1e-4f) problems.Add("underline " + UnderlineLength + " m < .02");
            var regions = new[] { BandRegion308.Ink, BandRegion308.Body, BandRegion308.Attack };
            foreach (var (label, place) in new[] { ("plain", Plain), ("form", Form) })
            {
                var spans = new List<(float From, float To, BandRegion308 Region)>();
                foreach (var region in regions) { float from = place.Of(region); spans.Add((from, from + UnderlineLength + BandLength, region)); }
                spans.Sort((a, b) => a.From.CompareTo(b.From));
                for (int i = 0; i < spans.Count; i++)
                {
                    var s = spans[i];
                    // the hands' own lengths of shaft, and the ends of the shaft the band may use
                    if (Overlaps(s.From, s.To, UpperHand - HandHalf, UpperHand + HandHalf)) problems.Add(label + " " + s.Region + " under the upper hand");
                    if (Overlaps(s.From, s.To, LowerHand - HandHalf, LowerHand + HandHalf)) problems.Add(label + " " + s.Region + " under the lower hand");
                    if (s.To > Ferrule + 1e-4f) problems.Add(label + " " + s.Region + " past the ferrule");
                    if (s.From < Butt - 1e-4f) problems.Add(label + " " + s.Region + " past the butt");
                    if (s.Region == BandRegion308.Ink && s.From < UpperHand) problems.Add(label + " ink band not above the hand");
                    if (i == 0) continue;
                    var p = spans[i - 1];
                    // pigment to pigment >= a band; and some bare shaft between a band and the next unit's paper
                    float pigmentGap = s.From + UnderlineLength - p.To;
                    if (pigmentGap < BandLength - 1e-4f) problems.Add(label + " " + p.Region + " -> " + s.Region + " pigment gap " + pigmentGap.ToString("0.000") + " < " + BandLength);
                    if (s.From - p.To < .04f - 1e-4f) problems.Add(label + " " + p.Region + " -> " + s.Region + " bare gap " + (s.From - p.To).ToString("0.000") + " < .04");
                }
            }
            return problems;
        }

        static bool Overlaps(float a0, float a1, float b0, float b1) => a0 < b1 - 1e-4f && b0 < a1 - 1e-4f;
    }
}
