"""Dry contract tests: temporary files and fake calls only; never calls Unity."""
import contextlib, io, json, os, tempfile, time, unittest
from pathlib import Path
from unittest.mock import patch
import verify_architecture296 as verify


class Contract(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.commands, self.output = [], io.StringIO()
        self.lookup = {cmd: (path, kind) for rows in verify.PHASES.values() for cmd, path, kind in rows}

    def write(self, name, value):
        path = self.folder / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value) if isinstance(value, dict) else value, encoding="utf-8")
        return path

    def caller(self, kind, method, command, timeout):
        self.commands.append(command)
        print("RAW_RESPONSE_MUST_STAY_HIDDEN")
        if command == "status":
            return dict(id="mock", status="COMPLETE", result="play=False dirty=False scene=Assets/_Project/Art/World/Architecture296/test.unity")
        path, format = self.lookup[command]
        state = dict(Active=False, Finished=True, Restored=True, Status="PASS", Checks=["PASS observed"], Failures=[])
        if command.startswith("mum"): state = {k.lower(): v for k, v in state.items()}
        self.write(path, state if format == "runtime" else "PASS observed\n")
        if format == "traversal":
            self.write("traversal-status.json", dict(Command=command, Active=False, Finished=True, Restored=True, Status="PASS"))
        return dict(id="mock", status="COMPLETE", result="Scheduled" if format in ("runtime", "traversal", "perf") else "PASS observed")

    def runner(self, caller=None):
        with contextlib.redirect_stdout(self.output):
            return verify.Runner(self.folder, caller or self.caller)

    def test_edit_order_and_summary_only(self):
        self.assertTrue(self.runner().execute("edit"))
        self.assertEqual([c for c in self.commands if c != "status"], [r[0] for r in verify.PHASES["edit"]])
        self.assertNotIn("RAW_RESPONSE", self.output.getvalue())
        self.assertEqual(json.loads((self.folder/"Analysis/final-verification-progress.json").read_text())["runs"][0]["status"], "PASS")

    def test_play_uppercase_and_lowercase_completion(self):
        self.assertTrue(self.runner().execute("play"))
        self.assertEqual([c for c in self.commands if c != "status"], [r[0] for r in verify.PHASES["play"]])

    def test_service_complete_with_fail_stops_before_next(self):
        def fail(*args):
            result = self.caller(*args)
            if args[2] == "check": result["result"] = "PASS first\nFAIL actual collider"
            return result
        self.assertFalse(self.runner(fail).execute("edit"))
        self.assertEqual(self.commands, ["status", "check"])

    def test_service_error_stops_before_next(self):
        def fail(*args):
            result = self.caller(*args)
            if args[2] == "check": result.update(status="ERROR", error="mock failure")
            return result
        self.assertFalse(self.runner(fail).execute("edit"))
        self.assertNotIn("gate-mobility", self.commands)

    def test_stale_success_cannot_complete(self):
        path = self.write("checks.txt", "PASS old")
        before = verify.signature(path); started = time.time_ns()
        runner = self.runner()
        with patch.object(verify.time, "sleep", return_value=None), self.assertRaises(TimeoutError):
            runner.wait_result("check", path, "text", before, started, time.monotonic()+.01)

    def test_unrestored_runtime_cannot_complete(self):
        started = time.time_ns()
        path = self.write("Runtime/checks.json", dict(Active=False, Finished=True, Restored=False, Status="PASS", Checks=["PASS"]))
        with patch.object(verify.time, "sleep", return_value=None), self.assertRaises(TimeoutError):
            self.runner().wait_result("runtime:start", path, "runtime", None, started, time.monotonic()+.01)
        self.assertEqual(self.commands, [])

    def test_wrong_traversal_command_cannot_complete(self):
        started = time.time_ns(); path = self.write("walk-first-visit.txt", "PASS unrelated")
        self.write("traversal-status.json", dict(Command="nav-diagnose", Active=False, Finished=True, Restored=True, Status="PASS"))
        with patch.object(verify.time, "sleep", return_value=None), self.assertRaises(TimeoutError):
            self.runner().wait_result("walk", path, "traversal", None, started, time.monotonic()+.01)

    def test_perf_waits_for_edit_before_next_probe(self):
        playing = [False]; pending = [False]
        def caller(*args):
            command = args[2]
            if command.startswith("perf:"):
                self.assertFalse(playing[0]); playing[0] = True; pending[0] = True
            reply = self.caller(*args)
            if command == "status" and playing[0]:
                reply["result"] = reply["result"].replace("play=False", "play=True")
            return reply
        def tick(_):
            if pending[0]: playing[0] = False; pending[0] = False
        with patch.object(verify.time, "sleep", side_effect=tick):
            self.assertTrue(self.runner(caller).execute("performance"))
        self.assertEqual([c for c in self.commands if c != "status"], [r[0] for r in verify.PHASES["performance"]])

    def test_timeout_stops_phase_without_next_command(self):
        runner = self.runner()
        with patch.object(runner, "wait_result", side_effect=TimeoutError("mock unfinished deadline")):
            self.assertFalse(runner.execute("edit"))
        self.assertEqual(self.commands, ["status", "check"])

    def test_freshness_requires_new_timestamp_and_signature(self):
        started = time.time_ns(); path = self.write("checks.txt", "PASS fresh")
        # Simulate a service receipt produced after request dispatch, without
        # depending on the host filesystem's timestamp clock precision.
        os.utime(path, ns=(started+1000000, started+1000000))
        self.assertTrue(verify.fresh(path, None, started))
        self.assertFalse(verify.fresh(path, verify.signature(path), started))
        os.utime(path, ns=(started-1000000, started-1000000))
        self.assertFalse(verify.fresh(path, None, started))


if __name__ == "__main__":
    unittest.main(verbosity=2)
