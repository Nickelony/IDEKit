# Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests

The test suite for `Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures`. It covers only the binding and controller
behavior of the package, not the neutral `Nickelony.IDEKit.Core` or `Nickelony.IDEKit.LanguageFeatures`
behavior. It pins the Avalonia UI thread through the shared `AvaloniaTestHost`: each UI test class carries
`[AvaloniaTestClass]` (the shared bodies write `[STATestClass]`, which the project file aliases to it), whose
attribute dispatches each test method (including `async Task` bodies) onto the headless session's UI thread.

The suite covers the completion, code-action, hover, signature-help, definition-navigation,
semantic-token, and diagnostics-glue surfaces. Where an assertion has no Avalonia counterpart, the test
re-pins the expressible behavior.
Common gaps are the completion window, which is a `Popup` whose chrome assertions read
`window.CompletionList`, the lack of an ambient mouse position, and the lack of an AvaloniaEdit
completion-tooltip member. See `docs/EditorBindingGuide.md` section 4 for the intentional per-binding pieces
and `docs/EditorSharingGuide.md` section 4 for the files that stay forked.

A `Repository` guard test checks that the suite includes the shared
`tests/IDEKit/Shared/Editor.LanguageFeatures.Tests` tree with one wildcard, so a dropped include fails the
guard instead of silently removing coverage.

## Completion tooltip

The Avalonia binding has no completion tooltip pipeline, because AvaloniaEdit publishes no completion tooltip member. The tooltip
theme, hooks and capability query exist in the WPF binding only (docs/EditorBindingGuide.md section 4), so this suite has no
tooltip tests. When an AvaloniaEdit upgrade exposes a tooltip member, the pipeline is added to the binding with its tests.
