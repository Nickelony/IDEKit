# Shared test support and environment-dependent tiers

This folder holds the helpers that every suite links (the polling helper, the temporary-directory
scope, the logger scope, the test-category names). It also documents the tiers of tests whose result
depends on the machine they run on, and the policy for each tier.

## Tier vocabulary

`TestCategories.cs` names the tiers a run can select with `dotnet test --filter TestCategory=<name>`.
The same strings are used everywhere; every suite links the shared file through `TestCategories.cs`.

| Tier | What it needs | In the default run |
|---|---|---|
| `Performance` | Nothing extra. The test asserts an allocation or wall-clock budget, so it is an informative smoke check rather than a deterministic assertion. | deselected by the `LanguageFeatures` suite; selected elsewhere |
| `Integration` | A real external dependency (a child process, a file watcher, a language-server binary, or a downloaded asset). | selected |
| `InteractiveWindow` | An interactive window station (a desktop session) to show real WPF content in a window. | selected; a headless run deselects it explicitly |

The default run selects every test unless a suite's `test.runsettings` deselects a tier. The `LanguageFeatures`
suite deselects `Performance` by default (its `test.runsettings` sets `TestCategory!=Performance`) because the
budgets vary with the machine and its load; the deterministic functional coverage of the same scenarios stays
selected, and the budgets are checked on demand with `dotnet test --filter TestCategory=Performance`. Every
other tier stays selected, so the release baseline stays honest; a fast or hermetic run can deselect a tier
with `--filter TestCategory!=<name>`.

Two tiers keep their own note next to their tests, because a decision applies to them alone:

- The LuaLS live tier is dormant by design: no archive is bundled, so the tests skip until
  `NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE` names one (`tests/LanguageServer/Nickelony.LanguageServer.Lua.Tests/Integration/README.md`).
- The interactive-window-station tier for hosted WPF tests is described below.

## Interactive window station (hosted WPF)

Some tests render real WPF content and must show it in a window: they go through
`WPFTestHost.ShowInHostWindow`, `ShowHostedEditor`, or `ShowEditorWithMargin` (linked from
`shared/TestHelpers`). Showing a window needs an interactive window station, which a desktop session
provides but a headless agent or a service account does not.

On a session without one, WPF throws `Win32Exception` while the window is shown or while the first
dispatcher work runs. That failure belongs to the environment, not to the test, so the shared helper
converts it into an **inconclusive** result carrying the underlying error instead of failing the test
with WPF's low-level message. The consequence is deliberate and stated here: **on such a session the
hosted tests drop their coverage** - the run still reports success - while on a session with a desktop
they run and assert normally.

The policy for the tier:

- The release gate runs the hosted tests from a session with an interactive window station (a desktop
  session); the release verification numbers are recorded from such a run.
- Styled painting, margin rendering, and wheel/scroll behavior are asserted only where a desktop
  session exists. A green headless run is not evidence for those paths.
- A headless run deselects the tier explicitly with `--filter TestCategory!=InteractiveWindow`. The hosted
  test classes carry `[TestCategory(TestCategories.InteractiveWindow)]`, so the tier is selectable and a
  headless leg reports those classes as **excluded** instead of silently inconclusive. The category is
  applied per class, so a class that mixes hosted and non-hosted tests loses both on a headless leg; the
  desktop release gate still runs every class, so nothing is lost where the hosted assertions run.
- The helper additionally keeps its `Assert.Inconclusive` conversion as an exact fallback: a hosted test
  that runs on a session without a window station (the default leg on a headless machine) reports the
  environment failure as inconclusive instead of a red test, while every other exception still propagates
  so a product regression cannot masquerade as a headless skip.

The KeyBindings WPF adapter suites (`Nickelony.KeyBindings.Wpf.Tests`) are outside this tier on purpose:
they construct `KeyEventArgs` over a stubbed `KeyboardDevice` and `PresentationSource` and never create an
HWND, so they need an STA thread (they carry `[STATestClass]`) but no interactive window station, and they
carry no `InteractiveWindow` category. They run in the default headless leg; the suite's non-visual
`KeyCodeMapperTests` keeps `[TestClass]` for the same reason.

## Log assertions

`TestLoggerScope` renders each entry as `Level|Message|ExceptionMessage` and exposes
`HasEntryAtLeast(LogLevel)` for tests that only need to observe that a diagnostic was emitted at a minimum
level. That is the preferred oracle for a test that does not care which diagnostic fired, so the render format
can change without breaking it.

A test that must distinguish several diagnostics, or assert that a diagnostic is *absent*, pins the message
wording on purpose (through `Logs`). The prefix checks (`log.StartsWith("Warn|", ...)`) are the accepted cost of
that precision: those assertions stay wording-based on purpose and are not reduced to a level-only check, which
would lose the "which diagnostic, with what context" guarantee. Every level-only assertion is already expressed
through `HasEntryAtLeast`, so no wording assertion remains that only needs a level.

## Windows-only coverage

Some `Nickelony.LanguageServer.Client.Tests` cases assert behaviour that only exists on Windows: the
kill-on-close job-object guard (`ChildProcessLifetimeTests`) and the lifecycle/transport races that wait on a
real child process or on process-exit signalling. Those cases carry
`[OSCondition(OperatingSystems.Windows)]`, so a run on Linux or macOS **excludes** them instead of failing:
the Windows kill guarantee and those disposal races have no cross-platform oracle, and the run reports them
as not-run rather than silently passing.

The policy for the tier:

- Windows-only coverage is asserted from a Windows session; the release verification numbers include it.
- The same OS condition gates the Windows-only `WorkspaceFileWatcherTests` cases in `Provider.Tests` (real
  `FileSystemWatcher` error raising) and the `WorkspaceSnapshotTrackerTests` cases that need a Windows path
  identity.
- A non-Windows run reports the cases as excluded, so the gap is visible in the test count instead of being
  inferred. `docs/LanguageServerInternals.md` records the platform split for the lifetime guard.
- Closing the gap needs a Linux/macOS crash guard (`BL-27`, `prctl(PR_SET_PDEATHSIG)`); until then the
  non-Windows guard is the documented no-op.

## Test class organization

- A test class that shows WPF content (through `WPFTestHost`, or a hosted margin/editor helper) is
  annotated `[STATestClass]` so it runs on an STA thread; a class that only exercises non-visual logic
  uses `[TestClass]`. For example, `BookmarkCoordinatorTests` uses `[TestClass]`, while the margin
  suites use `[STATestClass]`.
- A large suite is split across partial classes by scenario (`XxxTests.Scenario.cs`); the class
  attribute is declared once, on the primary file (for example `MarkdownRendererTests.cs`).
- `[DoNotParallelize]` marks a test that must not run alongside others. The IDEKit Core suite sets
  `[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]` in its `AssemblyInfo.cs`, so
  the attribute is meaningful there; in a suite that sets no assembly-level `[assembly: Parallelize]` it
  is preemptive and only takes effect if parallelization is enabled later.
