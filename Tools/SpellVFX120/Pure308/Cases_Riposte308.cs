using System.Collections.Generic;
using Oheangbu.App.World;
using Oheangbu.Drawing;

namespace Pure308
{
    // #308 D308-147 step 3 (debate 32, plan C): the riposte input split, rule half. Runs the REAL pure classes the game uses:
    //   DrawGestureDecision308 (Drawing) = what one Q gesture means, RiposteCandidate308 + RiposteWindowClaim308 (App) = the window.
    // The harness below only replays the order DrawingInputController calls them in (press -> stroke / release -> end).
    // Fixtures: RiposteGestureDecision308Tests (RGD), DrawingRiposteBridge308Tests (DRB, the rule half), RiposteWindowClaim308Tests (RWC).
    // What it cannot see: the controller's Unity half, the real Input System frame order, EnemyVitals - Riposte308Checks (edit mode) and Play.
    public static partial class Program
    {
        // One Q gesture the way DrawingInputController drives it. World = what Riposte301 reads: the key of the lock target's open window
        // (none = closed / no target) and whether the world lets a volley start (same lock, ready, in reach, able to act).
        sealed class Rig
        {
            public readonly RiposteCandidate308 Candidate = new RiposteCandidate308();
            public readonly DrawGestureDecision308 Gesture = new DrawGestureDecision308();
            public RiposteWindowKey308 World;       // the window open on the lock target right now
            public bool Eligible = true;            // the world lets a volley start now
            public bool Defer = true;               // the profile switch
            public int Presentations, Volleys, ChooseDraws, Commits, QuietExits, Cancels;
            public bool InMode;

            public DrawPressMode Press()
            {
                var mode = DrawPressMode.None;
                if (Candidate.Begin(World, Eligible)) mode = Defer ? DrawPressMode.Deferred : DrawPressMode.Immediate;
                InMode = true;
                Gesture.Press(mode);
                if (mode != DrawPressMode.Deferred) Presentations++;
                return mode;
            }

            public void Stroke()
            {
                var first = Gesture.StrokeStarted();
                if (first.ChooseDraw) { Candidate.StrokeChosen(); ChooseDraws++; }
                if (first.BeginPresentation) Presentations++;
            }

            // Q came up: after the frame's Stroke(), before the commit
            public DrawRelease308 Release()
            {
                if (!InMode) return DrawRelease308.Commit;   // the controller's Update returns before the release when the mode is gone
                var release = Gesture.Release();
                if (release == DrawRelease308.RiposteRelease)
                {
                    Exit(quiet: true);
                    if (Candidate.ReleaseWithoutStroke(World, Eligible) && Candidate.TryClaim(World)) Volleys++;
                }
                else { Commits++; Exit(quiet: false); }
                return release;
            }

            // CancelForUi / a shut gate / a disable
            public void Cancel() { Exit(quiet: true); }

            void Exit(bool quiet)
            {
                if (!InMode) return;
                InMode = false;
                if (Gesture.End()) { Candidate.Cancel(); Cancels++; }
                if (quiet) QuietExits++;
            }

            public static RiposteWindowKey308 Window(int enemy = 7, uint life = 1, uint generation = 1) => new RiposteWindowKey308(enemy, life, generation);
        }

