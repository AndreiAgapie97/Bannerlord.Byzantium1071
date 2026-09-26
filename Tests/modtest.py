"""Headless Campaign++ checks. Uses production rules and managed integration tests."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PROJECTS = {
    "rules": ROOT / "Tests/Byzantium1071.Tests/Byzantium1071.Tests.csproj",
    "game": ROOT / "Tests/Byzantium1071.GameTests/Byzantium1071.GameTests.csproj",
}
# Exact class/method prefixes, not a second implementation of the features.
SCENARIOS = {
    "castle-prisoners": {
        "rules": ["CastleConformityMathTests", "CastleSaveMathTests", "HeadlessScenarioTests.CastlePrisoners"],
        "game": ["CastleConformityTests", "CastlePrisonerWithdrawalTests", "CastleElitePaymentTests", "CastleRecruitmentActionTests"],
    },
    "slave-transport": {
        "rules": ["SlaveMathTests", "HeadlessScenarioTests.SlaveUpkeep"],
        "game": ["SlaveTransportTests"],
    },
    "manpower": {
        "rules": ["ManpowerMathTests", "ManpowerSaveMathTests", "ManpowerInvariantPropertyTests", "CastlePoolMathTests", "AiRecoveryMathTests", "SystemSimulationTests", "HeadlessScenarioTests.Manpower"],
        "game": ["AiRecoveryGameContractTests"],
    },
    "revenue": {
        "rules": ["RevenueMathTests", "EconomyMathTests", "InvestmentMathTests", "HeadlessScenarioTests.Revenue", "HeadlessScenarioTests.Investment"],
        "game": ["RevenueTuningRegressionTests", "RevenueTuningPatchBindingTests", "InvestmentBehaviorTests"],
    },
    "service": {
        "rules": ["ServiceMathTests", "SystemSimulationTests.ServiceLifecycle", "HeadlessScenarioTests.Service"],
        "game": ["DemobilizationServiceContractTests", "DemobilizationPersistenceTests"],
    },
    "diplomacy": {
        "rules": ["ExhaustionMathTests", "GovernanceMathTests", "SystemSimulationTests.LongWar", "SystemSimulationTests.Governance", "HeadlessScenarioTests.Governance"],
        "game": ["ClanSurvivalDecisionTests", "DevastationBehaviorTests", "GovernanceBehaviorTests"],
    },
    "ui": {
        "rules": ["TroopServiceLayoutTests", "LocalizationTests", "TranslationQualityTests", "DisplayMathTests"],
        "game": ["CastleRecruitmentUiContractTests", "CastleRecruitmentLifecycleTests", "FullScreenLedgerTests", "LedgerSortingTests", "LedgerUiContractTests", "NameplateUiContractTests", "SlaveConversionScreenContractTests"],
    },
    "compatibility": {
        "rules": ["CompatibilityMathTests", "SettingsContractTests", "ModuleDataTests"],
        "game": ["PatchSignatureTests", "SettingsMigrationTests", "GameTestEnvironmentTests"],
    },
}
LIMITS = ("Scripted rules and selected managed integration only; no running campaign, "
          "native rendering, full save/load, or other-mod compatibility proof. "
          "Days/seed configure HeadlessScenarioTests and the managed castle checkpoint scenarios; "
          "other tests retain their own inputs.")


def bounded_integer(low, high):
    def parse(value):
        number = int(value)
        if not low <= number <= high:
            raise argparse.ArgumentTypeError(f"must be between {low} and {high}")
        return number
    return parse


def new_run_directory(parent):
    parent.mkdir(parents=True, exist_ok=True)
    directory = parent.resolve() / ("run-" + uuid.uuid4().hex)
    directory.mkdir()
    return directory


def test_filter(scenario, layer):
    if scenario == "all":
        return None
    namespace = "Byzantium1071.Tests" if layer == "rules" else "Byzantium1071.GameTests"
    # Include the trailing dot for a class, so a similarly named class cannot match.
    return "|".join(f"FullyQualifiedName~{namespace}.{name}" + ("." if "." not in name else "")
                    for name in SCENARIOS[scenario][layer])


def game_folder(override):
    return Path(override) if override else Path(ET.parse(PROJECTS["game"]).findtext(".//GameFolder"))


def require_game(folder):
    for client in ("Win64_Shipping_Client", "Gaming.Desktop.x64_Shipping_Client"):
        binaries = folder / "bin" / client
        if (binaries / "Bannerlord.exe").is_file() and (binaries / "TaleWorlds.CampaignSystem.dll").is_file():
            return
    raise ValueError(f"Game-backed checks require an installed Bannerlord at {folder}. "
                     "Use --game-folder PATH or explicitly choose --layer rules.")


def read_results(path):
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    methods = {}
    for definition in root.findall(".//t:UnitTest", ns):
        method = definition.find("t:TestMethod", ns)
        if method is not None:
            methods[definition.get("id")] = f"{method.get('className')}.{method.get('name')}"
    results = []
    for row in root.findall(".//t:UnitTestResult", ns):
        results.append({
            "name": row.get("testName"), "outcome": row.get("outcome"),
            "full_name": methods.get(row.get("testId"), row.get("testName", "")),
            "duration": row.get("duration"),
            "message": row.findtext("t:Output/t:ErrorInfo/t:Message", default="", namespaces=ns),
            "output": row.findtext("t:Output/t:StdOut", default="", namespaces=ns),
        })
    if not results:
        raise ValueError("No tests executed; an empty selection is not a pass.")
    return results


def verify_run_summary(path, results):
    # Validate after retaining individual results, so an aborted run still reports completed checks.
    root = ET.parse(path).getroot()
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    summary = root.find("t:ResultSummary", ns)
    if summary is not None:
        outcome = summary.get("outcome")
        if outcome not in ("Completed", "Passed"):
            raise ValueError(f"Test run did not complete successfully: {outcome}.")
        errors = [info.findtext("t:Text", default="Run-level error", namespaces=ns)
                  for info in summary.findall("t:RunInfos/t:RunInfo", ns)
                  if info.get("outcome") in ("Error", "Failed", "Aborted")]
        if errors:
            raise ValueError("Test run reported errors: " + "; ".join(errors))
        counters = summary.find("t:Counters", ns)
        if counters is not None and counters.get("total") is not None:
            total = int(counters.get("total"))
            if total != len(results):
                raise ValueError(f"Test run reports {total} tests but contains {len(results)} results.")


def verify_selection(scenario, layer, results):
    if scenario == "all":
        return
    namespace = "Byzantium1071.Tests" if layer == "rules" else "Byzantium1071.GameTests"
    missing = []
    for selector in SCENARIOS[scenario][layer]:
        prefix = f"{namespace}.{selector}" + ("." if "." not in selector else "")
        if not any(test["full_name"].startswith(prefix) for test in results):
            missing.append(selector)
    if missing:
        raise ValueError("Requested checks did not execute: " + ", ".join(missing))


def run_process(command, environment, stream, timeout):
    options = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt" else {"start_new_session": True}
    with subprocess.Popen(command, cwd=ROOT, env=environment, stdout=stream,
                          stderr=subprocess.STDOUT, **options) as process:
        try:
            return process.wait(timeout=timeout)
        except (subprocess.TimeoutExpired, KeyboardInterrupt):
            # dotnet starts a separate test host; terminate our tree, not just its parent.
            try:
                if os.name == "nt":
                    subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                                   stdout=stream, stderr=subprocess.STDOUT, timeout=15, check=False)
                else:
                    os.killpg(process.pid, signal.SIGKILL)
            finally:
                if process.poll() is None:
                    process.kill()
                process.wait()
            raise


def execute_layer(layer, args, directory, folder):
    directory.mkdir()
    command = ["dotnet", "test", str(PROJECTS[layer]), "-c", "Debug", "-p:ModuleId=",
               "--verbosity", "minimal", "--results-directory", str(directory),
               "--logger", "trx;LogFileName=results.trx"]
    if layer == "game":
        command.append(f"-p:GameFolder={folder}")
    selection = test_filter(args.scenario, layer)
    if selection:
        command.extend(["--filter", selection])
    environment = dict(os.environ, B1071_SCENARIO_DAYS=str(args.days), B1071_SCENARIO_SEED=str(args.seed))
    record = {"layer": layer, "command": command, "status": "ERROR", "tests": []}
    print(f"Running {layer}: {args.scenario} ...", flush=True)
    log = directory / "console.log"
    try:
        # Each run has a fresh directory; failed builds cannot reuse a previous TRX.
        # File output avoids pipe deadlocks and retains diagnostics on timeouts.
        with log.open("w", encoding="utf-8") as stream:
            returncode = run_process(command, environment, stream, args.timeout)
        record["exit_code"] = returncode
        record["tests"] = read_results(directory / "results.trx")
        verify_run_summary(directory / "results.trx", record["tests"])
        verify_selection(args.scenario, layer, record["tests"])
        record["status"] = "PASS" if returncode == 0 and all(
            test["outcome"] == "Passed" for test in record["tests"]) else "FAIL"
    except KeyboardInterrupt:
        record["status"] = "CANCELLED"
        record["error"] = "Interrupted by user; test process cleanup attempted. See console log."
    except (OSError, ValueError, ET.ParseError, subprocess.TimeoutExpired) as error:
        record["error"] = str(error)
    record["log"] = str(log)
    print(f"{record['status']}: {layer}, {len(record['tests'])} checks; log: {log}", flush=True)
    return record


def write_report(directory, report):
    (directory / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    lines = ["# Campaign++ headless checks", "", f"Result: **{report['status']}**", "",
             f"Selection: {report['scenario']} | Days: {report['days']} | Seed: {report['seed']}",
             "", LIMITS, ""]
    for run in report["runs"]:
        passed = sum(test["outcome"] == "Passed" for test in run["tests"])
        lines.extend([f"## {run['layer']}: {run['status']}", "",
                      f"{passed} passed / {len(run['tests'])} reported checks.", "",
                      run.get("error", ""), ""])
        for test in run["tests"]:
            lines.append(f"- {test['outcome']}: {test['name']}")
            for field in ("message", "output"):
                if test[field]:
                    lines.extend("    " + line for line in test[field].splitlines())
        lines.append("")
    (directory / "report.md").write_text("\n".join(lines), encoding="utf-8")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("list", help="List named checks and their verification limits")
    run = commands.add_parser("run", help="Build and run checks without starting Bannerlord")
    run.add_argument("scenario", choices=["all", *SCENARIOS])
    run.add_argument("--layer", choices=["all", "rules", "game"], default="all")
    run.add_argument("--days", type=bounded_integer(1, 10000), default=365)
    run.add_argument("--seed", type=bounded_integer(0, 2147483647), default=42)
    run.add_argument("--timeout", type=bounded_integer(1, 86400), default=600, help="Seconds per test project")
    run.add_argument("--game-folder", help="Override the installed game path for game-backed checks")
    run.add_argument("--output", type=Path, default=ROOT / "TestResults/headless", help="Parent directory for unique run folders")
    args = parser.parse_args(argv)
    if args.command == "list":
        print("all: complete existing suites plus configurable scenarios")
        for name, layers in SCENARIOS.items():
            print(f"{name}: " + "; ".join(f"{layer}: {', '.join(tests)}" for layer, tests in layers.items()))
        print("\n" + LIMITS)
        return 0
    layers = list(PROJECTS) if args.layer == "all" else [args.layer]
    print(LIMITS, flush=True)
    directory = new_run_directory(args.output)
    report = {"scenario": args.scenario, "days": args.days, "seed": args.seed,
              "started_utc": datetime.now(timezone.utc).isoformat(), "limits": LIMITS, "runs": []}
    for layer in layers:
        try:
            folder = None
            if layer == "game":
                folder = game_folder(args.game_folder)
                if any(character in str(folder) for character in (';', ',', '"', '%')):
                    raise ValueError("Game path contains an unsupported MSBuild argument character.")
                require_game(folder)
            report["runs"].append(execute_layer(layer, args, directory / layer, folder))
            if report["runs"][-1]["status"] == "CANCELLED":
                break
        except (OSError, ValueError) as error:
            report["runs"].append({"layer": layer, "status": "ERROR", "error": str(error), "tests": []})
            print(f"ERROR: {error}", flush=True)
    report["status"] = "PASS" if all(run["status"] == "PASS" for run in report["runs"]) else "FAIL"
    write_report(directory, report)
    print(f"{report['status']}: {directory / 'report.md'}")
    if any(run["status"] == "CANCELLED" for run in report["runs"]):
        return 130
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
