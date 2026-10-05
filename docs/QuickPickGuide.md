# Quick pick guide

A quick pick is one input over one list. The text the user types picks a mode, the mode supplies the items, and
the controller filters them, previews the active one and accepts it. The command palette of VS Code is the `>`
mode of a quick pick, and quick open is another mode of the same widget.

The quick pick is a family of its own, `Nickelony.QuickPick` with a window per toolkit, and it references no editor, so a host with no editor, such as a
3D view, takes a command palette with `Nickelony.KeyBindings` and nothing from IDEKit. This guide covers the mode
contract, the controller, the window of each toolkit, the recipe for a command palette, the modes for files, open
editors and lines, and the modes for symbols.

## Show a command palette

`QuickPickController` in `Nickelony.QuickPick` holds the session. A presenter in `Nickelony.QuickPick.Wpf` or
`Nickelony.QuickPick.Avalonia` shows the state of the controller in a window near the top of a host element. A host
builds both once, beside the window they belong to, and its shortcut handler only opens the session:

```csharp
using Nickelony.QuickPick;
using Nickelony.QuickPick.Wpf;   // or Nickelony.QuickPick.Avalonia

// When the window is created, on the UI thread. A command palette alone is one mode under the empty prefix.
var controller = new QuickPickController(
    [new QuickPickMode(commandProvider, prefix: string.Empty)],
    recentStore,
    options: QuickPickControllerOptions.Default with { EnableHelpMode = false });

var presenter = new QuickPickPresenter(windowContent, controller);

// In the handler of the shortcut:
controller.Open();

// When the window closes:
presenter.Dispose();
controller.Dispose();
```

