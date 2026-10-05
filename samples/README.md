# Sample hosts

Two runnable editors show the packages working together: a WPF host over AvalonEdit and an Avalonia host over
AvaloniaEdit. Both compile the same source from
[`shared/Samples`](https://github.com/Nickelony/LanguageServer/tree/main/shared/Samples), which selects the
engine with `#if AVALONIAEDIT`, so each head holds only its windows and its start-up code.

```powershell
dotnet run --project samples/Nickelony.Samples.Wpf
dotnet run --project samples/Nickelony.Samples.Avalonia -- C:\path\to\a\folder
```

The editor opens the folder that its first argument names, or the `Nickelony.Samples` folder of the temporary
directory without one. It writes its starter files - a Lua script with a module in `lib`, a JSON settings file, a
Markdown readme and a C# and a Visual Basic file - into the default folder, a new folder or an empty one, and
leaves a folder of your own as it is. A starter file that is already there keeps its text, so delete it to get the
current template. A sample is not a package: nothing packs it and no library references it.

## How the source is laid out

Each language feature has one folder, named for the `Nickelony.IDEKit.LanguageFeatures` namespace it shows.
The folder holds two kinds of file:

- A `Sample{Feature}` type wires the library to one editor, for example
  [`Hover/SampleHover.cs`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Hover/SampleHover.cs).
- An in-process provider answers the feature without a language server, for example
  [`Hover/KeywordHoverProvider.cs`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Hover/KeywordHoverProvider.cs).

[`SampleLanguageSession`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Language/SampleLanguageSession.cs)
creates one of each wiring type per editor, and
[`SampleWorkbench`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Workbench/SampleWorkbench.cs)
composes the tabs, the explorer, the commands and the lists of the window. Each head lays its controls out over
the workbench, builds its menus from the commands and holds the child windows a command asks for. Every menu item
shows its shortcut, and **View > Key bindings** changes it.

## Find a feature

Each folder below is under [`shared/Samples`](https://github.com/Nickelony/LanguageServer/tree/main/shared/Samples).

### Language features

| Feature | Try it in the window | Folder | Library entry point |
| --- | --- | --- | --- |
| Completion and snippets | Type a letter outside a comment or a string, type `.`, or choose **Edit > Complete word**; `fun` offers the `function` snippet, whose placeholders Tab steps through, and the WPF tooltip of a function renders its Markdown documentation | `Completion` | `CompletionControllerFactory`, `CompletionPresenter`, `CompletionControllerHooks.ScopeAt`, `CompletionPresenterHooks.BuildMarkdownContent`, `ICompletionTriggerCharacters`, `CompletionInsertTextFormat.Snippet` |
| Hover with Markdown, and the diagnostic under the pointer | Rest the pointer on `greet`, `print` or a `TODO`; `hoverDelay` in the settings sets the wait | `Hover` | `HoverControllerFactory`, `HoverPresenter`, `HoverPointerOptions`, `HoverDecision` |
| Highlighted code blocks in a popup | Rest the pointer on `greet` | `Presentation` | `MarkdownRenderer`, `CodeBlockHighlightingInstaller` |
| Signature help | Type `print(`, then move the caret out of the call or press Esc | `SignatureHelp` | `SignatureHelpControllerFactory`, `SignatureHelpPresenter`, `ISignatureHelpTriggerCharacters` |
| Code actions | Choose **Edit > Quick fix** on a line that ends in spaces, or click the light bulb | `CodeActions`, `Documents` | `CodeActionControllerFactory`, `CodeActionPresenter`, `WorkspaceEditApplier` |
| Diagnostics and the problems list | See the `prnt` call of the script, then choose **Navigate > Next problem** | `Diagnostics` | `DiagnosticsControllerFactory`, `DiagnosticsController` |
| Diagnostics of a whole project | Open `README.md` and type `TODO` or `FIXME` in it or in any other Markdown or text file of the folder; each open editor of such a file marks the marker as information, and deleting the marker clears it | `Diagnostics` | `ProjectDiagnosticsSource`, `ProjectDiagnosticsSourceHooks`, `DiagnosticRangeConversion.FromLineAndCharacter` |
| Semantic tokens | The names of called functions take their own color | `SemanticTokens` | `SemanticTokensColorizer`, `SemanticTokensSourceExtensions.Subscribe` |
| Go to definition, into another file too | Choose **Navigate > Go to definition**, or Ctrl+click a name | `Navigation` | `TextAreaNavigationExtensions.GoToDefinitionAsync`, `TextAreaNavigationExtensions.AttachPointerNavigation` |
| Find references | Choose **Navigate > Find all references**, then click a row of the **References** tab | `Navigation` | `IReferenceProvider`, `NavigationLocation`, `TextAreaExtensions.ApplyLocation` |
| Outline | Type in the filter box, or click a row | `DocumentSymbols` | `DocumentSymbolOutlineBuilder`, `DocumentSymbolFilter` |
| Rename across documents, open or not | Choose **Edit > Rename symbol** | `Rename`, `Documents` | `IRenameProvider`, `WorkspaceEditApplier` |
| Formatting through a provider | Choose **Edit > Format document** with the Lua server running | `Formatting` | `FormattingService` |
| JSON Schema completion and hover | Open the `settings.json` tab | `Json` | `JsonSchemaVocabularyIndex` |

A language server answers the same features through one provider; see
[`SampleLanguageFeatures.FromServer`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Language/SampleLanguageFeatures.cs).
A right-click in an editor offers the language commands of the menus.

### Documents and views

| Feature | Try it in the window | Source | Library entry point |
| --- | --- | --- | --- |
| Opening and saving through the view coordinator, with a question for unsaved changes on close | Edit a tab, then close it | [`Documents/SampleDocuments`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Documents/SampleDocuments.cs), [`Documents/SampleDocumentView`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Documents/SampleDocumentView.cs) | `WorkspaceViewCoordinator`, `WorkspaceDocumentTextViewBase`, `PublishPendingEditsAsync` |
| Two editors of one document, kept in step | Choose **View > Split editor**, then type in either one | [`Workbench/SampleWorkbench`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Workbench/SampleWorkbench.cs) | `WorkspaceViewCoordinator.OpenWithViewAsync` |
| One language-server copy of a document however many tabs show it | Start the Lua server, split a Lua tab, close the first tab, then type an error in the other; its diagnostic still updates | [`Workbench/SampleWorkbench`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Workbench/SampleWorkbench.cs), [`Documents/SampleDocuments`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Documents/SampleDocuments.cs) | `WorkspaceDocumentSynchronizer` |
| A Markdown preview as a read-only view of the document | Open `README.md` and choose **View > Open preview** | [`Documents/SampleMarkdownPreview`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Documents/SampleMarkdownPreview.cs) | `IWorkspaceDocumentView`, `MarkdownRenderer` |
| An explorer whose renames and deletes move the open tabs | Right-click the explorer, or press F2 or Delete on an entry | [`Workbench/SampleExplorer`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Workbench/SampleExplorer.cs) | `WorkspaceViewCoordinator.RenameAsync`, `RenameDirectoryAsync`, `DeleteAsync`, `DeleteDirectoryAsync` |
| A trash folder in place of the recycle bin | Delete a file, then look in the folder beside the sample folder whose name ends in `.Trash` | [`Documents/SampleTrashFileSystem`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Documents/SampleTrashFileSystem.cs) | `WorkspaceFileSystemDecorator` |
| Reloading a file that changed on disk | Change `sample.lua` in another editor | [`Documents/SampleFileReloader`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Documents/SampleFileReloader.cs) | `FileWatchSupervisor`, `FileReloadCoordinator`, `WorkspaceViewReloadHooksFactory` |

Split views and previews open in a second editor group beside the first, so an edit shows in both at once.

### Editing and the workbench

| Feature | Try it in the window | Source | Library entry point |
| --- | --- | --- | --- |
| Auto-closing, indentation, line comments, trimming | Type `(`, press Enter after `then`, or choose **Edit > Toggle line comment** | [`Editing/SampleEditingFeatures`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Editing/SampleEditingFeatures.cs) | `AutoClosingService`, `PolicyIndentationStrategy`, `LineCommentService` |
| The indentation a file shows, set from the status bar, and its conversion to spaces or tabs | Compare `sample.lua` (four spaces) with `settings.json` (two) in the status bar, rest the pointer on the indentation for its source, click it, or choose **Edit > Convert indentation to tabs** | [`Editing/SampleEditingFeatures`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Editing/SampleEditingFeatures.cs) | `TextEditorIndentationExtensions`, `IndentationDetector`, `IndentationPlanner` |
| The encoding and the line terminators of a file, in the status bar: reopen or save with another encoding, convert to LF or CRLF | Open a file that is not UTF-8, click the encoding in the status bar and choose **Reopen with encoding**, then **Save with encoding**; click the line terminators to convert them, and undo the conversion | [`Workbench/SampleWorkbench`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Workbench/SampleWorkbench.cs), [`Documents/SampleEncodings`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Documents/SampleEncodings.cs) | `WorkspaceDocumentReloadRequest.Encoding`, `WorkspaceViewCoordinator.ReplaceAsync`, `TextFileFormat`, `TextEditorLineTerminatorExtensions` |
| Bookmarks and change markers | Choose **Edit > Toggle bookmark**, or edit a line | [`Editing/SampleLineStatus`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Editing/SampleLineStatus.cs) | `BookmarkMargin`, `ChangeMarkerMargin` |
| Find and replace, and find in files, with the matches in a list | Choose **Search > Find and replace**, or **Search > Find in files** | [`Editing/SampleSearch`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Editing/SampleSearch.cs) | `FindReplaceOperations` |
| TextMate and regular-expression highlighting, recolored in place on a theme switch | Open each tab | [`Highlighting`](https://github.com/Nickelony/LanguageServer/tree/main/shared/Samples/Highlighting) | `TextMateHighlightingSession`, `RegexHighlightingDefinition` |
| Themes from an `ArgbColor` palette, which the window chrome follows | Choose **View > Toggle theme** | [`Themes/SampleThemes`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Themes/SampleThemes.cs) | `ThemeCatalog`, `TextMateTokenTheme`, `MarkdownRenderTheme` |
| Running an external command | Choose **Run > Run command** | [`Processes/SampleProcessRunner`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Processes/SampleProcessRunner.cs) | `ProcessRunner` |
| Key bindings: an editor tier and a window tier, overrides in a JSON file, display text, a recorded shortcut | Choose **View > Key bindings**, select a command and press a shortcut | [`Keys/SampleKeyBindings`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Keys/SampleKeyBindings.cs) | `KeyBindingService`, `KeyBindingDispatcherChain`, `ToKeyStroke`, `KeyRepeatGate` (Avalonia head) |
| A command palette: the command catalog as a quick pick with the word matching of the VS Code palette, titles, categories, shortcuts, the commands that do not apply dimmed, and the commands last run listed first | Press Ctrl+Shift+P or F1, or choose **View > Command palette**; type `fd` or `format doc`, move with the arrows, press Enter, or Esc to close; delete the `>` and type `?` to list the modes | [`QuickPick/SampleQuickPick`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/QuickPick/SampleQuickPick.cs), [`QuickPick/CommandQuickPickProvider`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/QuickPick/CommandQuickPickProvider.cs), [`QuickPick/JsonQuickPickRecentStore`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/QuickPick/JsonQuickPickRecentStore.cs) | `QuickPickController`, `QuickPickPresenter`, `IQuickPickProvider`, `IQuickPickRecentStore`, `CommandDescriptor.Title` |
| Quick open, the open editors and go to line: the files of the folder by a fuzzy path, with the open documents and the recently opened files first, the tabs, and a line with a preview | Press Ctrl+P and type `lib/ut`, or the start of a file name; type `edt ` for the tabs; press Ctrl+G and type `5` or `5:3`, then Esc to put the caret back; type `util.lua:6:10` to open a file at a position; create a file in another program while the list is open | [`QuickPick/SampleQuickPick`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/QuickPick/SampleQuickPick.cs) | `WorkspaceFileQuickPickProvider`, `OpenEditorsQuickPickProvider`, `GoToLineQuickPickProvider`, `TextEditorNavigationTarget` |
| Symbols in the quick pick: the symbols of the active file, grouped by kind, and the symbols of the workspace from the running language servers | Press Ctrl+Shift+O and type `greet`, move through the list and press Esc to put the caret back, then type `:` after the `@` to group by kind; choose **Language > Start the Lua language server** or **Start the Roslyn language server**, press Ctrl+T and type a name to search the whole folder (the first search starts a server that is not running and waits for it, so type the name again if the list stays empty); type `?` to list every mode | [`QuickPick/SampleQuickPick`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/QuickPick/SampleQuickPick.cs) | `DocumentSymbolQuickPickProvider`, `WorkspaceSymbolQuickPickProvider`, `IWorkspaceSymbolProvider` |
| Commands that a menu item and a shortcut both run | Every menu item | [`Workbench/SampleWorkbench`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Workbench/SampleWorkbench.cs) | None: the commands are host code |
| The rows of the outline, problems, references and search results lists | The lists of the window | [`Workbench/SamplePanels`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/Workbench/SamplePanels.cs) | `NavigationLocation` |
| The Lua and Roslyn language server providers | See the next section | [`LanguageServers`](https://github.com/Nickelony/LanguageServer/tree/main/shared/Samples/LanguageServers) | `LanguageServerFeatureProvider` |

A command of the window tier, such as **File > Save** or **View > Split editor**, runs wherever the focus is; a
command of the editor tier runs only while an editor has the focus.

## Language servers

The in-process providers answer the Lua files until a language server takes over, and the C# and Visual Basic
files have highlighting only until Roslyn starts. The status line names what answers each language:

- **Language > Start the Lua language server** picks a LuaLS `.zip` archive or a `lua-language-server`
  executable, and the Lua tabs switch to the server. **Language > Use the in-process Lua providers** switches back.
- **Language > Start the Roslyn language server** picks `Microsoft.CodeAnalysis.LanguageServer.exe`, and the
  picker starts at the one the C# extension of Visual Studio Code installs. That build carries C# only, so the
  Visual Basic file needs a build that carries Visual Basic.
- `NICKELONY_SAMPLE_LUA_SERVER` names a Lua archive or executable to start with. Without it, the sample reads
  `NICKELONY_LUA_LANGUAGE_SERVER_ARCHIVE`, the variable of the Lua integration tests, so one setting serves both.
- `NICKELONY_SAMPLE_ROSLYN_SERVER` names a Roslyn server to start with.

An archive extracts once into the `Nickelony.Samples.LuaLS` folder of the temporary directory, outside the
workspace the server indexes. A server starts on its first request, and one that fails to start reports it in the
status line. The build and the tests never need a server.

A server also talks to the window besides its answers, through the `LanguageServerFeatureProviderHooks` that
[`SampleLanguageServers`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/LanguageServers/SampleLanguageServers.cs)
passes to its provider:

- The status line shows a message of the server, and the progress of its work, such as Roslyn loading the projects.
- A quick fix that the server offers without an edit, such as the missing `using` of a Roslyn project, is resolved
  when you choose it. A quick fix that is a command, such as LuaLS's **Remove all trailing spaces**, runs on the
  server, and the edit it asks for goes to the document authority like a rename's edit. The sample refuses an edit
  that names a file outside its folder.
- The starter folder holds `Sample.csproj`, the project that lets Roslyn report compiler errors in `Sample.cs`.

## Tests

[`tests/Samples`](https://github.com/Nickelony/LanguageServer/tree/main/tests/Samples) opens each head's main window
and drives the features through it. Both suites compile one shared test tree, so a feature has one test for both
heads; the Avalonia suite runs headless, and the WPF suite needs a desktop session for the tests that show a window.
A project keeps only the tests of its own toolkit.
