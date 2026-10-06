using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    // A presenter that can say, without drawing anything, whether it has something to show for a request. The guard asks it
    // while a cast's presentation is held back, so a handler that lets the stroke adapter draw when the presenter has
    // nothing (handle.Shown) still decides right.
    public interface ISpellPresenterProbe
    {
        bool CanShow(in SpellFxRequest request);
    }

    // #308 the presenter every effect handler really talks to (SPEC-SPELL-120-308 section 9, decision of 2026-10-04:
    // "a presentation failure never changes rules"). It stands between the handlers and the registered ISpellPresenter:
    //  - isolated: every call into the presenter runs inside its own guard. A presenter that throws is reported and the
    //    call answers as if nothing was shown; no exception ever reaches handler code;
    //  - after the rules: while the wiring holds it (around ISpellEffect.Commit), nothing reaches the presenter. Begin
    //    answers a handle of this guard and the request waits; once the cast is judged the wiring releases the guard and
    //    the waiting calls run in the order they were made. A cast that was withdrawn shows nothing;
    //  - best effort: the handles it gives out are its own numbers. Whether the presenter really showed something is
    //    never visible to a rule (handlers only pass handles back for cues).
    public sealed class SpellPresenterGuard308 : ISpellPresenter
    {
        enum Call { Begin, Cue, End }
        struct Waiting
        {
            public Call Call; public int Id;
            public SpellFxRequest Request;
            public SpellFxCue Cue; public SpellFxCueArgs Args;
            public bool Immediate;
        }

        // Handles whose presentation may still be alive. A bound of bookkeeping, not a balance number: the oldest handle
        // is forgotten first (its later cues are then ignored, like the cues of a presentation that already ended).
        const int HandleCapacity = 256;

        ISpellPresenter _inner;
        ISpellPresenterProbe _probe;
        readonly List<Waiting> _waiting = new List<Waiting>();
        readonly Dictionary<int, SpellFxHandle> _shown = new Dictionary<int, SpellFxHandle>();
        readonly Queue<int> _order = new Queue<int>();
        int _next, _holds;

        public int Faults { get; private set; }             // presenter calls that threw (checks read this)
        public int WaitingCalls => _waiting.Count;
        public bool Holding => _holds > 0;

        public void Bind(ISpellPresenter inner)
        {
            _inner = inner is SpellPresenterGuard308 ? null : inner;
            _probe = _inner as ISpellPresenterProbe;
        }

        // From here on calls wait. Nested holds count.
        public void Hold() { _holds++; }

        // show = the rules of the held cast are in: everything that waited is presented now. false = the cast was
        // withdrawn: what waited is dropped.
        public void Release(bool show)
        {
            if (_holds > 0) _holds--;
            if (_holds > 0) return;
            if (!show) { _waiting.Clear(); return; }
            // a presenter call may ask for more (a cue that begins a body): those calls run at once, in order
            for (int i = 0; i < _waiting.Count; i++)
            {
                var call = _waiting[i];
                switch (call.Call)
                {
                    case Call.Begin: Show(call.Id, call.Request); break;
                    case Call.Cue: Pass(call.Id, call.Cue, call.Args); break;
                    default: Finish(call.Id, call.Immediate); break;
                }
            }
            _waiting.Clear();
        }

        public SpellFxHandle Begin(in SpellFxRequest request)
        {
            if (_inner == null) return default;
            if (_holds > 0)
            {
                if (!Probe(request)) return default;        // nothing to show: Id 0, exactly as the presenter would answer
                int held = NextId();
                _waiting.Add(new Waiting { Call = Call.Begin, Id = held, Request = request });
                return new SpellFxHandle(held);
            }
            int id = NextId();
            return Show(id, request) ? new SpellFxHandle(id) : default;
        }

        public void Cue(SpellFxHandle handle, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (_inner == null || !handle.Shown) return;
            if (_holds > 0) { _waiting.Add(new Waiting { Call = Call.Cue, Id = handle.Id, Cue = cue, Args = args }); return; }
            Pass(handle.Id, cue, args);
        }

        public void End(SpellFxHandle handle, bool immediate = false)
        {
            if (_inner == null || !handle.Shown) return;
            if (_holds > 0) { _waiting.Add(new Waiting { Call = Call.End, Id = handle.Id, Immediate = immediate }); return; }
            Finish(handle.Id, immediate);
        }

        // Death, rest, scene leave: nothing waits any longer and every handle is forgotten.
        public void EndAll()
        {
            _waiting.Clear(); _shown.Clear(); _order.Clear();
            if (_inner == null) return;
            try { _inner.EndAll(); }
            catch (Exception exception) { Fault(exception); }
        }

        bool Probe(in SpellFxRequest request)
        {
            if (_probe == null) return true;
            try { return _probe.CanShow(request); }
            catch (Exception exception) { Fault(exception); return false; }
        }

        bool Show(int id, in SpellFxRequest request)
        {
            SpellFxHandle shown;
            try { shown = _inner.Begin(request); }
            catch (Exception exception) { Fault(exception); return false; }
            if (!shown.Shown) return false;
            while (_order.Count >= HandleCapacity) _shown.Remove(_order.Dequeue());
            _shown[id] = shown; _order.Enqueue(id);
            return true;
        }

        void Pass(int id, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (!_shown.TryGetValue(id, out var shown)) return;
            try { _inner.Cue(shown, cue, args); }
            catch (Exception exception) { Fault(exception); }
        }

        void Finish(int id, bool immediate)
        {
            if (!_shown.TryGetValue(id, out var shown)) return;
            _shown.Remove(id);
            try { _inner.End(shown, immediate); }
            catch (Exception exception) { Fault(exception); }
        }

        int NextId()
        {
            int id = ++_next;
            if (id == 0) id = ++_next;
            return id;
        }

        void Fault(Exception exception)
        {
            Faults++;
            Debug.LogException(exception);
        }
    }
}
