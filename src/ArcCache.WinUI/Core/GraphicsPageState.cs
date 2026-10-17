namespace ArcCache.Core;

public enum GraphicsCheckState { NotRun, Running, Passed, Failed, NoIntel }
public enum GraphicsDriverState { Unknown, Checked, UncheckedTestable, Unsupported, Changed }
public enum GraphicsWindowsState { Unknown, Off, RestartRequired, On, Error }
public enum GraphicsCopyTestState { NotRun, Running, Passed, Failed, Stopped, Stale }
public enum GraphicsStep { Chip, Windows, DriverCopy, Ready, Blocked }
public enum GraphicsPrimaryAction { None, CheckAgain, EnableWindowsSetting, RestartAndCheck, TestDriverCopy, CheckSelectedChip, ChooseGame }

public sealed record GraphicsPageInputs(
    GraphicsCheckState Check,
    GraphicsDriverState Driver,
    GraphicsWindowsState Windows,
    GraphicsCopyTestState CopyTest,
    bool MultipleChips = false,
    string ChipName = "Intel graphics",
    string DriverVersion = "",
    string AvailableSizes = "Default, 1 GB",
    bool ChipSelectionComplete = false,
    int CopyTestProgress = 0,
    string CopyTestStage = "",
    bool ControlCopyPassed = false,
    int SelectedTrialMiB = 512);

