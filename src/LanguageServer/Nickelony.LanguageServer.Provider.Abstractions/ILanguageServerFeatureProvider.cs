using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.Formatting;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.Synchronization;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

namespace Nickelony.LanguageServer.Provider.Abstractions;

/// <summary>Defines the language-neutral provider contract a text editor uses to obtain language features from a language server.</summary>
/// <remarks>
/// <para>
/// The provider implements each language-feature
/// contract listed here, so a consumer of one feature depends on that feature's interface and mocks it alone: <see cref="ICompletionProvider"/>,
/// <see cref="IHoverProvider"/>, <see cref="IDefinitionProvider"/>, <see cref="ISignatureHelpProvider"/>, <see cref="ICodeActionProvider"/>,
/// <see cref="IDocumentSymbolProvider"/>, <see cref="IWorkspaceSymbolProvider"/>, <see cref="IReferenceProvider"/>,
/// <see cref="IRenameProvider"/> and <see cref="IFormattingProvider"/>. Diagnostics and semantic tokens are sources, because the server
/// pushes them (<see cref="IDiagnosticsSource"/>, <see cref="ISemanticTokensSource"/>), and <see cref="ILanguageServerCapabilitiesSource"/> publishes
/// the advertised capabilities. The documents a host mirrors to the server arrive through <see cref="IDocumentSynchronizationTarget"/>.
/// </para>
/// <para>
/// This list does not grow after publication, because a new base interface would add abstract members to every implementer.
/// A language feature added later arrives as a sibling interface that the provider also implements, and a consumer
/// reads it with a type test, for example <c>provider as IInlayHintProvider</c> for an interface that does not exist yet, so a decorator or a
/// fake that does not implement it stays valid.
/// </para>
/// <para>
/// A document is identified by its local file path, and a request that carries an identifier the provider cannot map to a file is logged and returns
/// its documented fallback. A feature the server does not advertise returns its documented fallback rather than failing.
/// </para>
/// </remarks>
public interface ILanguageServerFeatureProvider
	: IDisposable,
		IAsyncDisposable,
		ICompletionProvider,
		IHoverProvider,
		IDefinitionProvider,
		ISignatureHelpProvider,
		ICodeActionProvider,
		IDocumentSymbolProvider,
		IWorkspaceSymbolProvider,
		IReferenceProvider,
		IRenameProvider,
		IFormattingProvider,
		IDiagnosticsSource,
		ISemanticTokensSource,
		ILanguageServerCapabilitiesSource,
		IDocumentSynchronizationTarget
{
	/// <summary>Indicates whether a ready language-server session is available to serve requests.</summary>
	/// <remarks>
	/// This is <see langword="false"/> before lazy startup, while the provider is starting or restarting, when no session is available,
	/// and after disposal. Decide whether to issue a request with this property.
	/// </remarks>
	bool IsAvailable { get; }

	/// <summary>Raised when the provider reports a language-server startup failure.</summary>
	/// <remarks>
	/// The event is raised at most once for transient failures and at most once more when failures become persistent.
	/// A successful start resets both, so a later failure is reported again.
	/// </remarks>
	event EventHandler<StartupFailedEventArgs>? StartupFailed;

	/// <summary>Raised when the workspace file watcher cannot be started or recovered and external changes are not forwarded.</summary>
	/// <remarks>
	/// Raised once per unresolved failure; a successful automatic recovery suppresses it. An initial creation or activation failure is reported
	/// directly because there is no watcher to recover. A missing workspace root is logged and does not raise this event.
	/// </remarks>
	event EventHandler<WorkspaceFileWatchFailedEventArgs>? WorkspaceFileWatchFailed;
}
