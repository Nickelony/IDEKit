using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Describes what the language server of one connection session advertised during initialization, as one immutable snapshot.</summary>
/// <remarks>
/// <para>
/// A reader gets every member from one session: the flags, the text-synchronization mode, and the semantic-token legend never mix
/// two connection generations, because <see cref="ILanguageServerClient.NegotiatedCapabilities"/> hands out the whole snapshot at once.
/// </para>
/// <para>
/// <see cref="None"/> is the snapshot of a client with no negotiated session, before initialization completes and after the connection is detached
/// or becomes unavailable. A capability the server did not advertise reads <see langword="false"/> like one that is not negotiated yet. The legend,
/// trigger-character and command lists are read-only snapshots, and record equality compares them by reference.
/// </para>
/// </remarks>
public sealed record NegotiatedServerCapabilities
{
	/// <summary>The snapshot of a client with no negotiated session: every flag is <see langword="false"/> and the legend is empty.</summary>
	public static NegotiatedServerCapabilities None { get; } = new();

	/// <summary>The text-document synchronization mode the server negotiated.</summary>
	/// <remarks>
	/// A server that advertises no text synchronization also reads <see cref="TextDocumentSyncKind.None"/>, which startup accepts
	/// only when <see cref="LanguageServerClientOptions.RequireTextDocumentSynchronization"/> is <see langword="false"/>.
	/// </remarks>
	public TextDocumentSyncKind TextDocumentSyncKind { get; init; }

	/// <summary>The semantic token types of the server's legend.</summary>
	public IReadOnlyList<string> SemanticTokenTypes { get; init; } = [];

	/// <summary>The semantic token modifiers of the server's legend.</summary>
	public IReadOnlyList<string> SemanticTokenModifiers { get; init; } = [];

	/// <summary>Indicates whether the server supports <c>completionItem/resolve</c>.</summary>
	public bool SupportsCompletionResolve { get; init; }

	/// <summary>The characters that the server opens completion on when they are typed.</summary>
	public IReadOnlyList<string> CompletionTriggerCharacters { get; init; } = [];

	/// <summary>Indicates whether the server supports <c>textDocument/documentSymbol</c>.</summary>
	public bool SupportsDocumentSymbols { get; init; }

	/// <summary>Indicates whether the server supports <c>workspace/symbol</c>.</summary>
	public bool SupportsWorkspaceSymbols { get; init; }

	/// <summary>Indicates whether the server supports <c>workspaceSymbol/resolve</c>.</summary>
	/// <remarks>
	/// A server that advertises the request may list a symbol without its range and answer the range when asked, so a host resolves the symbol
	/// that the user chose.
	/// </remarks>
	public bool SupportsWorkspaceSymbolResolve { get; init; }

	/// <summary>Indicates whether the server supports <c>textDocument/codeAction</c>.</summary>
	public bool SupportsCodeActions { get; init; }

	/// <summary>Indicates whether the server supports <c>codeAction/resolve</c>.</summary>
	/// <remarks>
	/// The resolve flow is host-owned because it sends an action payload back over the connection.
	/// </remarks>
	public bool SupportsCodeActionResolve { get; init; }

	/// <summary>The commands the server executes through <c>workspace/executeCommand</c>, or an empty list when it advertises none.</summary>
	/// <remarks>
	/// A command that the server does not list here is not a server command: the server may name it in a code action for the client to interpret,
	/// as the Roslyn language server does for its <c>roslyn.client.*</c> commands.
	/// </remarks>
	public IReadOnlyList<string> ExecuteCommands { get; init; } = [];

	/// <summary>Indicates whether the server supports <c>textDocument/references</c>.</summary>
	public bool SupportsReferences { get; init; }

	/// <summary>Indicates whether the server supports <c>textDocument/rename</c>.</summary>
	public bool SupportsRename { get; init; }

	/// <summary>Indicates whether the server supports <c>textDocument/formatting</c>.</summary>
	public bool SupportsFormatting { get; init; }

	/// <summary>Indicates whether the server supports <c>textDocument/hover</c>.</summary>
	public bool SupportsHover { get; init; }

	/// <summary>Indicates whether the server supports <c>textDocument/definition</c>.</summary>
	public bool SupportsDefinition { get; init; }

	/// <summary>Indicates whether the server supports <c>textDocument/signatureHelp</c>.</summary>
	public bool SupportsSignatureHelp { get; init; }

	/// <summary>The characters that the server opens signature help on when they are typed.</summary>
	public IReadOnlyList<string> SignatureHelpTriggerCharacters { get; init; } = [];

	/// <summary>The characters that the server updates signature help on when it is already open.</summary>
	public IReadOnlyList<string> SignatureHelpRetriggerCharacters { get; init; } = [];

	/// <summary>Indicates whether the server supports full semantic token requests.</summary>
	public bool SupportsSemanticTokensFull { get; init; }

	/// <summary>Indicates whether the server supports pull diagnostics (<c>textDocument/diagnostic</c>).</summary>
	public bool SupportsPullDiagnostics { get; init; }
}
