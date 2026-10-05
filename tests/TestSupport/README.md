# Shared test support and environment-dependent categories

This folder holds the helpers that suites of more than one family link, such as the polling helper and the
recording logger. A helper that one family uses lives in that family's own `TestSupport` folder, and a
test project links each helper into its virtual `TestSupport\` folder. The `Wpf/` and `Avalonia/` subfolders hold
the helpers that need the types of one toolkit, so the folder root stays free of any UI toolkit. This page also
documents the categories of tests whose result depends on the machine they run on, and the policy for each
category.

## Category vocabulary

`TestCategories.cs` names the categories a run can select with `dotnet test --filter TestCategory=<name>`.
A suite that links the file uses its constants; a suite that does not writes the same strings as literals.

| Category | What it needs | In the default run |
|---|---|---|
| `Performance` | Nothing extra. The test asserts an allocation or wall-clock budget, so it is an informative smoke check rather than a deterministic assertion. | deselected by each binding's `LanguageFeatures` suite; selected elsewhere |
| `Integration` | A real external dependency (a child process, a file watcher, or a developer-supplied language server). | selected; a fast profile deselects it |
| `InteractiveWindow` | An interactive window station (a desktop session) to show real WPF content in a window. | selected; a headless run deselects it explicitly |
| `Repository` | Nothing extra. The tests check the repository's own files rather than product behavior, so their result describes the checkout. | selected |
| `RepositoryReport` | Nothing extra. The tests print a measure of the repository and never fail, so a reviewer reads their output with detailed verbosity. | selected |

The default run selects every test unless a suite's project file deselects a category. Each binding's
`LanguageFeatures` suite deselects `Performance` by default, because the budgets vary with the machine and its
load. Its project file sets `VSTestTestCaseFilter` to `TestCategory!=Performance`. The deterministic functional
coverage of the same scenarios stays selected, and the budgets are checked on demand with
`dotnet test --filter TestCategory=Performance`.

Every other category stays selected, so the release baseline stays honest. A fast or hermetic run can deselect a
category with `--filter TestCategory!=<name>`.

`tests/Directory.Build.props` points every test project at `tests/test.runsettings`, which sets a session
timeout, so a wedged test fails the run instead of hanging it; the budget is generous so a loaded machine
cannot abort a healthy session. A suite with a longer legitimate worst case, such as the Client suite, raises
the timeout with `VSTestCLIRunSettings` in its project file. `tests/test.fast.runsettings` sets only the
category filter.

The `Integration` category has two profiles:

- The default `tests/test.runsettings` keeps the category selected. The release gate, the full run whose numbers
  verify a release, runs the real child process, file watcher, and language-server checks and reports their
  count in the run totals.
- The `tests/test.fast.runsettings` sets `TestCaseFilter` to `TestCategory!=Integration`, and the test command
  selects it with its settings switch. The real-dependency tests are deselected, so they never report as
  inconclusive.

The category is set per test, so the fast profile runs every other test in the suite.

Some categories keep their own note next to their tests, because a decision applies to them alone:

- The LuaLS live suite is dormant by design: no archive is bundled, so the tests are inconclusive until
  `NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE` names one (`tests/LanguageServer/Nickelony.LanguageServer.Lua.Tests/Integration/README.md`).
- The Roslyn live suite is dormant by design too: no server is bundled, so its tests are inconclusive until
  `NICKELONY_ROSLYN_LANGUAGE_SERVER` names an existing `Microsoft.CodeAnalysis.LanguageServer.exe`, such as the one
  the C# extension of Visual Studio Code installs under `.vscode/extensions/ms-dotnettools.csharp-*/.roslyn/`.
  Each test writes a scratch C# project into a temporary folder that targets the framework of the SDK that
  `dotnet --version` reports, so the suite is inconclusive without a `dotnet` SDK as well
  (`tests/LanguageServer/Nickelony.LanguageServer.Roslyn.Tests/Integration/`).
- The interactive-window-station category for hosted WPF tests is described below.

## Interactive window station (hosted WPF)

Some tests render real WPF content and must show it in a window: they go through
`WpfTestHost.ShowInHostWindow`, `ShowHostedEditor`, or `ShowEditorWithMargin`. `ShowInHostWindow` comes from
`WpfToolkitTestHost` in `tests/TestSupport/Wpf`, and the other two from the `WpfTestHost` of
`tests/IDEKit/TestSupport`, which derives from it. Showing a window needs an interactive window station, which a
desktop session provides but a headless agent or a service account does not.

On a session without one, WPF throws `Win32Exception` while the window is shown or while the first
dispatcher work runs. That failure belongs to the environment, not to the test, so the shared helper
converts it into an **inconclusive** result that carries the underlying error, instead of a failure with
WPF's low-level message.

The consequence is that **on such a session the hosted tests drop their coverage**, and the run still
reports success. On a session with a desktop, they run and assert normally.

A hosted window never holds the real foreground, so a test that needs it active calls
`TestWindowActivation.Activate`. From then on Windows cannot change that state: a foreground change in another
process, such as a parallel test run, neither deactivates the window nor closes a popup over it that closes on a
click outside it. The window and such a popup also ignore the buttons of the real mouse, because a test raises its
clicks as WPF events and a real click comes from a user at the desktop.

The policy for the category:

- The release gate runs the hosted tests from a session with an interactive window station (a desktop
  session); the release verification numbers are recorded from such a run.
- Styled painting, margin rendering, and wheel/scroll behavior are asserted only where a desktop
  session exists. A green headless run is not evidence for those paths.
- A headless run deselects the category with `--filter TestCategory!=InteractiveWindow`. The hosted classes carry
  `[TestCategory(TestCategories.InteractiveWindow)]`, so a headless leg reports them as **excluded** instead of
  silently inconclusive.
- The category is per class, so a class that mixes hosted and non-hosted tests loses both on a headless leg.
  The desktop release gate runs every class, so nothing is lost there.
- The helper keeps its `Assert.Inconclusive` conversion as an exact fallback. A hosted test on a session without a
  window station (the default leg on a headless machine) reports the environment failure as inconclusive instead of
  a red test.
- Every other exception still propagates, so a product regression cannot masquerade as a headless skip.

The KeyBindings WPF adapter suites (`Nickelony.KeyBindings.Wpf.Tests`) are outside this category on purpose. They
construct `KeyEventArgs` over a stubbed `KeyboardDevice` and `PresentationSource` and never create an HWND.
They need an STA thread (they carry `[STATestClass]`) but no interactive window station, so they carry no
`InteractiveWindow` category and run in the default headless leg. The non-visual `KeyCodeConversionTests`
keeps `[TestClass]` for the same reason.

The sample suites in `tests/Samples` open the main window of each sample head. The classes of the shared sample tree that
show a window carry the category in the WPF suite only, so they report as excluded on a headless run. The Avalonia suite
starts its own headless session, so it needs no desktop.

## Avalonia headless rendering

The Avalonia suites run on the headless session (`Avalonia.Headless`) of `AvaloniaToolkitTestHost` in
`tests/TestSupport/Avalonia`, which starts the `AvaloniaTestApp` that the compiling project supplies. The plain one
of that folder carries the Fluent theme. The AvaloniaEdit suites compile the one of `tests/IDEKit/TestSupport`
instead: its app builder (`AvaloniaTestApp.BuildAvaloniaApp`) enables the Skia renderer with
`UseHeadlessDrawing = false`, so the shared bitmap helpers (`TestBitmapRendering.Avalonia`) render an element into
a `RenderTargetBitmap` and scan real pixels.

The AvaloniaEdit render tests therefore assert like the WPF ones instead of reporting inconclusive. A render test
that still reports inconclusive does so for a recorded engine divergence, not for a missing renderer. The Skia
package is a test-only dependency and never flows into a shipped package.

## Log assertions

`RecordingLogger` captures each entry from any thread. `Entries` holds the entries as typed records (level,
event id, message and exception message), and `Logs` renders the same entries as
`Level|Message|ExceptionMessage`. It exposes `HasEntryAtLeast(LogLevel)` for tests that only need to observe
that a diagnostic was emitted at a minimum level. That is the preferred oracle for a test that does not care
which diagnostic fired, so the render format can change without breaking it.

A test that must distinguish several diagnostics, or assert that a diagnostic is *absent*, pins the message
wording on purpose (through `Logs`). The prefix checks (`log.StartsWith("Warn|", ...)`) are the accepted cost of
that precision: a level-only check would lose the "which diagnostic, with what context" guarantee.

## Windows-only coverage

Some `Nickelony.LanguageServer.Client.Tests` cases assert behavior that only exists on Windows: the
kill-on-close job-object guard (`PlatformChildProcessGuardTests`) and the lifecycle/connection races that wait on a
real child process or on process-exit signalling. Those cases carry
`[OSCondition(OperatingSystems.Windows)]`, so a run on Linux or macOS **excludes** them instead of failing:
the Windows kill guarantee and those disposal races have no cross-platform oracle, and the run reports them
as not-run rather than silently passing.

The policy for the category:

- Windows-only coverage is asserted from a Windows session; the release verification numbers include it.
- The same OS condition gates the Windows-only `FileWatcherTests` cases in `FileWatching.Tests` (real
  `FileSystemWatcher` error raising) and the `FileSnapshotTrackerTests` cases that need a Windows path
  identity.
- A non-Windows run reports the cases as excluded, so the gap is visible in the test count instead of being
  inferred. `docs/LanguageServerInternals.md` records the platform split for the lifetime guard.
- The `linux` job of `.github/workflows/build.yml` runs the Client suite on every push, so its test count shows
  the excluded cases.
- Closing the gap needs a Linux/macOS crash guard (`prctl(PR_SET_PDEATHSIG)`). It is open work in the
  non-Windows child-process lifetime guard entry of [BACKLOG.md](https://github.com/Nickelony/LanguageServer/blob/main/BACKLOG.md);
  until then the non-Windows guard is the documented no-op.

## Test class organization

- A test class that shows WPF content (through `WpfTestHost`, or a hosted margin/editor helper) is
  annotated `[STATestClass]` so it runs on an STA thread; a class that only exercises non-visual logic
  uses `[TestClass]`. For example, `BookmarkTrackerTests` uses `[TestClass]`, while the margin
  suites use `[STATestClass]`.
- One type's large suite is one partial class over `{Type}Tests.{Aspect}.cs`, and the class attribute is declared
  once, on the primary file (for example `MarkdownRendererTests.cs`). A suite that drives one facade through one
  feature is `{Facade}{Feature}Tests` in that feature's test folder, and a suite across several types ends in
  `ScenarioTests`.
- `[DoNotParallelize]` marks a test that must not run alongside others. The IDEKit Core suite sets
  `[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]` in its `AssemblyInfo.cs`, so
  the attribute is meaningful there; in a suite that sets no assembly-level `[assembly: Parallelize]` it
  has no effect until parallelization is enabled.

## Load-sensitive failures

A test that waits for an asynchronous effect polls its condition or awaits its completion signal, and a test that
drives several operations waits for the effect of each before it issues the next, so the load of a parallel
full-solution run changes how long a test takes and not what it asserts. A red run therefore means a regression, with
one exception: a `Performance` test asserts a wall-clock budget or a scaling ratio, and heavy load can break it. Rerun a
failing `Performance` test alone before treating the run as a regression.

A test that passes alone and fails under load holds a race. Remove the race; do not widen its wait.

## Soak tests

A soak test runs several tasks at once over a real directory for a bounded time, then checks the invariants of the
contracts: the store soak of `Workspace.Tests` and the coordinator soak of `Workspace.Views.Tests`. A soak carries no
category, so the default run and CI select it. It logs its seed, and each task derives its operations from the seed and
its index. Set `NICKELONY_SOAK_SEED` to the logged value to repeat the operations of a failed run; the interleaving of the
tasks still varies, so rerun it more than once.

## Where a test helper lives

A helper lives in the nearest `TestSupport/` folder that every project compiling it shares:

- `tests/TestSupport` holds the helpers that several families link; its `Wpf/` and `Avalonia/` subfolders hold the
  ones that need the types of one toolkit.
- `tests/{Family}/TestSupport` holds the helpers that several projects of one family link.
- A project's own `TestSupport/` holds the rest.

A family adds what names its own types to a toolkit helper by deriving from it. `WpfToolkitTestHost` and
`AvaloniaToolkitTestHost` are abstract holders of static members, and a family's `WpfTestHost` or `AvaloniaTestHost`
derives from one, so a call through the family type reaches the toolkit members and the family's own. A family
whose controls need more than the plain `AvaloniaTestApp` compiles its own in place of it, under the same name.

A test project never links a file from another project's own folder, and a `TestSupport` folder holds no test
class other than an assembly initializer. The [repository
conventions](https://github.com/Nickelony/LanguageServer/blob/main/CONVENTIONS.md#vocabulary-table) own the names a helper or
a test double takes: a double starts with `Fake`, `Recording`, `Scripted`, `Gated`, `Throwing` or `Stub`, a
subclass written for a test starts with `Test`, and a host or helper type ends with `TestHost`, `TestHelpers` or
`TestExtensions`.

## Fakes

A double that looks like another one keeps its own type only for a difference that a test depends on, and its summary
states that difference. The `ILanguageServerClient` doubles in `tests/LanguageServer/TestSupport` and the dispatcher doubles
follow this rule: `FakePumpedDispatcher` and `FakePumpedUiThread` share one queue-and-clock engine, so a test reaches the
same behavior through an `IUiDispatcher` or through a synchronization context and a `TimeProvider`.

The file-watching doubles are shared by the FileWatching, Workspace.QuickPick and Provider suites because all of them drive a
watch supervisor. The Workspace.QuickPick suite has no snapshot tracker to record, and it also takes the clock:

- `FakeTimeProvider` is a clock and timer source that fires only when a test calls `Advance`.
- `StubFileWatcher` answers `Start` with a configured result and never observes the file system.
- `RecordingFileWatcherFactory` records each watcher creation, with the dispatch delegate and the failure callback of the
  framework, and hands out queued watchers, or a passive stub when none is queued. A root can switch to a real
  `FileWatcher` or throw on creation. It is the one capture of workspace file watchers.
- `RecordingFileSnapshotTracker` counts the calls a supervisor makes to its snapshot tracker and answers scripted
  snapshots.

## Choosing a wait

- A negative assertion waits out `TestPolling.AbsenceWindow` when its flow exposes no quiescence observation.
- In dispatcher tests, `PumpUntil` waits for a condition that must become true. `PumpUntilIdle` is the
  quiescence observation for a posted or resumed continuation. It drains the queued work, so the assertion
  does not depend on a wall-clock window.
- A timer-driven negative assertion has no barrier, so it pumps for a bounded duration with `PumpFrames`, the
  last resort.
