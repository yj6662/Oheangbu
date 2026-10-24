// PURE308
using System;

namespace Oheangbu.Drawing
{
    // D308-147 / D308-148 (debate 32, plan C): what the draw key's press means. Q pressed with a riposte (앞잡) candidate
    // enters the draw mode as usual, but the presentation bundle (slow-down, close-up, swing cut, arm pose) waits for the
    // first stroke. A candidate released without a stroke is the riposte; a stroke makes it a drawing.
    // The controller keeps the facts of one Q gesture in DrawGestureDecision308 and asks the arbiter (the combat side)
    // through this interface. Drawing never references the combat side: the arbiter is set from outside, like EntryAllowed.

    /// <summary>What the arbiter answers when the draw key goes down.</summary>
    public enum DrawPressMode
    {
        /// <summary>Not a candidate: an ordinary draw, the presentation starts with the mode.</summary>
        None = 0,
        /// <summary>A candidate whose presentation waits for the first stroke.</summary>
        Deferred = 1,
        /// <summary>A candidate that presents at once (the old early presentation, kept for A/B).</summary>
        Immediate = 2,
    }

    /// <summary>The combat side of the draw key. Set on <c>DrawingInputController.PressArbiter</c>; one owner, no global.</summary>
    public interface IDrawPressArbiter
    {
        /// <summary>The key went down (gates passed, the world is running, the draw mode is not entered yet).
        /// A candidate fixes its target and window here and consumes nothing.</summary>
        DrawPressMode BeginCandidate();
        /// <summary>The candidate's first stroke began: the gesture is a drawing, the window's riposte is gone.</summary>
        void OnStrokeChosen();
        /// <summary>The key came up with no stroke ever started. The draw mode has been left quietly already (not entered
        /// any more). true = the arbiter acted on it.</summary>
        bool OnReleasedWithoutStroke();
        /// <summary>The gesture ended without a choice (a menu, a shut gate, a disable): nothing is fired later.</summary>
        void CancelCandidate();
    }

    public enum DrawRelease308
    {
        /// <summary>The ordinary release: the drawing is committed (or quietly dropped when it is too short).</summary>
        Commit = 0,
        /// <summary>A candidate that never drew: leave quietly and hand the release to the arbiter. No commit.</summary>
        RiposteRelease = 1,
    }

    /// <summary>What the first stroke of a gesture asks of the controller.</summary>
    public readonly struct DrawStrokeStart308
    {
        /// <summary>Start the presentation bundle now (before StrokeStarted is raised).</summary>
        public readonly bool BeginPresentation;
        /// <summary>Tell the arbiter the candidate became a drawing.</summary>
        public readonly bool ChooseDraw;
        public DrawStrokeStart308(bool beginPresentation, bool chooseDraw) { BeginPresentation = beginPresentation; ChooseDraw = chooseDraw; }
    }

    /// <summary>The facts of one Q gesture, no engine objects. The controller owns one instance and feeds it press, first
    /// stroke, release and end. <see cref="HasStartedStroke"/> lives for the whole gesture: InterruptLetter clears the
    /// strokes but never this.</summary>
    public sealed class DrawGestureDecision308
    {
        private enum Phase { Idle, Plain, Candidate, DrawChosen, Released }

        private Phase _phase;

        /// <summary>A gesture is running (pressed, not yet ended).</summary>
        public bool Active => _phase != Phase.Idle;
        /// <summary>A riposte candidate that has not drawn: the only state whose release is the riposte.</summary>
        public bool IsCandidate => _phase == Phase.Candidate;
        /// <summary>The gesture started a stroke at least once (kept through a hit that clears the strokes).</summary>
        public bool HasStartedStroke { get; private set; }
        /// <summary>The presentation bundle has been started for this gesture.</summary>
        public bool PresentationBegun { get; private set; }

        /// <summary>The key went down. mode comes from the arbiter (None without one).</summary>
        public void Press(DrawPressMode mode)
        {
            HasStartedStroke = false;
            switch (mode)
            {
                case DrawPressMode.Deferred: _phase = Phase.Candidate; PresentationBegun = false; break;
                case DrawPressMode.Immediate: _phase = Phase.Candidate; PresentationBegun = true; break;
                default: _phase = Phase.Plain; PresentationBegun = true; break;
            }
        }

        /// <summary>The input controller accepted a stroke start. Only the gesture's first stroke asks for anything.</summary>
        public DrawStrokeStart308 StrokeStarted()
        {
            if (_phase == Phase.Idle || HasStartedStroke) return default;
            HasStartedStroke = true;
            if (_phase != Phase.Candidate) return default;
            _phase = Phase.DrawChosen;
            bool begin = !PresentationBegun;
            PresentationBegun = true;
            return new DrawStrokeStart308(begin, true);
        }

        /// <summary>The key came up (after this frame's stroke handling, before the commit).</summary>
        public DrawRelease308 Release()
        {
            if (_phase == Phase.Candidate && !HasStartedStroke) { _phase = Phase.Released; return DrawRelease308.RiposteRelease; }
            return DrawRelease308.Commit;
        }

        /// <summary>The gesture is over (any exit). true = a candidate ended undecided: the arbiter must be told to cancel.
        /// A riposte release answered false here - it was decided, not cancelled.</summary>
        public bool End()
        {
            bool undecided = _phase == Phase.Candidate;
            _phase = Phase.Idle; HasStartedStroke = false; PresentationBegun = false;
            return undecided;
        }
    }
}
