using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.App.SpellVFX120
{
    // #308 D308-128 21 / D308-132 step 6: the look of a five-element reaction. ISpellReactionPresenter308.React puts the kind's
    // prefab (SpellReactionLook308SO: wildfire, steam; mud is recognised and has none yet) at the product's centre, turned along the way the
    // wave ran, grown from the radius of the material to the product's in .4 s where they differ, living the rule's life. The
    // bodies are a side table of their own (never Vfx120Effects, never in _live): they end by themselves, EndAll takes them off, at
    // most MaxReactionBodies stand at once (the rule keeps at most three products too; a fourth look pales the oldest). The
    // consumed material's own floor is Zone308's: SpellFxCue.React -> Vfx308ZoneBody.React. Never swallows a cue; a failure here
    // is logged and shows nothing. The wave's travel time to the material (D308-132 "Delay + along / speed") is not drawn: Delay 0.
    public sealed partial class Vfx120SpellPresenter308 : ISpellReactionPresenter308
    {
        public const int MaxReactionBodies = 3;
        const float ReactionFade = .3f;

        readonly List<Vfx308ZoneBody> _reactions = new List<Vfx308ZoneBody>();
        readonly HashSet<ReactionKind308> _reactionWarned = new HashSet<ReactionKind308>();
        SpellReactionLook308SO _reactionLook;
        bool _reactionLookLoaded;

        /// <summary>Reaction looks put down, refused (no prefab for the kind, too close to the eye, a failure) and warnings said.</summary>
        public int ReactionsShown { get; private set; }
        public int ReactionsRefused { get; private set; }
        public int ReactionWarnings { get; private set; }
        public int LiveReactions { get { PruneReactions(); return _reactions.Count; } }
        public IReadOnlyList<Vfx308ZoneBody> ReactionBodies => _reactions;

        /// <summary>Checks hand the look in (null = read Resources once).</summary>
        public void BindReactionLook(SpellReactionLook308SO look) { _reactionLook = look; _reactionLookLoaded = look != null; }

        SpellReactionLook308SO ReactionLook()
        {
            if (_reactionLookLoaded) return _reactionLook;
            _reactionLookLoaded = true;
            _reactionLook = Resources.Load<SpellReactionLook308SO>(SpellReactionLook308SO.ResourcesPath);
            return _reactionLook;
        }

        public void React(in SpellFxCueArgs args)
        {
            if (args.Kind == ReactionKind308.None) return;
            var look = ReactionLook();
            if (look == null || !look.Knows(args.Kind)) { WarnReaction(args.Kind, look == null ? "no SpellReactionLook308 in Resources (run Vfx308Build reaction)" : "no entry for the kind"); ReactionsRefused++; return; }
            if (!look.TryGet(args.Kind, out var entry)) { WarnReaction(args.Kind, "a recognised kind with no look yet: nothing is drawn"); ReactionsRefused++; return; }
            var eye = Camera.main;
            if (eye != null && (args.Point - eye.transform.position).sqrMagnitude < EyeClearMeters * EyeClearMeters) { ReactionsRefused++; return; }
            PruneReactions();
            while (_reactions.Count >= MaxReactionBodies)
            {
                var oldest = _reactions[0];
                _reactions.RemoveAt(0);
                if (oldest != null) oldest.Fade(ReactionFade);
            }
            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(entry.Prefab, args.Point, Quaternion.identity);
                instance.name = "Spell308_React_" + args.Kind;
                if (_owner != null && instance.scene != _owner.gameObject.scene) SceneManager.MoveGameObjectToScene(instance, _owner.gameObject.scene);
                var body = SpellReactionDress308.Dress(instance, entry, entry.Prefab.transform.localScale, args);
                if (body == null) { Destroy(instance); ReactionsRefused++; return; }
                _reactions.Add(body);
                ReactionsShown++;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (instance != null) Destroy(instance);
                ReactionsRefused++;
            }
        }

        void WarnReaction(ReactionKind308 kind, string why)
        {
            if (!_reactionWarned.Add(kind)) return;
            ReactionWarnings++;
            Debug.LogWarning("[Spell308] reaction look " + kind + ": " + why);
        }

        void PruneReactions() => _reactions.RemoveAll(b => b == null || b.Gone);

        void ClearReaction308()
        {
            foreach (var body in _reactions) if (body != null) body.Kill();
            _reactions.Clear();
        }
    }
}