public sealed record GraphicsPageState(
    GraphicsCheckState CheckState,
    GraphicsStep CurrentStep,
    GraphicsPrimaryAction PrimaryAction,
    bool Step1Complete,
    bool Step2Complete,
    bool Step3Complete,
    bool Step1Expanded,
    bool Step2Expanded,
    bool Step3Expanded,
    bool ShowCheckAgainLink,
    bool ShowAmberDriverNote,
    bool ShowFinish,
    bool IsBusy,
    bool ShowStopTest,
    bool ShowUpdateDriverLink,
    bool ShowChipPicker,
    int CopyTestProgress,
    string CopyTestStage,
    string Step1Result,
    string Step1Body,
    string Step2Result,
    string Step2Body,
    string Step3Result,
    string Step3Body,
    string Warning,
    string FinishText,
    string CacheSizes)
{
    public bool ReadyForGames { get; init; }

    public string HomeTitle => CheckState switch
    {
        GraphicsCheckState.NotRun or GraphicsCheckState.Running => "Checking your graphics…",
        GraphicsCheckState.Failed => "Graphics check needs attention",
        GraphicsCheckState.NoIntel => "No Intel graphics found",
        _ => "Good to go"
    };

    public string HomeHeader => ShowFinish ? "Good to go" : "Home";

    public string AccentButton => PrimaryAction switch
    {
        GraphicsPrimaryAction.CheckAgain when CurrentStep == GraphicsStep.Windows => "Check Windows setting again",
        GraphicsPrimaryAction.CheckAgain => "Try again",
        GraphicsPrimaryAction.EnableWindowsSetting => "Turn on game-specific settings",
        GraphicsPrimaryAction.RestartAndCheck => "I restarted Windows — check again",
        GraphicsPrimaryAction.TestDriverCopy when CheckState == GraphicsCheckState.Passed && Step3Result is "Test did not pass" or "Test stopped" => "Test again",
        GraphicsPrimaryAction.TestDriverCopy => "Test driver copy",
        GraphicsPrimaryAction.CheckSelectedChip => "Check this chip",
        GraphicsPrimaryAction.ChooseGame => "Choose a game",
        _ => ""
    };

    public static GraphicsPageState Resolve(GraphicsPageInputs input)
    {
        bool checking = input.Check is GraphicsCheckState.NotRun or GraphicsCheckState.Running;
        bool failed = input.Check == GraphicsCheckState.Failed || input.Driver == GraphicsDriverState.Changed && !checking;
        bool noIntel = input.Check == GraphicsCheckState.NoIntel;
        bool chipFound = input.Check == GraphicsCheckState.Passed;
        bool chooseChip = chipFound && input.MultipleChips && !input.ChipSelectionComplete;
        bool step1Complete = chipFound && input.Driver is not (GraphicsDriverState.Unknown or GraphicsDriverState.Changed) && !chooseChip;
        bool windowsReady = input.Windows == GraphicsWindowsState.On;
        bool step2Complete = step1Complete && windowsReady;
        bool exactDriver = input.Driver == GraphicsDriverState.Checked;
        bool testPassed = input.Driver == GraphicsDriverState.UncheckedTestable && input.CopyTest == GraphicsCopyTestState.Passed && input.ControlCopyPassed;
        bool step3Complete = step2Complete && (exactDriver || testPassed);
        bool showAmber = input.Driver == GraphicsDriverState.UncheckedTestable;

        string chip = string.IsNullOrWhiteSpace(input.DriverVersion) ? input.ChipName : $"{input.ChipName} · Driver {input.DriverVersion}";
        string step1 = input.Check switch
        {
            GraphicsCheckState.Running or GraphicsCheckState.NotRun => "Checking graphics chip…",
            GraphicsCheckState.NoIntel => "No Intel graphics found",
            GraphicsCheckState.Failed => "Graphics check could not finish",
            GraphicsCheckState.Passed when input.Driver == GraphicsDriverState.Changed => "Driver changed · checking again",
            GraphicsCheckState.Passed when chooseChip => "Multiple Intel graphics chips found",
            GraphicsCheckState.Passed when input.Driver == GraphicsDriverState.Unknown => "Intel graphics chip found",
            GraphicsCheckState.Passed => chip,
            _ => "Check your graphics chip"
        };
        string step1Body = input.Check switch
        {
            GraphicsCheckState.NoIntel => "Arc Cache needs an Intel Arc or Xe graphics chip to set up a driver copy.",
            GraphicsCheckState.Failed => "The graphics check could not finish. Check that the Intel driver installation is complete, then try again.",
            GraphicsCheckState.Passed when chooseChip => "Select the Intel graphics chip you want Arc Cache to check.",
            GraphicsCheckState.Passed when input.Driver == GraphicsDriverState.Unknown => "Arc Cache found your chip and is checking whether this driver can be used.",
            GraphicsCheckState.Passed when input.Driver == GraphicsDriverState.Changed => "The driver changed. Arc Cache is checking the new version now.",
            _ => ""
        };

        bool windowsKnown = input.Windows is GraphicsWindowsState.Off or GraphicsWindowsState.On or GraphicsWindowsState.RestartRequired;
        string step2 = !step1Complete ? "Waiting for step 1" : input.Windows switch
        {
            GraphicsWindowsState.On => "On",
            GraphicsWindowsState.Off => "Off",
            GraphicsWindowsState.RestartRequired => "Restart required",
            GraphicsWindowsState.Error => "Could not check setting",
            _ => "Waiting for step 1"
        };
        string step2Body = input.Windows switch
        {
            GraphicsWindowsState.Off when input.CopyTest == GraphicsCopyTestState.Passed => "Previous Windows setting restored. Turn this setting on to let a game use its driver copy.",
            GraphicsWindowsState.Off => "Turn this Windows setting on to let a game use its driver copy.",
            GraphicsWindowsState.RestartRequired => "Save your work, restart Windows, then return here to check again.",
            GraphicsWindowsState.Error => "Restart Windows, then return here and try again.",
            GraphicsWindowsState.Unknown when step1Complete => "Arc Cache is checking the Windows setting.",
            GraphicsWindowsState.On when step1Complete => "Windows lets each set-up game load the driver copy in its own folder.",
            _ => ""
        };

        string step3;
        string step3Body;
        bool showStop = false;
        bool updateDriverLink = false;
        if (!step1Complete)
        {
            step3 = "Waiting for step 1";
            step3Body = "";
        }
        else if (!step2Complete)
        {
            step3 = "Waiting for step 2";
            step3Body = "";
        }
        else if (exactDriver)
        {
            step3 = "This driver is on the checked list";
            step3Body = "";
        }
        else if (input.Driver == GraphicsDriverState.Unsupported)
        {
            step3 = "This driver file cannot be tested safely";
            step3Body = "Arc Cache does not have a safe test for this Intel driver yet. Update the Intel graphics driver, then check again.";
            updateDriverLink = true;
        }
        else if (input.CopyTest == GraphicsCopyTestState.Running)
        {
            step3 = "Testing driver copy…";
            step3Body = "Your installed driver and game files are unchanged.";
            showStop = true;
        }
        else if (input.CopyTest == GraphicsCopyTestState.Passed && !input.ControlCopyPassed)
        {
            step3 = "Default driver copy needs its test";
            step3Body = "Arc Cache checks the unchanged Default copy before it can use any tested cache size.";
        }
        else if (input.CopyTest == GraphicsCopyTestState.Passed)
        {
            step3 = "Test passed · " + input.AvailableSizes;
            step3Body = "";
        }
        else if (input.CopyTest == GraphicsCopyTestState.Failed)
        {
            step3 = "Test did not pass";
            step3Body = "Arc Cache could not verify the driver copy. Test again or use the default settings.";
        }
        else if (input.CopyTest == GraphicsCopyTestState.Stopped)
        {
            step3 = "Test stopped";
            step3Body = "No game files were changed. You can test again.";
        }
        else if (input.CopyTest == GraphicsCopyTestState.Stale)
        {
            step3 = "Driver changed · test again";
            step3Body = "A new driver was detected. Test this copy before using it for a game.";
        }
        else
        {
            step3 = "This driver version needs a test";
            step3Body = "The test checks a temporary driver copy; your installed driver is not replaced.";
        }

        GraphicsStep current;
        GraphicsPrimaryAction action;
        bool showAgain;
        string warning = "";
        string finish = "";

        if (checking || failed || noIntel || chooseChip || input.Driver is GraphicsDriverState.Unknown or GraphicsDriverState.Changed)
        {
            current = GraphicsStep.Chip;
            action = failed ? GraphicsPrimaryAction.CheckAgain : chooseChip ? GraphicsPrimaryAction.CheckSelectedChip : GraphicsPrimaryAction.None;
            showAgain = noIntel || chipFound && !failed && !chooseChip;
        }
        else if (input.Windows is GraphicsWindowsState.Error or GraphicsWindowsState.Unknown or GraphicsWindowsState.Off or GraphicsWindowsState.RestartRequired)
        {
            current = GraphicsStep.Windows;
            action = input.Windows switch
            {
                GraphicsWindowsState.Error => GraphicsPrimaryAction.CheckAgain,
                GraphicsWindowsState.Off => GraphicsPrimaryAction.EnableWindowsSetting,
                GraphicsWindowsState.RestartRequired => GraphicsPrimaryAction.RestartAndCheck,
                _ => GraphicsPrimaryAction.None
            };
            showAgain = input.Windows is GraphicsWindowsState.Off or GraphicsWindowsState.RestartRequired;
        }
        else if (step3Complete)
        {
            current = GraphicsStep.Ready;
            action = GraphicsPrimaryAction.ChooseGame;
            showAgain = true;
            finish = "Ready. Choose a game.";
            if (testPassed) warning = "Not pre-reviewed by Arc Cache · passed its test on this PC";
        }
        else if (input.Driver == GraphicsDriverState.Unsupported)
        {
            current = GraphicsStep.Blocked;
            action = GraphicsPrimaryAction.None;
            showAgain = true;
        }
        else
        {
            current = GraphicsStep.DriverCopy;
            action = input.CopyTest == GraphicsCopyTestState.Running ? GraphicsPrimaryAction.None : GraphicsPrimaryAction.TestDriverCopy;
            showAgain = true;
            if (showAmber) warning = "Not pre-reviewed by Arc Cache · test the driver copy first";
        }

        if (!step2Complete && current is GraphicsStep.DriverCopy or GraphicsStep.Ready or GraphicsStep.Blocked)
        {
            current = GraphicsStep.Windows;
            action = GraphicsPrimaryAction.None;
        }

        bool step3Expanded = current is GraphicsStep.DriverCopy or GraphicsStep.Blocked;
        return new GraphicsPageState(
            input.Check,
            current,
            action,
            step1Complete,
            step2Complete,
            step3Complete,
            current == GraphicsStep.Chip,
            current == GraphicsStep.Windows,
            step3Expanded,
            showAgain && action != GraphicsPrimaryAction.CheckAgain && action != GraphicsPrimaryAction.CheckSelectedChip,
            showAmber,
            current == GraphicsStep.Ready,
            checking || input.CopyTest == GraphicsCopyTestState.Running,
            showStop,
            updateDriverLink,
            chooseChip,
            Math.Clamp(input.CopyTestProgress, 0, 100),
            string.IsNullOrWhiteSpace(input.CopyTestStage) ? "Checking the driver copy" : input.CopyTestStage,
            step1,
            step1Body,
            step2,
            step2Body,
            step3,
            step3Body,
            warning,
            finish,
            input.AvailableSizes)
        {
            // Entering My games depends on the unchanged 512 MiB control, not
            // whichever larger size is currently selected for the next test.
            ReadyForGames = step2Complete && (exactDriver || input.ControlCopyPassed)
        };
    }

    // Each preview row is also the expected action/status contract asserted in the app tests.
    public static IReadOnlyList<(string Key, string Name, GraphicsPageInputs Input, GraphicsStep Step, GraphicsPrimaryAction Action, string AccentButton, bool Amber, bool Ready)> StateTable { get; } =
    [
        ("fresh-check", "fresh check", new(GraphicsCheckState.Running, GraphicsDriverState.Unknown, GraphicsWindowsState.Unknown, GraphicsCopyTestState.NotRun), GraphicsStep.Chip, GraphicsPrimaryAction.None, "", false, false),
        ("no-intel-chip", "no Intel chip", new(GraphicsCheckState.NoIntel, GraphicsDriverState.Unsupported, GraphicsWindowsState.Off, GraphicsCopyTestState.NotRun), GraphicsStep.Chip, GraphicsPrimaryAction.None, "", false, false),
        ("check-failed", "check failed", new(GraphicsCheckState.Failed, GraphicsDriverState.Unknown, GraphicsWindowsState.Unknown, GraphicsCopyTestState.NotRun), GraphicsStep.Chip, GraphicsPrimaryAction.CheckAgain, "Try again", false, false),
        ("driver-review-pending", "chip found, driver review pending", new(GraphicsCheckState.Passed, GraphicsDriverState.Unknown, GraphicsWindowsState.On, GraphicsCopyTestState.NotRun), GraphicsStep.Chip, GraphicsPrimaryAction.None, "", false, false),
        ("windows-setting-off", "Windows setting off", new(GraphicsCheckState.Passed, GraphicsDriverState.Checked, GraphicsWindowsState.Off, GraphicsCopyTestState.NotRun), GraphicsStep.Windows, GraphicsPrimaryAction.EnableWindowsSetting, "Turn on game-specific settings", false, false),
        ("restart-pending", "restart pending", new(GraphicsCheckState.Passed, GraphicsDriverState.Checked, GraphicsWindowsState.RestartRequired, GraphicsCopyTestState.NotRun), GraphicsStep.Windows, GraphicsPrimaryAction.RestartAndCheck, "I restarted Windows — check again", false, false),
        ("checked-driver-ready", "checked driver ready", new(GraphicsCheckState.Passed, GraphicsDriverState.Checked, GraphicsWindowsState.On, GraphicsCopyTestState.NotRun), GraphicsStep.Ready, GraphicsPrimaryAction.ChooseGame, "Choose a game", false, true),
        ("unchecked-driver-setting-off", "unchecked driver with setting off", new(GraphicsCheckState.Passed, GraphicsDriverState.UncheckedTestable, GraphicsWindowsState.Off, GraphicsCopyTestState.NotRun), GraphicsStep.Windows, GraphicsPrimaryAction.EnableWindowsSetting, "Turn on game-specific settings", true, false),
        ("unchecked-driver-ready-to-test", "unchecked driver ready to test", new(GraphicsCheckState.Passed, GraphicsDriverState.UncheckedTestable, GraphicsWindowsState.On, GraphicsCopyTestState.NotRun), GraphicsStep.DriverCopy, GraphicsPrimaryAction.TestDriverCopy, "Test driver copy", true, false),
        ("test-running", "test running", new(GraphicsCheckState.Passed, GraphicsDriverState.UncheckedTestable, GraphicsWindowsState.On, GraphicsCopyTestState.Running, CopyTestProgress: 42, CopyTestStage: "Checking the temporary driver copy"), GraphicsStep.DriverCopy, GraphicsPrimaryAction.None, "", true, false),
        ("ready-unchecked-driver", "B580 32.0.101.9150 unchecked driver copy test passed", new(GraphicsCheckState.Passed, GraphicsDriverState.UncheckedTestable, GraphicsWindowsState.On, GraphicsCopyTestState.Passed, ChipName: "Intel Arc B580", DriverVersion: "32.0.101.9150", AvailableSizes: "Default, 1 GB", ControlCopyPassed: true), GraphicsStep.Ready, GraphicsPrimaryAction.ChooseGame, "Choose a game", true, true),
        ("setting-restored", "setting restored after test", new(GraphicsCheckState.Passed, GraphicsDriverState.UncheckedTestable, GraphicsWindowsState.Off, GraphicsCopyTestState.Passed), GraphicsStep.Windows, GraphicsPrimaryAction.EnableWindowsSetting, "Turn on game-specific settings", true, false),
        ("test-failed", "test failed", new(GraphicsCheckState.Passed, GraphicsDriverState.UncheckedTestable, GraphicsWindowsState.On, GraphicsCopyTestState.Failed), GraphicsStep.DriverCopy, GraphicsPrimaryAction.TestDriverCopy, "Test again", true, false),
        ("test-stopped", "test stopped", new(GraphicsCheckState.Passed, GraphicsDriverState.UncheckedTestable, GraphicsWindowsState.On, GraphicsCopyTestState.Stopped), GraphicsStep.DriverCopy, GraphicsPrimaryAction.TestDriverCopy, "Test again", true, false),
        ("unsupported-file", "unsupported file", new(GraphicsCheckState.Passed, GraphicsDriverState.Unsupported, GraphicsWindowsState.On, GraphicsCopyTestState.NotRun), GraphicsStep.Blocked, GraphicsPrimaryAction.None, "", false, false),
        ("unsupported-file-setting-off", "unsupported file with setting off", new(GraphicsCheckState.Passed, GraphicsDriverState.Unsupported, GraphicsWindowsState.Off, GraphicsCopyTestState.NotRun), GraphicsStep.Windows, GraphicsPrimaryAction.EnableWindowsSetting, "Turn on game-specific settings", false, false),
        ("driver-changed-rechecking", "driver changed; automatic recheck", new(GraphicsCheckState.Running, GraphicsDriverState.Changed, GraphicsWindowsState.On, GraphicsCopyTestState.Stale), GraphicsStep.Chip, GraphicsPrimaryAction.None, "", false, false),
        ("multiple-chips", "multiple chips selected", new(GraphicsCheckState.Passed, GraphicsDriverState.Checked, GraphicsWindowsState.On, GraphicsCopyTestState.NotRun, MultipleChips: true), GraphicsStep.Chip, GraphicsPrimaryAction.CheckSelectedChip, "Check this chip", false, false),
        ("long-graphics-names", "long graphics names", new(GraphicsCheckState.Passed, GraphicsDriverState.Checked, GraphicsWindowsState.On, GraphicsCopyTestState.NotRun, ChipName: "Intel Arc Graphics with an unusually long adapter name", DriverVersion: "32.0.101.9150.12345"), GraphicsStep.Ready, GraphicsPrimaryAction.ChooseGame, "Choose a game", false, true),
        ("windows-state-unreadable", "Windows state unreadable", new(GraphicsCheckState.Passed, GraphicsDriverState.Checked, GraphicsWindowsState.Error, GraphicsCopyTestState.NotRun), GraphicsStep.Windows, GraphicsPrimaryAction.CheckAgain, "Check Windows setting again", false, false)
    ];

    public static IReadOnlyList<(string Key, GraphicsPageInputs Input, bool ReadyForGames)> ReadinessStateTable { get; } =
    [
        ("4gb-selected-control-passed", new(GraphicsCheckState.Passed, GraphicsDriverState.UncheckedTestable, GraphicsWindowsState.On, GraphicsCopyTestState.NotRun, ControlCopyPassed: true, SelectedTrialMiB: 4096), true),
        ("selected-size-passed-control-not-passed", new(GraphicsCheckState.Passed, GraphicsDriverState.UncheckedTestable, GraphicsWindowsState.On, GraphicsCopyTestState.Passed, ControlCopyPassed: false, SelectedTrialMiB: 4096), false),
        ("control-passed-windows-off", new(GraphicsCheckState.Passed, GraphicsDriverState.UncheckedTestable, GraphicsWindowsState.Off, GraphicsCopyTestState.NotRun, ControlCopyPassed: true, SelectedTrialMiB: 4096), false)
    ];
}
