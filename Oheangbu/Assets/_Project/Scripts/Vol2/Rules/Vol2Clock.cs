using System;
namespace Oheangbu.Vol2.Rules
{
    /// <summary>The one combat clock of the vol.2 runtime (design section 4): a signed 64-bit count of microseconds. Rules read this
    /// and nothing else; floats are for rendering progress only and never flow back into a rule.</summary>
    public interface IVol2Clock
    {
        long NowUs { get; }
    }

    public static class Vol2Time
    {
        /// <summary>Seconds (double) to microseconds, rounded to nearest. 0.3 s is 300,000 us.</summary>
        public static long FromSeconds(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            return checked((long)Math.Round(seconds * 1000000.0));
        }
        public static double ToSeconds(long us) => us / 1000000.0;
        /// <summary>For a presenter only (remaining / elapsed time); never feed this back into a rule.</summary>
        public static float ToSecondsF(long us) => (float)(us / 1000000.0);
        /// <summary>Half-open interval [startUs, endUs): the start is inside, the end is not.</summary>
        public static bool Contains(long startUs, long endUs, long nowUs) => nowUs >= startUs && nowUs < endUs;
    }

    /// <summary>A clock over a seconds source (the combat time of the game: scaled, so pause and slow motion behave as the rest of
    /// combat). Starts at 0 at construction and never goes backwards: a source that steps back (or answers a non-finite number) is held.</summary>
    public sealed class Vol2Clock : IVol2Clock
    {
        readonly Func<double> _seconds;
        readonly double _origin;
        long _last;
        public Vol2Clock(Func<double> seconds)
        {
            _seconds = seconds ?? throw new ArgumentNullException(nameof(seconds));
            double first = _seconds();
            _origin = double.IsNaN(first) || double.IsInfinity(first) ? 0 : first;
        }
        public long NowUs
        {
            get
            {
                double s = _seconds();
                if (double.IsNaN(s) || double.IsInfinity(s)) return _last;
                long us = Vol2Time.FromSeconds(s - _origin);
                if (us > _last) _last = us;
                return _last;
            }
        }
    }

    /// <summary>A clock a test advances by hand.</summary>
    public sealed class ManualVol2Clock : IVol2Clock
    {
        public long NowUs { get; private set; }
        public ManualVol2Clock(long startUs = 0) { NowUs = startUs; }
        public void Set(long us) { if (us < NowUs) throw new ArgumentOutOfRangeException(nameof(us), "the clock never goes backwards"); NowUs = us; }
        public void Advance(long us) { if (us < 0) throw new ArgumentOutOfRangeException(nameof(us)); NowUs += us; }
    }
}
