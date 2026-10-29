using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;

namespace Oheangbu.App
{
    /// <summary>The verbs this scope can run, keyed by Verb. THE SINGLE LIST of verb executors is <see cref="CreateDefault"/>: a later slice adds its
    /// executor with ONE new file (Executors/Vol2XExecutor.cs) and ONE line here, `reg.Add(new Vol2XExecutor());`. A second executor for a verb is
    /// a programming error and throws at start-up (never silently replaces). A fixture builds its own registry with only the verbs it needs.</summary>
    public sealed class Vol2VerbRegistry
    {
        readonly Dictionary<Verb, IVol2VerbExecutor> _byVerb = new Dictionary<Verb, IVol2VerbExecutor>();

        public int Count => _byVerb.Count;

        public Vol2VerbRegistry Add(IVol2VerbExecutor executor)
        {
            if (executor == null) throw new ArgumentNullException(nameof(executor));
            if (_byVerb.ContainsKey(executor.Verb)) throw new InvalidOperationException("verb " + executor.Verb + " already has an executor: " + _byVerb[executor.Verb].Name);
            _byVerb.Add(executor.Verb, executor);
            return this;
        }

        public bool TryGet(Verb verb, out IVol2VerbExecutor executor) => _byVerb.TryGetValue(verb, out executor);
        public bool Handles(Verb verb) => _byVerb.ContainsKey(verb);

        public List<Verb> Registered()
        {
            var l = new List<Verb>(_byVerb.Keys); l.Sort(); return l;
        }

        /// <summary>The scope's verbs. ADD A NEW VERB EXECUTOR HERE (one line each). Slice 3: Once. Slice 4: Leave, Spread. Slice 5: Seed. Slice 6: Split. Slice 7: Return.</summary>
        public static Vol2VerbRegistry CreateDefault()
        {
            var reg = new Vol2VerbRegistry();
            reg.Add(new Vol2OnceExecutor());        // 한 번   (slice 3)
            reg.Add(new Vol2ResidueExecutor());     // 남긴다 (slice 4)
            reg.Add(new Vol2SpreadExecutor());      // 번진다 (slice 4)
            reg.Add(new Vol2SeedExecutor());        // 심는다 (slice 5)
            reg.Add(new Vol2SplitExecutor());       // 가른다 (slice 6)
            reg.Add(new Vol2ReturnExecutor());      // 돌아온다 (slice 7)
            return reg;
        }
    }
}
