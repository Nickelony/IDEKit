# Nickelony.IDEKit.AvalonEdit.Tests

The test suite for `Nickelony.IDEKit.AvalonEdit`. It covers only the binding behavior of the package, not the
neutral `Nickelony.IDEKit.Core` behavior. It runs its UI tests on an STA thread: each UI test class carries
`[STATestClass]`, and the classes that render or show real content also carry
`[TestCategory(TestCategories.InteractiveWindow)]`.

See `docs/EditorBindingGuide.md` section 4 for the intentional per-binding pieces, and
`docs/EditorSharingGuide.md` section 4 for the files that stay forked.

A `Repository` guard test checks that the suite includes the shared `tests/IDEKit/Shared/Editor.Tests` tree
with one wildcard, so a dropped include fails the guard instead of silently removing coverage.

## Inconclusive coverage

On a headless session, the tests that show WPF content in a window report inconclusive instead of failing;
the policy is in the
[test-support README](https://github.com/Nickelony/LanguageServer/blob/main/tests/TestSupport/README.md).