The window opens when the session opens and closes when it ends. `commandProvider` is the provider that
[the recipe](#the-command-palette-recipe) builds, and `recentStore` is optional: without one, the controller
keeps the recently used commands for its own life. The thread that creates the controller owns it, and a member that
another thread calls throws, except `Dispose`, which hands its teardown to that thread, as
[Drive the controller](#drive-the-controller) describes.

The controller takes the `SynchronizationContext.Current`
of that thread as its one route back to it, for its timers, the answers of its providers and every `await` of its
own, so the family defines no dispatcher contract of its own. The UI thread of a WPF, Avalonia or Windows Forms
host has such a context. A thread without one, or with only the base `SynchronizationContext` type, which posts to
the thread pool, makes the constructor throw, so a console host installs a pump of its own first.

## Modes

A mode is an `IQuickPickProvider` that a host offers as a `QuickPickMode` under a prefix. A null prefix is the
`Prefix` of the provider, which is the default prefix and no fixed address. The controller picks the mode from the
text of the input:

| Text | Mode |
| --- | --- |
| Starts with the prefix of an offered mode | That mode, and the text after the prefix is its query. The longest prefix wins. |
| Starts with `?`, while the help mode is on | The list of modes, one row per offered mode. Accepting a row types its prefix. |
| Starts with no offered prefix | The mode with an empty prefix, or the list of modes when there is none. |

A prefix may end in a space, such as `edt `, and is compared ordinally. The controller reads the prefix of each
mode once, when it is created. A change of mode clears the rows at once, because they belong to the old mode, and
cancels the preview of the old mode. While the help mode is on, a text that starts with `?` is a mode of its own, so
a host that shows the command mode by default clears the `>` before it types `?`.

### Choose the modes

A host offers the modes it wants, each under the prefix it wants, and `QuickPickControllerOptions.EnableHelpMode`
decides whether `?` lists them:

| Setup | Modes | Help mode |
| --- | --- | --- |
| A command palette alone | Commands under the empty prefix | Off, so `>` and `?` filter as plain text |
| Files and commands | Files under the empty prefix and commands under `>` | On |
| The IDE setup | Every provider under its own prefix | On |

The constructor throws for two modes with one prefix, for one provider in two modes, and for a mode under `?` while
the help mode is on. It also throws when the help mode is off and no mode has the empty prefix, because a text that
starts with no prefix must pick a mode. Separate entry points, such as Ctrl+P for the files and Ctrl+Shift+P for the
commands, call `controller.Open(">")` on one controller or use two controllers, and `Open` takes no mode filter. The
recent items of a mode are kept under the prefix it was offered under.

The list of modes is a mode of the controller, and a host localizes its hint and its empty row with
`HelpModePlaceholder` and `HelpModeEmptyText` of the same options, which state their defaults on the members.

## Implement a provider

A provider names its mode and answers two calls. `GetItemsAsync` returns the candidates for a query, and
`AcceptAsync` acts on the item the user chose:

```csharp
internal sealed class NameQuickPickProvider(IReadOnlyList<string> names, Action<string> choose) : IQuickPickProvider
{
    public string Prefix => string.Empty;
    public string Name => "Names";
    public string Placeholder => "Type a name";

    public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QuickPickItem>>([.. names.Select(name => new QuickPickItem(name, name))]);

    public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
    {
        choose(request.Item.Key);
        return Task.CompletedTask;
    }
}
```

- **`GetItemsAsync` returns every candidate.** The controller filters them by the query, so a provider that
  filters by itself answers with `QuickPickFilterMode.None` and keeps its own order and highlights.
- **`GetItemsAsync` starts on the dispatcher thread and is reentrant.** The controller asks for a newer query
  before the task of an older one completes, and cancels the token of the older call.
  - The answer of a call that ignores its token is dropped.
  - A provider that cancels its own work, such as a timed-out request, throws with a token of its own, and the
    controller reports that as a failed query.
- **`AcceptAsync` is not reentrant.** A call starts after the task of the earlier call completed, so a slow
  action never runs twice at once.
- **`GetFilterText` splits the query.** The controller ranks the items on the text that `GetFilterText` returns
  for the query, which is the query itself by default. A mode whose query carries more than a filter, such as
  `file.cs:12`, returns the filter part only, and `AcceptAsync` still receives the whole query, the text after
  the mode's prefix, in `QuickPickAcceptRequest.Query`.
- **The members that describe the mode can throw.** The controller reads `Placeholder`, `FilterMode` and
  `RecentGroup` on the dispatcher thread, reports a failure and uses a fallback, so none escapes the controller.
- **An item identifies itself by its `Key`.** The key stays the same from query to query, so the controller keeps
  the active item through a refresh and records the accepted item as recently used. The `Tag` of an item is the
  provider's own payload, and the controller never reads it.
- **`TrailingText` is the right edge of the row.** A command mode puts the shortcut of the command there, and the files
  and open editors modes put the unsaved text there. The row draws it dimmed, and a screen reader announces it after
  the description.
- **An item that is not `IsEnabled` is listed dimmed.** It never becomes active and is never accepted.
- **A query that leaves no item shows one row of `EmptyText`.** The row holds the
  [`EmptyText`](https://github.com/Nickelony/LanguageServer/blob/main/src/QuickPick/Nickelony.QuickPick/IQuickPickProvider.cs)
  of the provider, whose default the member states, cannot become active, and accepts nothing. An empty query shows no
  such row, and neither does a query whose answer failed.
- **A group puts items under a separator.** Items with one `QuickPickGroup.Id` form a run. Runs are listed by
  `QuickPickGroup.Order`, lowest first, and runs of equal order by the position of their first item. A group
  without a `Label` draws no separator, and `ShowsCount` appends the number of items the run shows.
- **`AcceptAsync` runs after the session closed.** The action may open the next session, such as a dialog or
  another quick pick.
- **`Preview` and `CancelPreview` work as a pair.** The controller calls `Preview` when the active item changes.
  When the session ends without an accept, it calls `CancelPreview` once, and the provider restores what the
  preview changed. An accept never calls `CancelPreview`, so the accept keeps the preview.
- **`OnDeactivated` ends a stay in the mode.** The controller calls it once after the session ended, with or without
  an accept, and after the typed text switched to another mode. It runs after `CancelPreview` and before
  `AcceptAsync`, so a provider releases what it kept for the stay, such as a request in flight.
- **`QueryDebounceDelay` sets the rest before a query.** A mode that costs a request to a server returns a longer
  delay than the controller's, and `null`, the default, keeps the delay of the controller.
- **A host hook runs on the dispatcher thread when it may touch the UI.** A provider calls a hook before its first
  `await`, or after an `await` that resumes on the captured context (`.ConfigureAwait(true)`), and the doc of each hook
  parameter says the thread it runs on ([C18](https://github.com/Nickelony/LanguageServer/blob/main/CONVENTIONS.md)).

A provider that keeps recent items returns a `RecentGroup`. The controller then records each accepted key and
lists the recent items first under that group, in the order of use. A host that keeps the keys between runs
passes an `IQuickPickRecentStore`, whose two members load and save a document of keys per mode. The controller
owns the order and the capacity (`MaxRecentCount`), so a store only reads and writes, as the sample does in
[`JsonQuickPickRecentStore`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/QuickPick/JsonQuickPickRecentStore.cs).
A store that throws is reported to the fault callback of the controller and costs the recent items, nothing else.

## Filtering and ranking

The `FilterMode` of a provider decides how the controller filters the items and orders them. The enum can grow,
so a host that switches over it keeps a default branch.

| Mode | Filter | Order |
| --- | --- | --- |
| `Fuzzy` | Keeps the items that match as a subsequence. | By score. |
| `Path` | As `Fuzzy`, and a query without a separator prefers the file name. | By score. |
| `None` | Keeps every item. | The provider's. |
| `RankOnly` | Keeps every item, for a list that ranks without hiding. | The matches by score, then the rest in the provider's order. |
| `Words` | Splits the query into words and keeps the items whose label or description each word matches as a prefix, as word starts or as a substring, in any order of the words. | The provider's, with the recent items first. |

`Words` is the filter of the command palette of VS Code. `Fuzzy` and `Path` use the `FuzzyMatcher` of
`Nickelony.Matching`, and `Words` uses its `WordMatcher`; the [matching guide](MatchingGuide.md) owns what matches
and what a score rewards, and a host can score its own lists with the same matcher. An empty query keeps the order
of the provider, except that a provider with a recent group lists its recent items first.

- **Equal scores order as the scorer of VS Code orders them.** The recent item comes first, then the more compact
  match, the shorter label, a match in the label, the shorter distance, the shorter text, the label in
  alphabetical order, and last the order of the provider.
- **`MaxItemCount` bounds the list after the ranking.** A list of many items is ranked on a pool thread, and a
  newer query cancels the ranking between two candidates.

The highlight ranges of an item are `MatchRange` values of `Nickelony.Matching`, so a provider that filters by itself
sets them with the type its own matcher returns.

## Drive the controller

The controller presents nothing, so a host with its own window reads `State` when `Changed` is raised and calls
the members below for the input of the user. Every member in the table runs on the dispatcher thread, which is the
thread that created the controller.

| Member | Effect |
| --- | --- |
| `Open(text)` | Starts a session with the text, replacing an open one. The first query starts at once. |
| `SetText(text)` | Changes the text, as typing does. The query starts after `QueryDebounceDelay`. |
| `MoveActive(delta, wrap)` | Moves the active row over the enabled items, skipping separators. |
| `SetActiveRow(index)` | Makes an enabled row active, as a click does. |
| `AcceptAsync()` | Waits for the answer to the typed text, closes the session, then calls the provider. |
| `Dismiss()` | Ends the session without an accept and cancels the preview. |
| `Refresh()` | Runs the query again and keeps the item that is active when the answer arrives. |

- **A newer text never lets an older answer show.** The controller cancels the query in flight and drops its
  answer, and the list keeps the rows of the last answer until the new one arrives.
- **A session accepts at most once, and only its own accept.** The session is closed before the provider runs, a
  call that arrives while an earlier one waits for an answer returns `false`, and a call that waited across the
  end of its session accepts nothing, even in a session that opened since.
- **A slow query reports `IsBusy`.** The state turns busy after `BusyDelay`, so a fast provider shows no flicker.
- **Failures stay inside.** A provider, a store or a handler that throws is passed to the fault callback of the
  constructor, and the controller goes on.
- **`Dispose` may come from any thread.**
  - On the dispatcher thread, it ends the session and raises `Changed` with the closed state before it returns.
  - From another thread, it makes the controller inert at once, so every member returns without effect and the
    query in flight and the accept token are canceled.
  - It then posts the rest to the dispatcher thread: ending the session, the preview and deactivation hooks, and
    the closed state run there when the thread pumps its context.
  - Two disposals tear down once.
  - A dispatcher thread that is gone drops the posted part, and the fault callback hears nothing of it.

## The command palette recipe

The command mode maps the `CommandCatalog` of the key bindings to items. A host that takes `Nickelony.KeyBindings`,
`Nickelony.QuickPick` and one toolkit window needs no package of IDEKit for it. A descriptor carries the `Title` and the
`Category` of its command, and the service of the bindings supplies the shortcut text. A command without a title
shows its serialized id.

The item carries the command in `Tag`, so the accept runs it without a lookup, and a
host whose command ids are an enum needs no mapping. The provider needs the host's own predicate for the commands
that apply now and the host's own way to run a command, and the controller calls both on the dispatcher thread:

```csharp
public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default)
{
    var otherCommands = new QuickPickGroup("other", "other commands");

    IEnumerable<QuickPickItem> items = catalog.Descriptors.Select(descriptor =>
        new QuickPickItem(descriptor.SerializedId, descriptor.Title ?? descriptor.SerializedId)
        {
            Description = descriptor.Category,
            TrailingText = service.GetDisplayText(descriptor.Command),
            IsEnabled = canRunNow(descriptor.Command),
            Group = otherCommands,
            Tag = descriptor.Command,
        });

    return Task.FromResult<IReadOnlyList<QuickPickItem>>([.. items.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)]);
}

public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
{
    if (request.Item.Tag is TCommand command)
        run(command);

    return Task.CompletedTask;
}
```

The provider also declares `Prefix => ">"`, the default prefix of its mode, a `Name`, a `Placeholder`,
`FilterMode => QuickPickFilterMode.Words`, `EmptyText => "No matching commands"` and
`RecentGroup => new QuickPickGroup("recent", "recently used")`. It returns the commands in the order of their
titles, and `Words` keeps that order, so the recently used commands come first in the order of use, under any
query, and the other commands follow by title.

A command that opens the palette gets a default binding such as
Ctrl+Shift+P and F1, and its handler calls `controller.Open(">")`, or `controller.Open()` when the commands are the
only mode. The sample wires all of this in
[`CommandQuickPickProvider`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/QuickPick/CommandQuickPickProvider.cs)
and
[`SampleQuickPick`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/QuickPick/SampleQuickPick.cs).

## Quick open, open editors and go to line

The library ships providers for the navigation modes of VS Code, in the IDEKit packages that own what they list. The
files mode has no prefix, so it is the default mode of the quick pick, and `controller.Open()` opens it. The open
editors mode answers `edt `, and the go to line mode answers `:`. Offer them beside the command provider, and the
`?` mode lists all of them:

```csharp
using Nickelony.IDEKit.AvalonEdit.Navigation;   // or the AvaloniaEdit namespace
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;
using Nickelony.IDEKit.Workspace.QuickPick;
using Nickelony.QuickPick;

SynchronizationContext context = SynchronizationContext.Current!;   // on the UI thread

var files = new WorkspaceFileQuickPickProvider(
    [workspaceRoot],
    store.Options.PathComparison,
    (request, cancellationToken) => OpenInTabAsync(request.Path, request.Line, request.Column),
    ListTabs,
    new WorkspaceFileQuickPickOptions { ExcludeGlobs = ["bin", "obj", ".git"] },
    reportFault: exception => logger.LogWarning(exception, "The file list contained a failure."));

var controller = new QuickPickController(
    [
        new QuickPickMode(files),
        new QuickPickMode(commandProvider),
        new QuickPickMode(
            new OpenEditorsQuickPickProvider(
                ListTabs,
                tab => Activate(tab),
                new OpenEditorsQuickPickOptions { RootPaths = [workspaceRoot] })),
        new QuickPickMode(new GoToLineQuickPickProvider(() => activeEditor is null ? null : new TextEditorNavigationTarget(activeEditor))),
    ],
    recentStore);

files.FilesChanged += (_, _) => context.Post(_ => controller.Refresh(), null);

IReadOnlyList<OpenEditorDescriptor> ListTabs() =>
    [.. tabs.Select(tab => new OpenEditorDescriptor(tab.Title, tab.Path, tab.HasUnsavedChanges) { Tag = tab })];
```

The host opens the files mode with Ctrl+P and the go to line mode with Ctrl+G by calling `controller.Open()` and
`controller.Open(":")`. The samples wire all of this in
[`SampleQuickPick`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/QuickPick/SampleQuickPick.cs).

### Files

`WorkspaceFileQuickPickProvider` in the `Nickelony.IDEKit.Workspace.QuickPick` package lists the files below the
workspace roots and opens the one the user accepts through `openFileAsync`, which receives a
`WorkspaceFileOpenRequest`. The package references no document store. The `LocalPathComparisonPolicy` argument is
the one that the store of the host uses (`store.Options.PathComparison`), so the list and the documents agree on
which paths are one file and on the casing of a glob.

- **The row.** A file shows by its name, with its folder below its root, written with slashes, as the description.
  A workspace with several roots puts the name of the root in front of the folder.
- **The match.** The mode matches a query as a path, so `lib/ut` finds `lib/util.lua`, and a query without a
  separator prefers the file name.
- **The groups.** The `recently opened` group holds the files the user accepted before and the open documents
  that `listOpenEditors` returns. The `file results` group holds the other files.
- **The recent group comes first** under every query, even when a file of the other group scores higher.
- **An empty query** lists the recent files in the order of use, then the open documents in the order of the host,
  and then the other files by folder and name; a query ranks the files inside each group.
- **A file that was accepted before** drops out of the group once it is deleted or excluded.
- **A position after the name.** A query that ends in `:line` or `:line:column`, such as `file.cs:12:5`, ranks by
  the text before the colon, and accepting the file passes the typed numbers in the `WorkspaceFileOpenRequest`.
- **The host clamps the position.** The provider does not read the file, so the host clamps the position to the
  document it opens, as `IEditorNavigationTarget.MoveCaret` does.
- **A trailing colon is ignored**, and a text after a colon that is no number stays part of the name.
- **Open documents.** A document with unsaved changes shows `WorkspaceFileQuickPickOptions.UnsavedText` at the
  right edge and carries `OpenEditorDescriptor.UnsavedIconKey`, which `ResolveIcon` of the window maps to an image.
  A `null` text hides the label and keeps the icon key.
- **A document outside the roots** is listed by its own path, and an id that is no path, such as the synthetic id
  of an untitled document, reaches `openFileAsync` unchanged.
- **The first query.** It starts one file watcher per root, then walks the roots on a pool thread, so the first
  list can take a moment and shows the busy state meanwhile. The walk lists hidden files, skips the folders and
  files that `ExcludeGlobs` names, and skips symbolic links and junctions.
- **Kept current.** Afterwards a query never walks. A created, deleted or moved file or folder shows up when the
  watcher reports it, which takes the debounce delay of the [watcher](FileWatchingGuide.md#the-watcher). A report
  that arrives during a walk waits for the walk.
- **The file system has the last word.** The list asks the file system what is at each reported path, so a late or
  reordered report cannot leave a stale file behind, and a rename that changes only the casing of a name takes the
  spelling on disk where paths compare without regard to case.
- **Watcher failures.** A watcher that fails is replaced and the roots are walked again once.
- **A root whose watcher cannot start** keeps its last list, and a later query tries again after a pause, so a
  failing root costs one attempt per pause and reports one fault for the same failure.
- **A root that does not exist** loses its files.
- **The bound.** `WorkspaceFileQuickPickOptions.MaxFileCount` bounds the list across all roots. Each root takes
  what the roots before it left, so an earlier root can fill the list and leave none for a later one. A file
  created while the list is full is ignored.
- **The refresh.** `FilesChanged` is raised on a pool thread. A host marshals to its dispatcher and calls
  `Refresh`, which keeps the active file when the new list holds it.
- **The globs.**
  - A glob without a slash, such as `bin` or `*.tmp`, matches the name of a file or a folder at any depth.
  - A glob with a slash, such as `docs/build`, matches the path below the root from its start, segment by segment,
    and its `**` segment spans folders, as in `src/**/obj` and `**/bin/**`. The remarks of
    `WorkspaceFileQuickPickOptions.ExcludeGlobs` give the grammar.
  - A folder that matches is skipped with everything below it.

Dispose the provider with the quick pick, which stops its watchers. A query that is still running then ends as
canceled. `reportFault` receives each failure the provider contained, such as a watcher that cannot start.

### Open editors

`OpenEditorsQuickPickProvider` in `Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation` lists the tabs that `listOpenEditors` returns,
in that order, so a host that orders its tabs by recent use gets the order of VS Code. A row shows the title, the
folder of the document and the unsaved text, and a query matches the title and the folder as a path.

The folder is shown below the workspace root that holds the document, with the name of the root in front when there
are several roots, as the files mode shows it: `OpenEditorsQuickPickOptions.RootPaths` names the roots, and a
document outside them shows the folder of its own path. `UnsavedText` and the icon key work as in the files mode.

Accepting a row calls `activate` with the descriptor of the tab, whose `Tag` the host set to find the tab again.
Two tabs of one document, such as the views of a split, share a `DocumentId` and get distinct keys.

### Go to line

`GoToLineQuickPickProvider` in `Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation` reads the text after `:` as `line` or
`line:column`, the way `LineColumnQuery.TryParse` does. The numbers are one-based, the whitespace around them is
ignored, and a column after the colon is optional. A line past the end of the document clamps to the last line, a
column past the end of the line clamps to the position after its last character, and a number too large for an
integer counts as the end.

- **The preview.** The mode lists one row, such as `Go to line 12, column 5`, that is the clamped position, and it
  moves the caret and scrolls the caret into view, vertically and horizontally, while the user types.
- **Esc, a change of mode and text that is no position** put the caret, the selection and the scroll position
  back. Enter keeps the caret where it is.
- **The hint.** Text that is no position lists one dimmed row with the line and column the caret had before the
  preview and the range of lines. A host without an open editor returns `null` from `getTarget`, and the row asks
  for a file.
- **The target.** The mode moves the caret through `IEditorNavigationTarget` of `Nickelony.IDEKit.Core.Navigation`:
  the line count, the length of a line, the caret, `MoveCaret`, `SelectRange` for a mode that previews a range, and
  the pair of `SaveView` and `RestoreView` around the moves.
- **The implementation.** `TextEditorNavigationTarget` in the `Navigation` namespace of each base binding implements
  it for a `TextEditor`. A move does not take the keyboard focus from the input, and a restore puts back both
  scroll offsets.
- **The lifetime.** Create a target for the editor that is active when the mode asks, and the mode keeps the target
  it previewed in until the preview ends.

## Symbols

`DocumentSymbolQuickPickProvider` and `WorkspaceSymbolQuickPickProvider` in
`Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols` and `...WorkspaceSymbols` list symbols, because they drive `IDocumentSymbolProvider` and
`IWorkspaceSymbolProvider`. The mode `@` lists the symbols of the active document, `@:` lists them grouped by kind, and
`#` searches the symbols of the workspace. Register them beside the other modes, and the `?` mode lists them too:

```csharp
using Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.Controllers.WorkspaceSymbols;

SymbolDocumentDescriptor? DescribeActiveDocument() =>
    activeTab?.Outline is { } outline
        ? new SymbolDocumentDescriptor(outline, activeTab.Path, activeTab.Editor.Text, new TextEditorNavigationTarget(activeTab.Editor))
        : null;

var controller = new QuickPickController(
    [
        // The modes of the previous sections, then:
        new QuickPickMode(new DocumentSymbolQuickPickProvider(DescribeActiveDocument, reportFault: LogFailure)),
        new QuickPickMode(new DocumentSymbolQuickPickProvider(DescribeActiveDocument, DocumentSymbolQuickPickOptions.Default with { GroupByKind = true }, LogFailure)),
        new QuickPickMode(
            new WorkspaceSymbolQuickPickProvider(
                () => runningServers,
                (symbol, cancellationToken) => OpenAtAsync(symbol.Location.DocumentId, symbol.Location.Range?.Start),
                new WorkspaceSymbolQuickPickOptions { RootPaths = [workspaceRoot] },
                LogFailure)),
    ],
    recentStore);
```

The host opens `@` with Ctrl+Shift+O and `#` with Ctrl+T by calling `controller.Open("@")` and `controller.Open("#")`.
The samples wire all of this in
[`SampleQuickPick`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/QuickPick/SampleQuickPick.cs).
Every hook above runs on the dispatcher thread, so it may touch the UI. The only exception is the `reportFault`
callback of the files mode, whose doc names the threads it runs on.

### Symbols of the file

- **The rows.** The mode flattens the outline of `SymbolDocumentDescriptor.Provider` in document order. A row shows the
  name of the symbol, the name of the symbol that contains it as the description, and the kind as the icon key. A
  symbol without a range is listed dimmed, because nothing can reveal it.
- **The match.** The mode filters and ranks the rows itself, as VS Code does.
  - The first word of the query must match the name and the words after it must match the container, so `one alp`
    finds the member `One` of `Alpha`, and `alp` finds `Alpha` and none of its members.
  - A name that holds the whole query needs no container.
  - The best score comes first, and equal scores keep document order.
- **The kinds.** `ToIconKey` of `DocumentSymbolKindExtensions` spells the icon key, which is the codicon name that VS
  Code uses for the kind, such as `symbol-method`, and a host maps the keys it draws through `ResolveIcon`.
- **The separators.** The mode `@` lists the symbols under one separator that counts them, such as `symbols (12)`. The
  mode `@:` lists one separator per kind, such as `methods (3)`, ordered alphabetically by the text of the separator,
  as VS Code orders them.
- **The texts.** `DocumentSymbolQuickPickOptions.GroupLabel` and `SymbolsLabel` set the texts, so a host
  localizes them or hides a separator with `null`.
- **The preview.** The active symbol is selected in the editor by its name, and scrolled into view while the user moves
  through the list. The preview selects the name and not the whole symbol, because the end of a long symbol would
  scroll its start out of view.
- **Esc, and a change of mode,** put the caret, the selection and the scroll position back, and Enter keeps the
  selection.
- **The outline.** The outline is requested once for the same provider, document identifier and text, and every query of
  a stay in the mode waits for that one request, so typing sends no further one. A query that a newer one supersedes
  stops waiting and leaves the request running.
- **The request ends with the stay**: the mode cancels and releases it when the session ends or the typed text
  switches to another mode (`IQuickPickProvider.OnDeactivated`).
- **An answer without a symbol or with a failure is not kept**, because the provider cannot tell a document without
  symbols from a failed request, so the next query asks again, and the failure reaches `reportFault` once on the
  dispatcher thread.
- **The descriptor.** `getDocument` runs on the dispatcher thread each time the mode lists. It returns `null` for a
  document without an outline, and the mode then lists one dimmed hint. The descriptor carries the provider of that
  document, its identifier and text, and the `IEditorNavigationTarget` of its editor.

### Symbols of the workspace

- **The query.** The first word of the typed text goes to every provider that `getProviders` returns, and the controller
  ranks the symbols that come back by that word, without hiding one, because the server filters.
- **The words after the first one** must match the container or the path of the document, as in `util core`.
- **A blank query** sends nothing and lists nothing, because a server may answer it with every symbol of its
  workspace, as LuaLS does.
- **The mode waits after typing** before it asks, as VS Code does, because each query costs a request per server; the
  [`QueryDebounceDelay`](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/Nickelony.IDEKit.LanguageFeatures.Controllers/WorkspaceSymbols/WorkspaceSymbolQuickPickOptions.cs)
  option states the delay.
- **Several servers.** The mode asks all providers at once and lists their symbols in the order of `getProviders`, each
  provider's symbols in the order it gave them.
- **A symbol that two providers list alike**, or that one server lists twice, as LuaLS does for a global function, is
  listed once: the same document, kind, container, name and start. The first provider keeps it.
- **`getProviders` runs on the dispatcher thread** each time the mode lists, so a host whose language servers start
  and stop answers with the providers that exist now.
- **A stopped server.** A provider of the language-server framework starts its stopped server for the search and
  answers once the server has initialized, bounded by the request timeout of the provider, so the first search after a
  start can list nothing and the next keystroke asks again.
- **The failures.** A provider that throws costs its own symbols. After all providers answered, the failures of the
  listing reach `reportFault` one after another, in the order of the providers, on the dispatcher thread.
- **The cancellation.** Every request carries the token of the query, so a newer query cancels the requests of the
  older one.
- **The rows.** A row shows the name of the symbol, and as the description the name of the symbol that contains it and
  the path of its document below its workspace root, joined by `ContainerSeparator`, and the kind as the icon key.
- **`WorkspaceSymbolQuickPickOptions.RootPaths` names the roots**, and a document outside them shows its own path.
- **The accept.** Accepting a row passes the symbol to `openAsync` on the dispatcher thread. A symbol without a range
  goes through the `IWorkspaceSymbolResolver` of the provider that listed it first, when that provider implements the
  contract.
- **A symbol that has a range** is never resolved, and a resolution that fails opens the symbol as it was.

### Texts

Every text that a library mode shows is an option of its `...QuickPickOptions` type with its default stated on the
member, so a host localizes it. Each of those types carries the `Name` and `Placeholder` of its mode, every one
but `GoToLineQuickPickOptions` its `EmptyText`, because the go to line mode always lists a row, and beside them the hints, the separators and the joiners that the type documents, as
[`OpenEditorsQuickPickOptions`](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/Nickelony.IDEKit.LanguageFeatures.Controllers/Navigation/OpenEditorsQuickPickOptions.cs)
shows. The list of modes takes its hint and its empty row from `QuickPickControllerOptions.HelpModePlaceholder` and
`HelpModeEmptyText`. A text that holds numbers is a composite format string. A `null` separator text hides the
separator.

## The window

`QuickPickPresenter` of `Nickelony.QuickPick.Wpf` or `Nickelony.QuickPick.Avalonia` is a text box over a
virtualized list in a popup, placed against a host element, which is a `UIElement` in WPF and a `Control` in
Avalonia. The window needs no editor, so a host places it against any element of its window. The presenter takes a
`QuickPickTheme` and optional `QuickPickPresenterHooks`:

- **The keys.** Up and Down move the active row and wrap, Page Up and Page Down move it by
  `QuickPickTheme.MaxVisibleRows` rows without wrapping, Enter accepts, and Esc ends the session.
- **The rows.** The matched characters of the label and of the description are bold. The description, the detail,
  the trailing text, the separators and the empty row are drawn at reduced opacity, and a disabled item is dimmed as a
  whole.
- **The theme.** `QuickPickTheme` sets the brushes of the frame, the text and the active row, the border, the
  corner radius, the width, the top offset and the row height.
- **The item template.** `QuickPickTheme.ItemTemplate` replaces the row template (a `DataTemplate` in WPF, an
  `IDataTemplate` in Avalonia) and receives the `QuickPickRow` as its data context. Assigning `Presenter.Theme`
  restyles an open window.
- **The hooks.** `ResolveIcon` maps the `IconKey` of an item to an image for the default row.
- **The look.** The default theme takes the colors of the toolkit: the system brushes in WPF and a fixed light
  palette in Avalonia, which a host replaces through the brushes of the theme.
- **The placement.** The frame hangs from the top of the host at `TopOffset` and is centered on it, and a host
  narrower than the window gets a window that fits.
- **Screen readers.** The name of the input is the placeholder of the active mode, and the name of a row is its
  label, description and trailing text, separated by commas.
- **The end of a session.** The session also ends when the window loses the keyboard focus, when the window of
  the host deactivates, and when the toolkit closes the popup. The focus then returns to the element that held
  it before the window opened.
- **A WPF host that no `Window` owns**, such as an `ElementHost`, reports no deactivation, and the loss of the
  keyboard focus ends its session.

Disposing the presenter closes its window; it does not end the session of the controller.
