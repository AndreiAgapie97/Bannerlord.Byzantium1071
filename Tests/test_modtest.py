"""Runner safety/reporting tests; no dotnet invocation or game installation needed."""
import argparse
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

import modtest


def trx(outcome="Passed", body=""):
    return ('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f'<Results><UnitTestResult testName="Example" outcome="{outcome}">{body}'
            '</UnitTestResult></Results></TestRun>')


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.folder = modtest.new_run_directory(modtest.ROOT / "TestResults/runner-tests")
        self.args = argparse.Namespace(scenario="all", days=731, seed=17, timeout=30)

    def execute(self, returncode=0, result=None):
        def fake_run(command, environment, stream, timeout):
            self.assertIn("-p:ModuleId=", command)
            self.assertEqual(environment["B1071_SCENARIO_DAYS"], "731")
            self.assertEqual(environment["B1071_SCENARIO_SEED"], "17")
            directory = Path(command[command.index("--results-directory") + 1])
            if result is not None:
                (directory / "results.trx").write_text(result, encoding="utf-8")
            return returncode
        with patch("modtest.run_process", side_effect=fake_run), redirect_stdout(io.StringIO()):
            return modtest.execute_layer("rules", self.args, self.folder / "rules", None)

    def test_success_requires_executed_passing_tests(self):
        self.assertEqual("PASS", self.execute(result=trx())["status"])

    def test_process_failure_cannot_be_hidden_by_passing_trx(self):
        self.assertEqual("FAIL", self.execute(returncode=1, result=trx())["status"])

    def test_test_failure_is_reported_even_if_process_exits_zero(self):
        result = self.execute(result=trx("Failed", "<Output><ErrorInfo><Message>Day 7: lost food</Message></ErrorInfo></Output>"))
        self.assertEqual("FAIL", result["status"])
        self.assertEqual("Day 7: lost food", result["tests"][0]["message"])

    def test_skipped_is_not_green(self):
        self.assertEqual("FAIL", self.execute(result=trx("NotExecuted"))["status"])

    def test_aborted_run_cannot_pass_with_only_passing_results(self):
        content = trx().replace('</TestRun>', '<ResultSummary outcome="Aborted"/></TestRun>')
        result = self.execute(result=content)
        self.assertEqual("ERROR", result["status"])
        self.assertIn("Aborted", result["error"])
        self.assertEqual(1, len(result["tests"]))

    def test_incomplete_result_set_cannot_pass(self):
        content = trx().replace('</TestRun>', '<ResultSummary outcome="Completed">'
            '<Counters total="2" executed="2" passed="2"/></ResultSummary></TestRun>')
        result = self.execute(result=content)
        self.assertEqual("ERROR", result["status"])
        self.assertIn("reports 2 tests but contains 1", result["error"])

    def test_run_level_error_cannot_hide_behind_passing_tests(self):
        content = trx().replace('</TestRun>', '<ResultSummary outcome="Completed">'
            '<RunInfos><RunInfo outcome="Error"><Text>Test host cleanup failed</Text></RunInfo></RunInfos>'
            '</ResultSummary></TestRun>')
        result = self.execute(result=content)
        self.assertEqual("ERROR", result["status"])
        self.assertIn("Test host cleanup failed", result["error"])
        self.assertEqual(1, len(result["tests"]))

    def test_run_level_warning_does_not_fail_passing_tests(self):
        content = trx().replace('</TestRun>', '<ResultSummary outcome="Completed">'
            '<RunInfos><RunInfo outcome="Warning"><Text>Diagnostic warning</Text></RunInfo></RunInfos>'
            '</ResultSummary></TestRun>')
        self.assertEqual("PASS", self.execute(result=content)["status"])

    def test_missing_report_is_error(self):
        self.assertEqual("ERROR", self.execute()["status"])

    def test_empty_selection_is_error(self):
        result = self.execute(result='<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"/>')
        self.assertEqual("ERROR", result["status"])
        self.assertIn("No tests executed", result["error"])

    def test_malformed_report_is_error(self):
        self.assertEqual("ERROR", self.execute(result="<broken")["status"])

    def test_partially_missing_selection_cannot_report_pass(self):
        self.args.scenario = "slave-transport"
        result = self.execute(result=trx().replace('testName="Example"',
            'testName="Byzantium1071.Tests.SlaveMathTests.Example"'))
        self.assertEqual("ERROR", result["status"])
        self.assertIn("HeadlessScenarioTests.SlaveUpkeep", result["error"])
        self.assertEqual(1, len(result["tests"]))

    def test_selector_does_not_match_another_class_with_same_prefix(self):
        with self.assertRaisesRegex(ValueError, "SlaveMathTests"):
            modtest.verify_selection("slave-transport", "rules", [
                {"full_name": "Byzantium1071.Tests.SlaveMathTestsExtra.Example"},
                {"full_name": "Byzantium1071.Tests.HeadlessScenarioTests.SlaveUpkeepExample"}])

    def test_custom_display_names_use_trx_method_identity(self):
        path = self.folder / "example.trx"
        content = trx().replace('testName="Example"', 'testId="id1" testName="Readable label"')
        content = content.replace('</TestRun>', '<TestDefinitions><UnitTest id="id1"><TestMethod '
            'className="Byzantium1071.GameTests.SlaveTransportTests" name="Example"/>'
            '</UnitTest></TestDefinitions></TestRun>')
        path.write_text(content)
        results = modtest.read_results(path)
        modtest.verify_selection("slave-transport", "game", results)
        self.assertEqual("Readable label", results[0]["name"])

    def test_missing_game_is_explicit_failure_and_still_writes_report(self):
        with patch("modtest.execute_layer") as execute, redirect_stdout(io.StringIO()):
            code = modtest.main(["run", "all", "--layer", "game", "--game-folder", str(self.folder / "absent"),
                                 "--output", str(self.folder)])
        self.assertEqual(1, code)
        execute.assert_not_called()
        report = json.loads(next(self.folder.glob("run-*/report.json")).read_text())
        self.assertEqual("FAIL", report["status"])
        self.assertIn("installed Bannerlord", report["runs"][0]["error"])

    def test_rules_only_never_checks_game_and_runs_have_unique_paths(self):
        with patch("modtest.require_game", side_effect=AssertionError("Must not access game")), \
                patch("modtest.execute_layer", return_value={"layer": "rules", "status": "PASS", "tests": []}), \
                redirect_stdout(io.StringIO()):
            for _ in range(2):
                self.assertEqual(0, modtest.main(["run", "all", "--layer", "rules", "--output", str(self.folder)]))
        self.assertEqual(2, len(list(self.folder.glob("run-*/report.json"))))

    def test_missing_dotnet_is_error(self):
        with patch("modtest.run_process", side_effect=FileNotFoundError("dotnet")), redirect_stdout(io.StringIO()):
            result = modtest.execute_layer("rules", self.args, self.folder / "rules", None)
        self.assertEqual("ERROR", result["status"])

    def test_timeout_is_reported(self):
        with patch("modtest.run_process", side_effect=subprocess.TimeoutExpired("dotnet", 30)), redirect_stdout(io.StringIO()):
            result = modtest.execute_layer("rules", self.args, self.folder / "rules", None)
        self.assertEqual("ERROR", result["status"])
        self.assertIn("timed out", result["error"])

    @unittest.skipUnless(modtest.os.name == "nt", "Windows process-tree cleanup")
    def test_timeout_terminates_only_the_spawned_process_tree(self):
        with patch("modtest.subprocess.Popen") as popen, patch("modtest.subprocess.run") as terminate:
            process = popen.return_value.__enter__.return_value
            process.pid = 12345
            process.wait.side_effect = [subprocess.TimeoutExpired("dotnet", 30), 1]
            process.poll.return_value = 1
            with self.assertRaises(subprocess.TimeoutExpired):
                modtest.run_process(["dotnet", "test"], {}, io.StringIO(), 30)
            self.assertEqual(["taskkill", "/PID", "12345", "/T", "/F"], terminate.call_args.args[0])
            self.assertNotIn("shell", popen.call_args.kwargs)

    @unittest.skipUnless(modtest.os.name == "nt", "Windows process-tree cleanup")
    def test_interrupt_cleans_up_the_spawned_process_tree(self):
        with patch("modtest.subprocess.Popen") as popen, patch("modtest.subprocess.run") as terminate:
            process = popen.return_value.__enter__.return_value
            process.pid = 12345
            process.wait.side_effect = [KeyboardInterrupt, 1]
            process.poll.return_value = 1
            with self.assertRaises(KeyboardInterrupt):
                modtest.run_process(["dotnet", "test"], {}, io.StringIO(), 30)
            self.assertEqual(["taskkill", "/PID", "12345", "/T", "/F"], terminate.call_args.args[0])

    def test_interrupt_writes_report_and_does_not_start_the_next_layer(self):
        with patch("modtest.run_process", side_effect=KeyboardInterrupt), \
                patch("modtest.require_game") as require, redirect_stdout(io.StringIO()):
            code = modtest.main(["run", "all", "--output", str(self.folder)])
        self.assertEqual(130, code)
        require.assert_not_called()
        report = json.loads(next(self.folder.glob("run-*/report.json")).read_text())
        self.assertEqual("FAIL", report["status"])
        self.assertEqual(["CANCELLED"], [run["status"] for run in report["runs"]])
        self.assertTrue(Path(report["runs"][0]["log"]).is_file())

    def test_interrupt_retains_completed_layer_results(self):
        def fake_run(command, environment, stream, timeout):
            if "Byzantium1071.GameTests.csproj" in command[2]:
                raise KeyboardInterrupt
            directory = Path(command[command.index("--results-directory") + 1])
            (directory / "results.trx").write_text(trx(), encoding="utf-8")
            return 0
        with patch("modtest.run_process", side_effect=fake_run), patch("modtest.require_game"), \
                redirect_stdout(io.StringIO()):
            code = modtest.main(["run", "all", "--output", str(self.folder)])
        self.assertEqual(130, code)
        report = json.loads(next(self.folder.glob("run-*/report.json")).read_text())
        self.assertEqual(["PASS", "CANCELLED"], [run["status"] for run in report["runs"]])
        self.assertEqual(1, len(report["runs"][0]["tests"]))

    def test_invalid_days_and_seed_are_rejected(self):
        for arguments in (["--days", "0"], ["--days", "10001"], ["--seed", "-1"], ["--seed", "2147483648"]):
            with self.subTest(arguments=arguments), self.assertRaises(SystemExit) as error, patch("sys.stderr", new=io.StringIO()):
                modtest.main(["run", "all", *arguments])
            self.assertEqual(2, error.exception.code)

    def test_every_catalog_entry_selects_real_source(self):
        for scenario, layers in modtest.SCENARIOS.items():
            for layer, selectors in layers.items():
                for selector in selectors:
                    parts = selector.split(".")
                    source = modtest.PROJECTS[layer].parent / (parts[0] + ".cs")
                    self.assertTrue(source.is_file(), f"Missing {scenario}/{layer}/{selector}")
                    if len(parts) > 1:
                        self.assertIn("void " + parts[1], source.read_text(encoding="utf-8-sig"))


if __name__ == "__main__":
    unittest.main()
