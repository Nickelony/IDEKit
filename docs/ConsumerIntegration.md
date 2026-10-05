# Consumer integration guide

This guide is the single host-facing source of the lifecycle, threading, and disposal contract a host
follows when consuming a provider package; the package READMEs and the XML documentation summarize it and
link here.

The contract is the same for every provider in the family, so the `Nickelony.LanguageServer.Lua` sample
below stands in for `Nickelony.LanguageServer.Roslyn` (or any future provider): construct the provider, subscribe to callbacks before opening documents, drive the
document lifecycle, and dispose it. Install the Lua package with `dotnet add package Nickelony.LanguageServer.Lua --prerelease`.

Implementing a language provider rather than consuming one starts with the
[provider authoring guide](ProviderAuthoring.md). [`SampleLanguageServers`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/LanguageServers/SampleLanguageServers.cs)
creates the Lua and Roslyn profiles and a provider over each for a running editor.

## Construction and disposal

Construct the provider during host setup, subscribe to callbacks before opening documents, and dispose it
during host shutdown. The provider owns its underlying client and watcher. Disposal is idempotent and
closes callback admission before releasing those resources; synchronous `Dispose` blocks on the one shared
teardown while `DisposeAsync` awaits it, so neither form can tear the provider down twice or skip it.

To drive a host-managed client instead of launching a server, use the provider's `Create` factory (for
example `LanguageServerFeatureProvider.Create(LuaLanguageServerProfileFactory.Create(), workspaceRootDirectoryPaths, client)`);
it takes a supplied `ILanguageServerClient` and transfers ownership of it to the provider. A host that needs
only a process setting, such as an environment variable, an extra argument or the working directory, keeps the
provider-created client and overrides the profile's `Launch` instead:
`profile with { Launch = profile.Launch with { EnvironmentVariables = variables } }`.

```csharp
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Lua;
using Nickelony.LanguageServer.Provider;

// The host supplies workspaceRoot, luaLanguageServerPath, logger, filePath, the document texts, caretOffset and
// token, and the handlers OnDiagnosticsUpdated and OnSemanticTokensUpdated.
var provider = new LanguageServerFeatureProvider(
    LuaLanguageServerProfileFactory.Create(),
    workspaceRootDirectoryPaths: [workspaceRoot],
    serverExecutablePath: luaLanguageServerPath,
    logger: logger);

try
{
    provider.DiagnosticsUpdated += OnDiagnosticsUpdated;
    provider.SemanticTokensUpdated += OnSemanticTokensUpdated;

    provider.OpenDocument(filePath, initialText);

    IReadOnlyList<CompletionItem> items =
        await provider.GetCompletionItemsAsync(
            new CompletionRequest(filePath, initialText, caretOffset),
            cancellationToken: token);

    provider.UpdateDocument(filePath, currentText);
    provider.CloseDocument(filePath);
}
finally
{
    provider.Dispose();
}
```

Subscribe and unsubscribe symmetrically when a host owns a longer-lived provider. After
disposal begins, no new provider callback is admitted; a callback already in progress may
finish.

## Events and UI-thread marshaling

Diagnostics, semantic-token, capability, startup-failure, and workspace-file-watcher callbacks
may arrive on background threads. The provider raises each event synchronously on the thread that
reports the change, which can hold the provider's internal startup lock, so a handler must return
promptly and must not synchronously wait on a provider operation.

A handler can also run on the caller's thread, inside the call. The first request or document call starts the
server process before it returns, and a start that fails raises `CapabilitiesChanged` and `StartupFailed` there.

The handlers of one invocation run serially, and a failing handler does not prevent later subscribers
from running. Marshal to the editor UI dispatcher before touching controls, and treat each payload as
an owned immutable snapshot.

Each payload event follows the .NET event pattern (the provider is the sender) and carries one `EventArgs`
(for example `eventArgs.DocumentId`, `eventArgs.Diagnostics`, or `eventArgs.Failure`).

