using System;
using System.Collections.Generic;
namespace Oheangbu.Vol2.Rules
{
    /// <summary>TemplateId = Frame x Verb (design section 7): P0..P5 (path), A0..A5 (attachment), F0..F5 (fan), R0..R5 (ring), the digit
    /// being the verb in table order (once, leave, spread, seed, split, return). 24 values; the body (5) and the peak (20) are profiles
    /// on top of it, not part of the id.</summary>
    public readonly struct Vol2TemplateId : IEquatable<Vol2TemplateId>
    {
        public const int Count = 24;
        const string FrameLetters = "PAFR";
        public Frame Frame { get; }
        public Verb Verb { get; }
        public Vol2TemplateId(Frame frame, Verb verb)
        {
            if ((uint)frame > 3 || (uint)verb > 5) throw new ArgumentOutOfRangeException();
            Frame = frame; Verb = verb;
        }
        public static Vol2TemplateId Of(Glyph glyph) => new Vol2TemplateId(glyph.Frame, glyph.Verb);
        public int Index => (int)Frame * 6 + (int)Verb;
        public string Name => FrameLetters[(int)Frame].ToString() + (int)Verb;
        public static bool TryParse(string name, out Vol2TemplateId id)
        {
            id = default;
            if (name == null || name.Length != 2) return false;
            int f = FrameLetters.IndexOf(name[0]), v = name[1] - '0';
            if (f < 0 || v < 0 || v > 5) return false;
            id = new Vol2TemplateId((Frame)f, (Verb)v); return true;
        }
        public static IReadOnlyList<Vol2TemplateId> All()
        {
            var all = new List<Vol2TemplateId>(Count);
            for (int f = 0; f < 4; f++) for (int v = 0; v < 6; v++) all.Add(new Vol2TemplateId((Frame)f, (Verb)v));
            return all;
        }
        public bool Equals(Vol2TemplateId other) => Frame == other.Frame && Verb == other.Verb;
        public override bool Equals(object obj) => obj is Vol2TemplateId other && Equals(other);
        public override int GetHashCode() => Index;
        public override string ToString() => Name;
        public static bool operator ==(Vol2TemplateId a, Vol2TemplateId b) => a.Equals(b);
        public static bool operator !=(Vol2TemplateId a, Vol2TemplateId b) => !a.Equals(b);
    }
}
