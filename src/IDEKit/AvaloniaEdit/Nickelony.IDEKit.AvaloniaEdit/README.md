# Nickelony.IDEKit.AvaloniaEdit

A cross-platform editor binding in the Nickelony IDEKit family: it binds the dependency-free
`Nickelony.IDEKit.Core` contracts to an AvaloniaEdit `TextEditor`. The binding provides programmatic edits, line
operations, configurable auto-closing, navigation, line comments, bookmarks, change markers, offset clamping,
diagnostic underlines, and the navigation target that lets a quick-pick mode move the caret of the editor. It
targets `net8.0` and depends on `Avalonia.AvaloniaEdit` and `Nickelony.IDEKit.Core`.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.AvaloniaEdit --prerelease
```

A minimal editor setup: compose the auto-closing service, attach a change-marker margin, and restore
bookmarks from the sidecar for the document.

```csharp
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.AutoClosing;
using Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
using Nickelony.IDEKit.AvaloniaEdit.LineStatus;
using Nickelony.IDEKit.Core.AutoClosing;
using Nickelony.IDEKit.Core.Bookmarks;

string filePath = "/home/user/scripts/main.lua";
string originalText = File.ReadAllText(filePath);
var editor = new TextEditor { Text = originalText };

var autoClosing = new AutoClosingService();
var options = AutoClosingOptions.Default;

editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, options);
// The tunneling route runs before the text area marks Backspace handled.
editor.AddHandler(InputElement.KeyDownEvent, (_, e) => autoClosing.TryDeletePair(editor, e, options), RoutingStrategies.Tunnel);

var tracker = new UnsavedChangesTracker(() => editor.Document);
tracker.SetBaseline(originalText);                       // after load or save
editor.TextArea.LeftMargins.Add(new ChangeMarkerMargin(tracker));

var bookmarks = new BookmarkTracker(() => editor.Document);
// ".bookmarks" is a sample sidecar extension; the host supplies the real one.
var store = new BookmarkSidecarStore(".bookmarks");
bookmarks.RestoreFrom(store, filePath);                // on load, for the new document
```

## Remarks

- The binding depends only on `Nickelony.IDEKit.Core` (plus AvaloniaEdit), so it takes no logging dependency
  and never pulls in the workspace document authority.
- The language-feature controllers ship separately as `Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures`.
- The [editor usage guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorUsageGuide.md)
  owns thread affinity, lifecycle, the edit-target contract and its no-document exception, bookmarks,
  change markers, diagnostics, indentation, highlighting, engine-provided features, and the host-owned
  defaults.

## License

MIT © 2026 Kewin Kupilas.
