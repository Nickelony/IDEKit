# Nickelony.IDEKit.AvalonEdit

A WPF editor binding in the Nickelony IDEKit family: it binds the dependency-free
`Nickelony.IDEKit.Core` contracts to an AvalonEdit `TextEditor`. The binding provides programmatic edits, line
operations, configurable auto-closing, navigation, line comments, bookmarks, change markers, offset clamping,
diagnostic underlines, and the navigation target that lets a quick-pick mode move the caret of the editor. It
targets `net8.0-windows` (WPF) and depends on AvalonEdit and `Nickelony.IDEKit.Core`.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.AvalonEdit --prerelease
```

A minimal editor setup: compose the auto-closing service, attach a change-marker margin, and restore
bookmarks from the sidecar for the document.

```csharp
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.AutoClosing;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using Nickelony.IDEKit.AvalonEdit.LineStatus;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Bookmarks;

string filePath = @"C:\scripts\main.lua";
string originalText = File.ReadAllText(filePath);
var editor = new TextEditor { Text = originalText };

var autoClosing = new AutoClosingService();
var options = AutoClosingOptions.Default;

editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, options);
editor.PreviewKeyDown += (_, e) => autoClosing.TryDeletePair(editor, e, options);

var tracker = new UnsavedChangesTracker(() => editor.Document);
tracker.SetBaseline(originalText);                       // after load or save
editor.TextArea.LeftMargins.Add(new ChangeMarkerMargin(tracker));

var bookmarks = new BookmarkTracker(() => editor.Document);
// ".bookmarks" is a sample sidecar extension; the host supplies the real one.
var store = new BookmarkSidecarStore(".bookmarks");
bookmarks.RestoreFrom(store, filePath);                // on load, for the new document
```

## Remarks

- The binding depends only on `Nickelony.IDEKit.Core` (plus AvalonEdit), so it takes no logging dependency
  and never pulls in the workspace document authority.
- The language-feature controllers ship separately as `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`.
- The [editor usage guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorUsageGuide.md)
  owns thread affinity, lifecycle, the edit-target contract and its no-document exception, bookmarks,
  change markers, diagnostics, indentation, highlighting, engine-provided features, and the host-owned
  defaults.

## License

MIT © 2026 Kewin Kupilas.
