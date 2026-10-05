// PURE308
using System.Collections.Generic;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 WP-09, handler buff.companion (SPEC-SPELL-120-308 section 13): while the buff lasts, every accepted cast is
    // accompanied by small extra shots at the aimed enemy. The shot's element is the buff glyph's own element, whatever the
    // accompanied spell is. Buff timing is the shared rule of every #308 buff (SpellBuffState308, Q8 default): casting the
    // same glyph again replaces its remaining time, different glyphs run side by side.
    // A companion shot is not a direct spell hit: its damage source is Companion (no five-element credit, no groggy).
    public static class CompanionShotRule308
    {
        // One running companion buff: what each accepted cast is accompanied by.
        public readonly struct Companion
        {
            public readonly char Letter;
            public readonly Element Element;
            public readonly float Power, Speed, Interval;
            public readonly int Shots;
            public Companion(char letter, Element element, float power, int shots, float speed, float interval)
            { Letter = letter; Element = element; Power = power; Shots = shots; Speed = speed; Interval = interval; }
        }

        // One extra shot: who, which life, when it lands, how much, which element. Index = its number inside one cast.
        public readonly struct Shot
        {
            public readonly int TargetId, Index;
            public readonly uint Life;
            public readonly float At, Power;
            public readonly Element Element;
            public readonly char Letter;
            public Shot(int targetId, uint life, float at, float power, Element element, char letter, int index)
            { TargetId = targetId; Life = life; At = at; Power = power; Element = element; Letter = letter; Index = index; }
        }

        // The companion buffs of one handler instance. The timers are the host's shared buff timers.
        public sealed class State
        {
            readonly List<Companion> _active = new List<Companion>();

            public int Count => _active.Count;
            public Companion this[int index] => _active[index];

            // A cast of the buff. The same glyph: its entry is replaced and its time starts over (never added up).
            // Another glyph: a second entry beside the first.
            public bool Activate(SpellBuffState308 timers, in Companion companion, float now, float duration)
            {
                if (timers == null || !(duration > 0f) || float.IsNaN(now) || companion.Shots <= 0 || !(companion.Power > 0f) || !(companion.Speed > 0f)) return false;
                timers.Activate(companion.Letter, now, duration);
                int at = IndexOf(companion.Letter);
                if (at >= 0) _active[at] = companion; else _active.Add(companion);
                return true;
            }

            public bool Active(SpellBuffState308 timers, char letter, float now)
            {
                return timers != null && IndexOf(letter) >= 0 && timers.Active(letter, now);
            }

            // The shots that go with one accepted cast, whatever that cast is. No aimed enemy, or a dead one: no shot.
            // Every running buff adds its own shots; shot i lands at now + i x interval + distance / speed.
            public void Accompany(SpellBuffState308 timers, SpellActorSnap[] actors, int aimedId, Vector3 from, float now, List<Shot> shots)
            {
                if (timers == null || actors == null || shots == null || aimedId < 0 || aimedId >= actors.Length || !actors[aimedId].Alive) return;
                var target = actors[aimedId];
                float distance = Vector3.Distance(from, target.Position);
                for (int c = 0; c < _active.Count; c++)
                {
                    var companion = _active[c];
                    if (!timers.Active(companion.Letter, now)) continue;
                    float flight = distance / companion.Speed;
                    float interval = companion.Interval > 0f ? companion.Interval : 0f;
                    for (int i = 0; i < companion.Shots; i++)
                        shots.Add(new Shot(target.Id, target.Life, now + i * interval + flight, companion.Power, companion.Element, companion.Letter, i));
                }
            }

            // Drops the buffs whose time is over (or whose timer the host cleared). ended = their glyphs (may be null).
            public int Expire(SpellBuffState308 timers, float now, List<char> ended)
            {
                int count = 0;
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    if (timers != null && timers.Active(_active[i].Letter, now)) continue;
                    if (ended != null) ended.Add(_active[i].Letter);
                    _active.RemoveAt(i);
                    count++;
                }
                return count;
            }

            public void Clear() => _active.Clear();

            int IndexOf(char letter)
            {
                for (int i = 0; i < _active.Count; i++) if (_active[i].Letter == letter) return i;
                return -1;
            }
        }
    }
}
