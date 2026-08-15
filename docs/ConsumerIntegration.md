# Consumer integration guide

This guide describes the lifecycle and threading contract a host should follow when
consuming a provider package. The contract is the same for every provider in the family, so the
`Nickelony.LanguageServer.Lua` sample below stands in for `Nickelony.LanguageServer.CSharp` or
`Nickelony.LanguageServer.VisualBasic` (or any future provider): construct the provider, subscribe to
callbacks before opening documents, drive the document lifecycle, and dispose it.

If you are implementing a language provider rather than consuming one, start with the
[provider authoring guide](ProviderAuthoring.md).

## Construction and disposal

Construct the provider during host setup, subscribe to callbacks before opening documents,
and dispose it during host shutdown. The provider owns its underlying client and watcher.
Disposal is idempotent and closes callback admission before releasing those resources.

```csharp
var provider = new LuaLanguageServerIntelliSenseProvider(
    workspaceRootDirectoryPaths: [workspaceRoot],
    serverExecutablePath: luaLanguageServerPath,
    logger: logger);

try
{
    provider.DiagnosticsUpdated += OnDiagnosticsUpdated;
    provider.SemanticTokensUpdated += OnSemanticTokensUpdated;

    provider.OpenDocument(filePath, initialText);

    IReadOnlyList<TextCompletionItem> items =
        await provider.GetCompletionItemsAsync(
            new LanguageServerCompletionRequest(filePath, initialText, new TextPosition(line, column)),
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

Diagnostics, semantic-token, capability, startup-failure, and workspace-watcher callbacks
may arrive on background threads. Their handlers run serially for one invocation, and a
failing handler does not prevent later subscribers from running. Marshal to the editor UI
dispatcher before touching controls, and treat each payload as an owned immutable snapshot.

Payload events follow the standard .NET event pattern: the provider is the sender, and the
event data is a single `EventArgs` object (`eventArgs.FilePath`, `eventArgs.Diagnostics`,
`eventArgs.Failure`, and so on).

```csharp
provider.DiagnosticsUpdated += (_, eventArgs) =>
{
    editorDispatcher.Post(() => errorList.Replace(eventArgs.FilePath, eventArgs.Diagnostics));
};
```

`CapabilitiesChanged` means that the host must reread `IsAvailable` and the capability
properties; it does not carry a cached capability payload.

Capability flags (`SupportsRename`, `SupportsReferences`, `SupportsFormatting`,
`SupportsDocumentSymbols`, `SupportsCodeActions`) report the negotiated state of the current
session and are `false` before the first successful lazy start. Do not make a flag check the only
path to a request: issue the request when its documented fallback is acceptable, or trigger
startup through a lifecycle member such as `OpenDocument`, then reread the flags after
`CapabilitiesChanged`.

## Document references and cancellation

Each `OpenDocument` call acquires one open reference. Pair it with one
`CloseDocument` call. Repeated opens therefore require repeated closes. `UpdateDocument`
does not acquire an open reference, although the provider may temporarily retain an
idle server-open document while a request reference is active.

Request methods accept the current buffer text and a `CancellationToken`. The supplied text is
authoritative for the call: the provider synchronizes its tracked document to that content before
issuing the request, so a request works even when no editor has the document open.

```csharp
using var requestCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

try
{
    var items = await provider.GetCompletionItemsAsync(
        new LanguageServerCompletionRequest(filePath, editorText, new TextPosition(line, column)),
        cancellationToken: requestCancellation.Token);
}
catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
{
    // The caller canceled; do not display this as an empty completion result or an error.
}
```

Caller cancellation propagates as `OperationCanceledException`. Provider disposal,
provider-enforced timeouts, internal transport failure, and unsupported capabilities use
the documented fallback value instead; they are not reported as caller cancellation.