```csharp
provider.DiagnosticsUpdated += (_, eventArgs) =>
{
    editorDispatcher.Post(() => errorList.Replace(eventArgs.DocumentId, eventArgs.Diagnostics));
};
```

An editor that shows one document creates its controller with the binding's `DiagnosticsControllerFactory.Create`
instead, which underlines that document's diagnostics, takes only its updates on the editor's dispatcher, and shows
the current diagnostics first (see the [language-features guide](EditorLanguageFeaturesGuide.md#diagnostics)).

`CapabilitiesChanged` means that the host must reread `IsAvailable` and `Capabilities`; the event carries no
payload.

The
[`Capabilities`](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Provider.Abstractions/ILanguageServerCapabilitiesSource.cs)
snapshot reports the negotiated state of the current session, and every flag is `false` before the first
successful lazy start. A read returns the whole snapshot, so a host that combines several flags never reads half
of an update.

Do not make a flag check the only path to a request: issue the request when its documented fallback is
acceptable, or trigger startup through a lifecycle member such as `OpenDocument`, then reread `Capabilities`
after `CapabilitiesChanged`.

### Server messages

A server also sends messages that answer no request: a message to show, progress of its work, a question with
actions, and an edit that a command of its own produced. The host receives them through the optional
`LanguageServerFeatureProviderHooks` that it passes to the provider constructor:

```csharp
var hooks = new LanguageServerFeatureProviderHooks
{
    ShowMessage = message => statusBar.Post($"{message.Kind}: {message.Text}"),
    Progress = progress => statusBar.Post(progress.Title ?? progress.Message ?? progress.Token),
    ApplyWorkspaceEdit = async (edit, cancellationToken) => await ApplyOnUiThreadAsync(edit),
};

using var provider = new LanguageServerFeatureProvider(profile, [workspaceRoot], serverExecutablePath, hooks, logger: logger);
```

- An unset hook keeps the provider's default for its message: the logger receives a message, progress is dropped,
  and a server edit is answered as not applied.
- A set hook also enables the client capability it needs, so the server sends progress and edits only to a host
  that handles them.
- `ShowMessage` and `Progress` run one at a time on a thread-pool thread, in the order the server sent the
  messages, so they return promptly and post to the UI: a later server message waits for them, while the answers
  to the host's own requests do not.
- `ShowMessageRequest` and `ApplyWorkspaceEdit` return a task, so no server message waits for them, and they may
  take as long as the answer needs.
- `ApplyWorkspaceEdit` receives a `WorkspaceEdit` that names its documents by local file path, and a host that
  tracks its documents applies it with `WorkspaceEditApplier`. An edit with a file operation is answered as not
  applied without reaching the hook.
- A server's edit can name any file the user can write, whatever the workspace roots are, so a host that confines
  the server to its workspace refuses an edit outside it in the `ApplyWorkspaceEdit` hook.
- When the `ApplyWorkspaceEdit` hook throws, the server hears only that the host did not apply the edit.
- The hooks apply to a client that the provider creates. A host that builds its own `LanguageServerClient` passes
  its [client hooks](ClientGuide.md#host-hooks) to the constructor instead.

### Startup failure

`StartupFailed` reports a failed start, and `CapabilitiesChanged` fires when that failure changes the provider
state. A null or blank server path builds a provider with no client, so the first request, `OpenDocument` call or
`UpdateDocument` call reports a persistent failure. The provider does not check that the file exists, so a host that checks the path
first can report the problem itself.

A start that fails, such as one for a missing executable, is reported once as transient and once more as
persistent when the failure threshold is reached. `StartupFailure.Message` is provider-defined text, and
`IsPersistent` is `true` when the host must intervene, for example by recreating the provider with a valid path.
Branch on `StartupFailure.Code`, one of the `ProviderFailureCodes` values, and not on the message: a failed start is
`StartFailed` and a provider with no client is `ClientUnavailable`. `Exception` holds the exception that the failed
attempt raised, when there was one. `WorkspaceFileWatchFailure` carries a code and an exception the same way.

```csharp
var provider = new LanguageServerFeatureProvider(
    LuaLanguageServerProfileFactory.Create(),
    workspaceRootDirectoryPaths: [workspaceRoot],
    serverExecutablePath: luaLanguageServerPath);

provider.StartupFailed += (_, eventArgs) =>
{
    StartupFailure failure = eventArgs.Failure;
    editorDispatcher.Post(() => statusBar.Show(failure.Message, failure.IsPersistent));
};

provider.CapabilitiesChanged += (_, _) =>
{
    editorDispatcher.Post(() => completionMenu.IsEnabled = provider.IsAvailable);
};
```

## Document leases and cancellation

Pair each `OpenDocument` call with one `CloseDocument` call, because an open holds a lease until its close and an
update holds none; the [document lease semantics](ProviderAuthoring.md#document-lease-semantics) own the rules.

Each request record carries the document identifier (a local file path for the providers of this family) and the
current document text, and each request method accepts a `CancellationToken`. The supplied text is authoritative for the call: the provider synchronizes its tracked
document to that content before issuing the request, so a request works even when no editor has the
document open.

A server names the document of every location and edit by its URI, and the path read back from the URI can
spell the requested document differently, for example with a lowercase drive letter. The provider therefore
names the requested document the way the request did: a definition there carries no document identifier, and a
reference or an edit there carries the request's identifier, so a host that compares identifiers ordinally finds
its own document. Other documents carry the normalized local path.

A `DiagnosticsUpdated` or `SemanticTokensUpdated` event names its document by the identifier the host last opened
or renamed it with, so a diagnostics controller with an ordinal comparer sees the server's pushes too.

```csharp
using var requestCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

try
{
    var items = await provider.GetCompletionItemsAsync(
        new CompletionRequest(filePath, editorText, caretOffset),
        cancellationToken: requestCancellation.Token);
}
catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
{
    // The caller canceled; do not display this as an empty completion result or an error.
}
```

Caller cancellation propagates as `OperationCanceledException`. Provider disposal,
provider-enforced timeouts, internal connection failure, and unsupported capabilities use
the documented fallback value instead; they are not reported as caller cancellation.

## Using the client directly

A host that speaks LSP itself consumes `Nickelony.LanguageServer.Client.ILanguageServerClient` directly instead of
a provider. The threading, restart, failure-classification, and disposal rules below apply to that surface too; only
the shape differs.

- **Capability reads are lock-free snapshot reads.**
  - `NegotiatedCapabilities` (the [capability snapshot](ClientGuide.md#capability-negotiation)) may be read from
    any thread.
  - A read returns one published snapshot, so the flags and the semantic-token legend of one read belong to
    the same generation; read it again after `EnsureStartedAsync`.
  - A `Supports*` flag reads `false` while the capability is unnegotiated, so the surface does not
    distinguish an unknown capability from an unsupported one.
- **Sends are concurrent.**
  - `SendRequestAsync`, `SendNotificationAsync` and `PushSettingsAsync` may be called concurrently.
  - `EnsureStartedAsync` serializes concurrent startup attempts.
- **The client never restarts itself.**
  - A connection failure marks the current connection unhealthy and raises `ConnectionUnavailable`
    (reporting the generation that was active immediately before the loss).
  - The host calls `EnsureStartedAsync` again and re-synchronizes workspace state, so restart policy,
    backoff, and workspace re-sync stay with the host.
  - Every start, a restart included, sends the notifications of
    `LanguageServerClientOptions.SessionStartNotificationsProvider` right after `initialized`, so a host does
    not resend them after a restart.
  - A caller that detects a failure on a captured generation invalidates it with
    `TryMarkConnectionUnhealthy` so it cannot invalidate a newer connection; a generation already
    reported by `ConnectionUnavailable` must not be invalidated again.
- **Connection failures are classified by one base type.**
  - Every failure caused by the connection not being usable derives from
    `LanguageServerConnectionException` (itself an `IOException`), so one `catch` covers "no ready
    session yet", "became unavailable", and "superseded".
  - A JSON-RPC error response makes `SendRequestAsync` throw `LanguageServerRequestRejectedException`,
    which carries the server's `ErrorCode`. It derives from `Exception`, not the connection base,
    because the connection stays usable, so the connection `catch` does not cover it.
  - A send that races disposal either throws `ObjectDisposedException` or surfaces as
    `LanguageServerConnectionUnavailableException` when the connection is torn down mid-send;
    `EnsureStartedAsync` returns `false` when disposal wins after its entry check.
- **Disposal and event delivery.**
  - `Dispose` and `DisposeAsync` share one teardown driven by a single `DisposeWaitTimeout` budget
    (later stages are skipped once it is exhausted); only the first caller performs it and every
    caller observes its completion.
  - Event delivery stops when teardown completes the server-message dispatcher and the subscriber sets:
    no new delivery starts, a queued-but-unstarted payload is dropped, and a handler already started
    runs to completion.
  - Each subscribed payload-event handler runs independently on the thread pool, and it must marshal to the
    thread its consumer requires.
  - Notifications for the same handler run one at a time, and repeated pending payloads may coalesce to the
    latest while it is busy. Different handlers may run concurrently, and a failing handler is isolated.
  - Server messages from an attached session are delivered even before the client reports `IsReady`; only
    detached or unhealthy sessions are fenced.
  - Unlike the payload events, `ConnectionUnavailable` handlers run synchronously on the thread that reports the
    loss.

## Packages and imports by feature

A host references the packages of the features it uses and imports the namespaces below. `{Engine}` is
`AvalonEdit` for WPF or `AvaloniaEdit` for Avalonia.

| Feature | Packages | Namespaces a host imports |
| --- | --- | --- |
| Text model and edit kernel | `Nickelony.IDEKit.Core` | `Nickelony.IDEKit.Core.Text`, `Nickelony.IDEKit.Core.Editing` |
| Comments, comment scanning, auto-closing, formatting, indentation, identifiers, find and replace | `Nickelony.IDEKit.Core` | `Nickelony.IDEKit.Core.Comments`, `Nickelony.IDEKit.Core.Scanning`, `Nickelony.IDEKit.Core.AutoClosing`, `Nickelony.IDEKit.Core.Formatting`, `Nickelony.IDEKit.Core.Indentation`, `Nickelony.IDEKit.Core.Identifiers`, `Nickelony.IDEKit.Core.FindReplace` |
| Bookmarks, line status, change notifications, diagnostic segments, themes, regex highlighting rules, colors, line diffs, navigation locations, editor navigation targets, open editor descriptors, latest-request gates, argument checks, callbacks, threading, paths | `Nickelony.IDEKit.Core` | `Nickelony.IDEKit.Core.Bookmarks`, `Nickelony.IDEKit.Core.LineStatus`, `Nickelony.IDEKit.Core.Notifications`, `Nickelony.IDEKit.Core.Diagnostics`, `Nickelony.IDEKit.Core.Themes`, `Nickelony.IDEKit.Core.Highlighting`, `Nickelony.IDEKit.Core.Media`, `Nickelony.IDEKit.Core.Diffing`, `Nickelony.IDEKit.Core.Navigation`, `Nickelony.IDEKit.Core.Requests`, `Nickelony.IDEKit.Core.Validation`, `Nickelony.IDEKit.Core.Callbacks`, `Nickelony.IDEKit.Core.Threading`, `Nickelony.IDEKit.Core.Paths` |
| Fuzzy matching and word matching | `Nickelony.Matching` | `Nickelony.Matching` |
| Quick pick, command palette and quick open | `Nickelony.QuickPick`, and `Nickelony.QuickPick.Wpf` or `Nickelony.QuickPick.Avalonia`; `Nickelony.IDEKit.Workspace.QuickPick` for the file list and `Nickelony.IDEKit.LanguageFeatures.Controllers` for the go to line, open editors and symbol modes | `Nickelony.QuickPick`, `Nickelony.QuickPick.Wpf` or `Nickelony.QuickPick.Avalonia`, `Nickelony.IDEKit.Workspace.QuickPick` |
| Editor services, margins and renderers for one control | `Nickelony.IDEKit.{Engine}` | `Nickelony.IDEKit.{Engine}.AutoClosing`, `Nickelony.IDEKit.{Engine}.Bookmarks`, `Nickelony.IDEKit.{Engine}.Comments`, `Nickelony.IDEKit.{Engine}.Diagnostics`, `Nickelony.IDEKit.{Engine}.Editing`, `Nickelony.IDEKit.{Engine}.Formatting`, `Nickelony.IDEKit.{Engine}.Highlighting`, `Nickelony.IDEKit.{Engine}.Media`, `Nickelony.IDEKit.{Engine}.Indentation`, `Nickelony.IDEKit.{Engine}.LineStatus`, `Nickelony.IDEKit.{Engine}.Navigation`, `Nickelony.IDEKit.{Engine}.Rendering`, `Nickelony.IDEKit.{Engine}.Text`, `Nickelony.IDEKit.{Engine}.Threading` |
| Language-feature contracts and records | `Nickelony.IDEKit.LanguageFeatures` | `Nickelony.IDEKit.LanguageFeatures.Completion`, `Nickelony.IDEKit.LanguageFeatures.Hover`, `Nickelony.IDEKit.LanguageFeatures.SignatureHelp`, `Nickelony.IDEKit.LanguageFeatures.CodeActions`, `Nickelony.IDEKit.LanguageFeatures.Diagnostics`, `Nickelony.IDEKit.LanguageFeatures.DocumentSymbols`, `Nickelony.IDEKit.LanguageFeatures.Editing`, `Nickelony.IDEKit.LanguageFeatures.Formatting`, `Nickelony.IDEKit.LanguageFeatures.Markup`, `Nickelony.IDEKit.LanguageFeatures.Navigation`, `Nickelony.IDEKit.LanguageFeatures.Rename`, `Nickelony.IDEKit.LanguageFeatures.SemanticTokens`, `Nickelony.IDEKit.LanguageFeatures.Synchronization`, `Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols` |
| Language-feature controllers, completion sessions and snippets | `Nickelony.IDEKit.LanguageFeatures.Controllers` | `Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions`, `Nickelony.IDEKit.LanguageFeatures.Controllers.Completion`, `Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit`, `Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets`, `Nickelony.IDEKit.LanguageFeatures.Controllers.Diagnostics`, `Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols`, `Nickelony.IDEKit.LanguageFeatures.Controllers.Formatting`, `Nickelony.IDEKit.LanguageFeatures.Controllers.Hover`, `Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation`, `Nickelony.IDEKit.LanguageFeatures.Controllers.SemanticTokens`, `Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp`, `Nickelony.IDEKit.LanguageFeatures.Controllers.WorkspaceSymbols` |
| Language-feature controllers and presenters for one control | `Nickelony.IDEKit.{Engine}.LanguageFeatures` | `Nickelony.IDEKit.{Engine}.LanguageFeatures.CodeActions`, `Nickelony.IDEKit.{Engine}.LanguageFeatures.Completion`, `Nickelony.IDEKit.{Engine}.LanguageFeatures.Diagnostics`, `Nickelony.IDEKit.{Engine}.LanguageFeatures.Editing`, `Nickelony.IDEKit.{Engine}.LanguageFeatures.Formatting`, `Nickelony.IDEKit.{Engine}.LanguageFeatures.Hover`, `Nickelony.IDEKit.{Engine}.LanguageFeatures.Navigation`, `Nickelony.IDEKit.{Engine}.LanguageFeatures.SemanticTokens`, `Nickelony.IDEKit.{Engine}.LanguageFeatures.SignatureHelp` |
| Markdown rendering | `Nickelony.IDEKit.Markdown`, `Nickelony.IDEKit.{Engine}.Markdown` | `Nickelony.IDEKit.Markdown`, `Nickelony.IDEKit.Markdown.Model`, `Nickelony.IDEKit.Markdown.Planning`, `Nickelony.IDEKit.{Engine}.Markdown` |
| TextMate highlighting | `Nickelony.IDEKit.TextMate`, `Nickelony.IDEKit.{Engine}.TextMate` | `Nickelony.IDEKit.TextMate`, `Nickelony.IDEKit.{Engine}.TextMate` |
| Workspace documents and attached views | `Nickelony.IDEKit.Workspace`, `Nickelony.IDEKit.Workspace.Views` | `Nickelony.IDEKit.Workspace`, `Nickelony.IDEKit.Workspace.Documents`, `Nickelony.IDEKit.Workspace.FileSystem`, `Nickelony.IDEKit.Workspace.Reloading`, `Nickelony.IDEKit.Workspace.Replacement`, `Nickelony.IDEKit.Workspace.Views`, `Nickelony.IDEKit.Workspace.Views.Coordination` |
| A ready language package | `Nickelony.LanguageServer.Lua` or `Nickelony.LanguageServer.Roslyn`, with `Nickelony.LanguageServer.Provider.Abstractions` | `Nickelony.LanguageServer.Lua` or `Nickelony.LanguageServer.Roslyn`, `Nickelony.LanguageServer.Provider`, and `Nickelony.LanguageServer.Provider.Abstractions` |
| The client alone | `Nickelony.LanguageServer.Client` | `Nickelony.LanguageServer.Client`, and `Nickelony.LanguageServer.Client.Protocol` for protocol-level code |
| Authoring a provider | `Nickelony.LanguageServer.Provider` | `Nickelony.LanguageServer.Provider` |
| Key bindings | `Nickelony.KeyBindings`, and `Nickelony.KeyBindings.Wpf` or `Nickelony.KeyBindings.Avalonia` | `Nickelony.KeyBindings`, and `Nickelony.KeyBindings.Wpf` or `Nickelony.KeyBindings.Avalonia`; `Nickelony.KeyBindings.OverrideEntries` for code that reads or edits the entries of an override document |
| Watching workspace files | `Nickelony.IDEKit.FileWatching` | `Nickelony.IDEKit.FileWatching` |
| Running a process, indexing a JSON Schema | `Nickelony.IDEKit.Processes`, `Nickelony.IDEKit.JsonSchema` | `Nickelony.IDEKit.Processes`, `Nickelony.IDEKit.JsonSchema` |

The binding packages split their namespaces as finely as the neutral packages do, so a feature name imports
the same way in every tier. The cost is more `using` lines: wiring a language feature to one control imports
a contract namespace, a controllers namespace and a binding namespace for that feature, and a host that wants
fewer lines declares them once in a `global using` file.

## Package versions

The packages ship in lockstep and pin each other to an exact version, so a host upgrades every `Nickelony.*`
package it references to the same version in one step. Restore warns about a host that mixes versions
(NU1608), and a host that wants the mismatch to fail the restore lists NU1608 in its `WarningsAsErrors`.

## Testing a local build in a consumer

Restore a local package build into a consumer either into a private `RestorePackagesPath`, or with a version
number nothing has used yet. The order of the feed sources does not guard a restore: a same-version package in
another feed or in the global packages folder can win, so a consumer gate that reuses a version can report a
false pass or a false failure.
