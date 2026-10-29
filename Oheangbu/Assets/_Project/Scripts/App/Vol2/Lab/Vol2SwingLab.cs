using System;
using System.Linq;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 9 (D308-150 V2): the swing pass of the spell lab. After the 125 rows and the reaction pass the lab runs the ㅓ rows (every row that swings) AGAIN
    /// with real swings: instead of raising the swing signal by hand, a scripted brush is clicked through the scope's swing driver (the production Vol2SwingDriver), which opens a
    /// SwingId, sweeps its capsule over the clip's contact window and raises the swing signal itself. The rows keep their expectations (the same pose, the same dummies); what
    /// changes is the path to the contacts. Then four 갈무리 cases on the REAL combat wiring (hit / air / one body per swing / cooldown), and one look at the production
    /// brush source. Counted apart (swingRows / swingPass / swingFail in the summary); a failure fails the run.</summary>
    public static class Vol2SwingLab
    {
        public const string Scenario = "S9-swing";
    }

    /// <summary>The scope's swing driver, borrowed for one row: a scripted source, a recording 갈무리 port, the cooldown gate off (the ㅓ rows swing 0.4 s apart to show the
    /// second contact; the rules' 1.1 s is the 갈무리 rows' business). End() puts everything back.</summary>
    public sealed class SwingPassRig
    {
        public Vol2SwingDriver Driver { get; private set; }
        public Vol2ScriptedSwingSource Script { get; private set; }
        public Vol2RecordingGatherPort Recorder { get; private set; }
        IVol2SwingSource _source;
        IVol2SwingGatherPort _gather;
        bool _gate;
        int _swings0, _refused0, _cancelled0;

        public static SwingPassRig Begin(Vol2ShapeRuntime shapes, Vol2Services services)
        {
            var driver = shapes != null ? shapes.Swing : null;
            if (driver == null) throw new InvalidOperationException("the scope has no swing driver (Vol2DefenceInstaller.BindSeams did not bind one)");
            var rig = new SwingPassRig
            {
                Driver = driver, Script = new Vol2ScriptedSwingSource(services.Clock), Recorder = new Vol2RecordingGatherPort(),
                _source = driver.Source, _gather = driver.Gather, _gate = driver.Tuning.GateByCooldown,
                _swings0 = driver.Swings, _refused0 = driver.Refused, _cancelled0 = driver.Cancelled
            };
            driver.Reset();
            driver.Source = rig.Script; driver.Gather = rig.Recorder; driver.Tuning.GateByCooldown = false;
            return rig;
        }

        public void Click(Vector3 sleeve, Vector3 tip) { Script.Aim(sleeve, tip); Driver.TryBeginSwing(); }

        /// <summary>The driver's books after the row: every click became a swing, every swing was reported to the 갈무리 exactly once, none was cut, none is left open.</summary>
        public void Judge(Vol2OnceVerdict verdict, int expectedSwings)
        {
            int swings = Driver.Swings - _swings0, refused = Driver.Refused - _refused0, cancelled = Driver.Cancelled - _cancelled0;
            int reports = Recorder.Hits.Count + Recorder.Misses.Count;
            var ids = Recorder.Hits.Select(h => h.swing).Concat(Recorder.Misses).ToList();
            var problems = new System.Collections.Generic.List<string>();
            if (swings != expectedSwings) problems.Add("driver opened " + swings + " swings for " + expectedSwings + " clicks");
            if (refused != 0) problems.Add(refused + " clicks refused");
            if (cancelled != 0) problems.Add(cancelled + " swings cancelled before their window");
            if (reports != swings) problems.Add(reports + " 갈무리 reports for " + swings + " swings (exactly one each)");
            if (ids.Distinct().Count() != ids.Count) problems.Add("a swing was reported twice");
            if (Driver.InSwing) problems.Add("a swing is still open");
            string line = "swing driver: " + swings + " swings, " + Driver.RaisedEvents + " events total, first hits " + Recorder.Hits.Count + " (" + string.Join("|", Recorder.Hits.Select(h => h.target.Replace("Vol2LabDummy_", ""))) + "), misses " + Recorder.Misses.Count;
            if (problems.Count > 0) { verdict.Pass = false; verdict.Note += (verdict.Note.Length > 0 ? "; " : "") + string.Join(", ", problems); }
            verdict.Checks += (verdict.Checks.Length > 0 ? " | " : "") + line;
        }

        public void End()
        {
            if (Driver == null) return;
            Driver.Reset();
            Driver.Source = _source; Driver.Gather = _gather; Driver.Tuning.GateByCooldown = _gate;
            Driver = null;
        }
    }
}
