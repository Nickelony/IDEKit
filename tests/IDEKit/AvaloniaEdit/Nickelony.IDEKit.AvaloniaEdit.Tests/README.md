# Nickelony.IDEKit.AvaloniaEdit.Tests

The test suite for `Nickelony.IDEKit.AvaloniaEdit`. It covers only the adapter behavior of the package
(never the neutral `Nickelony.IDEKit.Core` suite) and pins the Avalonia UI thread through the shared
`AvaloniaTestHost`: every test class carries `[AvaloniaTestClass]`, whose attribute dispatches each test
method (including `async Task` bodies) onto the headless session's UI thread.

The suite mirrors the reference `Nickelony.IDEKit.AvalonEdit.Tests` suite's shape and intent. Where a WPF
assertion has no Avalonia counterpart the test re-pins the behavior that is expressible (for example a
`StyledProperty` default value plus a behavioral check instead of WPF `FrameworkPropertyMetadata`), and a
pixel assertion reports the test inconclusive because the headless drawing backend produces no pixels. See
`docs/EditorBindingGuide.md` section 4 for the intentional per-binding pieces and section 5.4 for the files
that stay forked.

The 3 tests in `TestSupport/AvaloniaTestHostTests.cs` are an infrastructure guard for the shared host
itself (hosted layout, a document-only body, and an async body's continuation all running on the UI
thread).
