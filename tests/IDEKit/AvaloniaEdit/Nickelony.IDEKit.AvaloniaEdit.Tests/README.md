# Nickelony.IDEKit.AvaloniaEdit.Tests

The test suite for `Nickelony.IDEKit.AvaloniaEdit`. It covers only the binding behavior of the package, not the
neutral `Nickelony.IDEKit.Core` behavior. It pins the Avalonia UI thread through the shared `AvaloniaTestHost`: each
UI test class carries `[AvaloniaTestClass]`, whose attribute dispatches each test method (including `async Task`
bodies) onto the headless session's UI thread.

Where a WPF assertion has no Avalonia counterpart, the test re-pins the behavior that is expressible. For
example, it checks a `StyledProperty` default value plus a behavioral check instead of WPF
`FrameworkPropertyMetadata`. Pixel assertions render real frames through the Skia-backed headless renderer
(`AvaloniaTestApp.BuildAvaloniaApp` calls `UseSkia` and disables the headless drawing backend). See
`docs/EditorBindingGuide.md` section 4 for the intentional per-binding pieces and
`docs/EditorSharingGuide.md` section 4 for the files that stay forked.

A `Repository` guard test checks that the suite includes the shared `tests/IDEKit/Shared/Editor.Tests` tree
with one wildcard, so a dropped include fails the guard instead of silently removing coverage.

The tests in `AvaloniaTestHostTests.cs` are an infrastructure guard for the shared host
itself (hosted layout, a document-only body, and an async body's continuation all running on the UI
thread, plus a disposed host window closing).

## Inconclusive coverage

Tests that assert a WPF behavior with no Avalonia counterpart report `Assert.Inconclusive` with a recorded
reason. The policy for inconclusive results is in the [test-support README](https://github.com/Nickelony/LanguageServer/blob/main/tests/TestSupport/README.md).
