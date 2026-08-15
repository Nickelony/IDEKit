# Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests

The test suite for `Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures`. It covers only the adapter and
controller behavior of the package (never the neutral `Nickelony.IDEKit.Core` / `Nickelony.IDEKit.IntelliSense`
suites) and pins the Avalonia UI thread through the shared `AvaloniaTestHost`: every test class carries
`[AvaloniaTestClass]`, whose attribute dispatches each test method (including `async Task` bodies) onto the
headless session's UI thread.

The suite mirrors the reference `Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests` suite's shape and
intent across the completion, code-action, hover, signature-help, definition-navigation, semantic-token,
and diagnostics-glue surfaces. Where a WPF assertion has no Avalonia counterpart the test re-pins the
behavior that is expressible (the completion window is a `Popup`, so its chrome assertions read
`window.CompletionList`; there is no ambient mouse position; AvaloniaEdit exposes no completion-tooltip
member) and leaves the test inconclusive with a recorded reason when nothing equivalent exists. See
`docs/EditorBindingGuide.md` section 4 for the intentional per-binding pieces and section 5.4 for the files
that stay forked.