        static partial void RunRiposte308(Report r, Context c)
        {
            // ---------------------------------------------------------------- RiposteGestureDecision308Tests (RGD)
            {   // RGD-1: a press with a candidate is only a candidate: the mode is on, the presentation waits, nothing fires
                var g = new Rig { World = Rig.Window() };
                var mode = g.Press();
                r.Check("RGD-1", mode == DrawPressMode.Deferred && g.Gesture.IsCandidate && !g.Gesture.PresentationBegun && g.Presentations == 0 && g.Volleys == 0 && g.Candidate.Active,
                    "a press with a riposte candidate: candidate only, presentation 0, no volley");
            }
            {   // RGD-2: the first stroke starts the presentation once and gives the window to the drawing; the release then commits
                var g = new Rig { World = Rig.Window() };
                g.Press(); g.Stroke(); g.Stroke();
                var release = g.Release();
                r.Check("RGD-2", g.Presentations == 1 && g.ChooseDraws == 1 && g.Gesture.PresentationBegun == false /* ended */ && release == DrawRelease308.Commit && g.Commits == 1 && g.Volleys == 0 && g.Cancels == 0,
                    "first stroke: presentation 1, draw chosen 1, later strokes add nothing, the release commits, no volley, no cancel");
                r.Check("RGD-2", !g.Candidate.Claims.CanOfferRiposte(g.World) && g.Candidate.Claims.ChoiceOf(g.World) == RiposteWindowChoice308.Draw,
                    "the window now belongs to the drawing: no riposte is offered in it");
            }
            {   // RGD-3: a candidate released with no stroke is the riposte, once; no commit, no cancel
                var g = new Rig { World = Rig.Window() };
                g.Press();
                var release = g.Release();
                r.Check("RGD-3", release == DrawRelease308.RiposteRelease && g.Volleys == 1 && g.Commits == 0 && g.Cancels == 0 && g.Presentations == 0 && g.QuietExits == 1 && !g.InMode,
                    "release without a stroke: one volley, no commit, no cancel, presentation never began, the mode left quietly");
                r.Check("RGD-3", g.Candidate.Claims.ChoiceOf(g.World) == RiposteWindowChoice308.Riposte && g.Candidate.Claims.BlocksNewDraw(g.World),
                    "the window belongs to the riposte: a new drawing in it is blocked");
            }
            {   // RGD-4: the first stroke and the release in one frame (stroke handled first): a drawing, no volley
                var g = new Rig { World = Rig.Window() };
                g.Press(); g.Stroke();
                var release = g.Release();
                r.Check("RGD-4", release == DrawRelease308.Commit && g.Volleys == 0 && g.Presentations == 1 && g.ChooseDraws == 1,
                    "stroke and release in the same frame: the stroke wins, a drawing, no volley");
            }
            {   // RGD-5: press and release in the same frame fire once; a repeated call does nothing
                var g = new Rig { World = Rig.Window() };
                g.Press();
                g.Release();
                g.Release();
                r.Check("RGD-5", g.Volleys == 1 && g.Commits == 0, "press and release in one frame: one volley, a second release call is nothing");
            }
            {   // RGD-6: a hit that clears the strokes (InterruptLetter) or a short / failed drawing never turns back into a riposte
                var g = new Rig { World = Rig.Window() };
                g.Press(); g.Stroke();
                // InterruptLetter / a short commit / a failed recognition leave the decision's record alone
                bool kept = g.Gesture.HasStartedStroke;
                var release = g.Release();
                r.Check("RGD-6", kept && release == DrawRelease308.Commit && g.Volleys == 0 && g.Commits == 1 && g.Candidate.Claims.ChoiceOf(g.World) == RiposteWindowChoice308.Draw,
                    "after a first stroke the record stays through a hit / a short drawing / a failed recognition: never a riposte");
            }
            {   // RGD-7: the window expired between press and release: nothing happens
                var g = new Rig { World = Rig.Window() };
                g.Press(); g.World = default;
                g.Release();
                r.Check("RGD-7", g.Volleys == 0 && g.Commits == 0 && g.Candidate.Claims.Count == 0, "release after the window closed: no volley, no commit, nothing claimed");
            }
            {   // RGD-8: out of reach / lock swapped / not able to act at the release: nothing happens
                var g = new Rig { World = Rig.Window() };
                g.Press(); g.Eligible = false;
                g.Release();
                r.Check("RGD-8", g.Volleys == 0 && g.Commits == 0 && g.Candidate.Claims.ChoiceOf(Rig.Window()) == RiposteWindowChoice308.None,
                    "release out of reach / on another lock / unable to act: no volley, no other target, the window stays open for a later press");
            }
            {   // RGD-9: the target was regenerated (new life) or the window re-opened (new generation): the old candidate is stale
                var life = new Rig { World = Rig.Window(7, 1, 1) };
                life.Press(); life.World = Rig.Window(7, 2, 1); life.Release();
                var gen = new Rig { World = Rig.Window(7, 1, 1) };
                gen.Press(); gen.World = Rig.Window(7, 1, 2); gen.Release();
                var other = new Rig { World = Rig.Window(7, 1, 1) };
                other.Press(); other.World = Rig.Window(8, 1, 1); other.Release();
                r.Check("RGD-9", life.Volleys == 0 && gen.Volleys == 0 && other.Volleys == 0,
                    "release on a regenerated target, a re-opened window or another enemy's window: no volley (no auto-switch)");
            }
            {   // RGD-10: a candidate cancelled by a menu / a dodge / death: Q up later does nothing
                var g = new Rig { World = Rig.Window() };
                g.Press(); g.Cancel();
                g.Release();
                r.Check("RGD-10", g.Volleys == 0 && g.Cancels == 1 && !g.Candidate.Active && !g.Gesture.Active, "cancelled candidate: the controller is told to cancel once, a later release fires nothing");
            }
            {   // RGD-11: mashing Q: the same window gives one volley; the next presses are ordinary draws
                var g = new Rig { World = Rig.Window() };
                g.Press(); g.Release();
                var again = g.Press();
                g.Release();
                var third = g.Press(); g.Stroke(); g.Release();
                r.Check("RGD-11", g.Volleys == 1 && again == DrawPressMode.None && third == DrawPressMode.None && g.ChooseDraws == 0,
                    "Q mash in one window: one volley, the other presses are not candidates (ordinary draws)");
                r.Check("RGD-11", g.Commits == 2, "the ordinary draws commit as before");
                // the next window of the same enemy is offered normally
                g.World = Rig.Window(7, 1, 2);
                var next = g.Press(); g.Release();
                r.Check("RGD-11", next == DrawPressMode.Deferred && g.Volleys == 2, "the next window is a normal candidate");
            }
            {   // RGD-12: a drawing in a window, a volley on another enemy, and back: the original window's riposte does not come back
                var g = new Rig { World = Rig.Window(7, 1, 1) };
                g.Press(); g.Stroke(); g.Release();
                g.World = Rig.Window(8, 1, 1);
                g.Press(); g.Release();
                g.World = Rig.Window(7, 1, 1);
                var back = g.Press(); g.Release();
                r.Check("RGD-12", back == DrawPressMode.None && g.Volleys == 1, "lock A (drawn) -> B (volley) -> A: A's window is still answered; B's volley stands");
            }
            {   // RGD-13: the A/B switch: early presentation on the press, the same release decision
                var g = new Rig { World = Rig.Window(), Defer = false };
                var mode = g.Press();
                bool early = g.Presentations == 1;
                g.Release();
                var s = new Rig { World = Rig.Window(), Defer = false };
                s.Press(); s.Stroke(); s.Release();
                r.Check("RGD-13", mode == DrawPressMode.Immediate && early && g.Volleys == 1 && s.Presentations == 1 && s.ChooseDraws == 1 && s.Volleys == 0,
                    "profile switch off: presentation on the press (A), the release still decides the riposte, a stroke still takes the window");
            }
            {   // RGD-14: no candidate at all (no window): the press is an ordinary draw with presentation on the press
                var g = new Rig { World = default };
                var mode = g.Press();
                g.Stroke();
                g.Release();
                r.Check("RGD-14", mode == DrawPressMode.None && g.Presentations == 1 && g.ChooseDraws == 0 && g.Commits == 1 && g.Volleys == 0,
                    "no candidate: presentation 1 on the press, a stroke chooses nothing, the release commits");
            }
            {   // RGD-15: a stroke held from before the press (the 'entered holding' rule): the first stroke is the same frame as the press
                var g = new Rig { World = Rig.Window() };
                g.Press(); g.Stroke();
                r.Check("RGD-15", g.Presentations == 1 && g.ChooseDraws == 1 && g.Gesture.HasStartedStroke, "a stroke already held at the press: the presentation starts in that frame");
            }

            // ---------------------------------------------------------------- DrawingRiposteBridge308Tests (DRB): the rule half
            {   // DRB-1: presentation events: candidate only = 0, first stroke = 1, ordinary = 1 on the press
                var cand = new Rig { World = Rig.Window() }; cand.Press(); cand.Release();
                var drew = new Rig { World = Rig.Window() }; drew.Press(); drew.Stroke(); drew.Release();
                var plain = new Rig(); plain.Press(); plain.Release();
                r.Check("DRB-1", cand.Presentations == 0 && drew.Presentations == 1 && plain.Presentations == 1, "presentation count: candidate only 0, first stroke 1, ordinary press 1");
            }
            {   // DRB-2: the release consumed by the riposte never reaches the commit; the first stroke is never lost
                var cand = new Rig { World = Rig.Window() }; cand.Press(); cand.Release();
                var drew = new Rig { World = Rig.Window() }; drew.Press(); drew.Stroke(); drew.Release();
                r.Check("DRB-2", cand.Commits == 0 && drew.Commits == 1, "a riposte release does not commit; a gesture with a stroke does");
            }
            {   // DRB-3: the record lives for the gesture and is gone after it
                var g = new Rig { World = Rig.Window() };
                g.Press(); g.Stroke();
                bool during = g.Gesture.HasStartedStroke;
                g.Release();
                r.Check("DRB-3", during && !g.Gesture.HasStartedStroke && !g.Gesture.Active, "HasStartedStroke lives through the gesture and is clear after the exit");
            }

            // ---------------------------------------------------------------- RiposteWindowClaim308Tests (RWC)
            {   // RWC-1: one answer per window
                var claim = new RiposteWindowClaim308(); var w = Rig.Window();
                bool first = claim.TryChooseRiposte(w), second = claim.TryChooseRiposte(w), draw = claim.TryChooseDraw(w);
                var claim2 = new RiposteWindowClaim308();
                bool d1 = claim2.TryChooseDraw(w), d2 = claim2.TryChooseDraw(w), riposte = claim2.TryChooseRiposte(w);
                r.Check("RWC-1", first && !second && !draw && d1 && d2 && !riposte, "a window takes one answer: riposte then riposte / draw refused, draw then draw ok / riposte refused");
            }
            {   // RWC-2: the key names enemy + life + opening; other windows are unaffected
                var claim = new RiposteWindowClaim308();
                claim.TryChooseRiposte(Rig.Window(7, 1, 1));
                r.Check("RWC-2", claim.CanOfferRiposte(Rig.Window(7, 1, 2)) && claim.CanOfferRiposte(Rig.Window(7, 2, 1)) && claim.CanOfferRiposte(Rig.Window(8, 1, 1)) && !claim.CanOfferRiposte(Rig.Window(7, 1, 1)),
                    "another opening, another life, another enemy: offered; the answered window: not");
                r.Check("RWC-2", !claim.CanOfferRiposte(default) && !claim.TryChooseRiposte(default) && !claim.TryChooseDraw(default) && !claim.BlocksNewDraw(default), "no window (key none) is never offered, taken or blocked");
            }
            {   // RWC-3: a stale (older) key never overwrites the newer window of the same enemy
                var claim = new RiposteWindowClaim308();
                claim.TryChooseDraw(Rig.Window(7, 1, 3));
                bool older = claim.TryChooseRiposte(Rig.Window(7, 1, 2)), olderLife = claim.TryChooseRiposte(Rig.Window(7, 0, 9));
                r.Check("RWC-3", !older && !olderLife && claim.ChoiceOf(Rig.Window(7, 1, 3)) == RiposteWindowChoice308.Draw && claim.Count == 1, "an older window of the same enemy is refused and changes nothing");
                bool newer = claim.TryChooseRiposte(Rig.Window(7, 1, 4));
                r.Check("RWC-3", newer && claim.ChoiceOf(Rig.Window(7, 1, 4)) == RiposteWindowChoice308.Riposte && claim.Count == 1, "a newer window replaces the enemy's entry");
            }
            {   // RWC-4: a late finish must not clear the next window (the ownership test the volley's Finish relies on)
                var mine = Rig.Window(7, 1, 1);
                r.Check("RWC-4", RiposteWindowClaim308.OwnsWindow(mine, Rig.Window(7, 1, 1)) && !RiposteWindowClaim308.OwnsWindow(mine, Rig.Window(7, 1, 2)) &&
                    !RiposteWindowClaim308.OwnsWindow(mine, Rig.Window(7, 2, 1)) && !RiposteWindowClaim308.OwnsWindow(mine, default) && !RiposteWindowClaim308.OwnsWindow(default, default),
                    "a volley owns only its own window: the next opening, a new life or no window is not its to close");
            }
            {   // RWC-5: the claim list is bounded and keeps the newest
                var claim = new RiposteWindowClaim308();
                for (int i = 1; i <= 200; i++) claim.TryChooseDraw(Rig.Window(i, 1, 1));
                r.Check("RWC-5", claim.Count <= 64 && claim.ChoiceOf(Rig.Window(200, 1, 1)) == RiposteWindowChoice308.Draw && claim.ChoiceOf(Rig.Window(1, 1, 1)) == RiposteWindowChoice308.None, "bounded at 64 entries, the oldest go first");
            }
            {   // RWC-6: Clear
                var claim = new RiposteWindowClaim308(); claim.TryChooseRiposte(Rig.Window());
                claim.Clear();
                r.Check("RWC-6", claim.Count == 0 && claim.CanOfferRiposte(Rig.Window()), "clear forgets every window");
            }
        }
    }
}
