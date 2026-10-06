// PURE308
using System;

namespace Oheangbu.App
{
    public enum SpellDispatchOutcome308 { Refused, Withdrawn, Faulted, Cast }

    // The steps of one handler cast. The wiring implements them on the real objects (CombatLoopWiring.Spell308.cs); the
    // offline runner implements them on a model and checks the order below.
    public interface ISpellDispatchSteps308
    {
        bool Prepare();                   // validation only: no ink, no state, nothing shown
        bool Spend();                     // the ink of the cast; false = not enough
        bool Commit();                    // rules only: hits are scheduled, lasting state is set. Presentation asked for in here waits
        // The cast is void as a whole: every hit Commit scheduled is taken back, the waiting presentation is dropped and
        // the ink is restored. faulted = Commit threw: the handler is also reset, so no half-set state outlives the refund.
        void Withdraw(bool faulted);
        void Accept();                    // the judged cast is announced (CastAccepted, accept hooks)
        void Present();                   // what waited is shown now
        void Fail();                      // the strokes evaporate
        void Report(Exception exception);
    }

    // #308 the order of a handler cast (decision of 2026-10-04: a presentation failure never changes rules).
    //   Prepare -> ink -> Commit (rules) -> accept -> presentation
    // Two things can never happen:
    //  - ink back while hits of the cast are still scheduled or its state is still on: the only refund is Withdraw, which
    //    takes the hits back first (and resets a handler that threw);
    //  - a cast undone by its presentation: once Commit has answered true the cast is judged. Whatever a listener or the
    //    presenter does after that is reported and ignored: the ink stays spent and every scheduled hit lands.
    public static class SpellDispatchRule308
    {
        public static SpellDispatchOutcome308 Run<T>(ref T steps) where T : ISpellDispatchSteps308
        {
            // 1. validation: a refusal is "no cast". Nothing was spent, nothing is taken back, no misfire ink on top.
            bool prepared;
            try { prepared = steps.Prepare(); }
            catch (Exception exception) { steps.Report(exception); prepared = false; }
            if (!prepared) { steps.Fail(); return SpellDispatchOutcome308.Refused; }

            // 2. ink
            bool spent;
            try { spent = steps.Spend(); }
            catch (Exception exception) { steps.Report(exception); Undo(ref steps, false); steps.Fail(); return SpellDispatchOutcome308.Refused; }
            if (!spent) { steps.Fail(); return SpellDispatchOutcome308.Refused; }

            // 3. rules
            bool committed, faulted = false;
            try { committed = steps.Commit(); }
            catch (Exception exception) { steps.Report(exception); committed = false; faulted = true; }
            if (!committed)
            {
                Undo(ref steps, faulted);
                steps.Fail();
                return faulted ? SpellDispatchOutcome308.Faulted : SpellDispatchOutcome308.Withdrawn;
            }

            // 4. the cast is judged: announced, then shown. Neither can undo it.
            try { steps.Accept(); }
            catch (Exception exception) { steps.Report(exception); }
            try { steps.Present(); }
            catch (Exception exception) { steps.Report(exception); }
            return SpellDispatchOutcome308.Cast;
        }

        static void Undo<T>(ref T steps, bool faulted) where T : ISpellDispatchSteps308
        {
            try { steps.Withdraw(faulted); }
            catch (Exception exception) { steps.Report(exception); }
        }
    }
}
