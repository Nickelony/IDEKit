namespace Nickelony.LanguageServer.Provider;

/// <summary>The delegates through which a host receives the messages that a language server sends it besides the answers to its requests.</summary>
/// <remarks>
/// <para>
/// Every hook is optional, and the hooks apply only to a client
/// that the provider creates. Without <see cref="ShowMessage"/> and <see cref="ShowMessageRequest"/> the provider logs a server message
/// at the level of its kind, without <see cref="Progress"/> it drops the progress reports, and without <see cref="ApplyWorkspaceEdit"/> it answers
/// that it did not apply an edit. A hook that is set also enables the client capability it needs, so a server sends progress and edits only to a host
/// that handles them.
/// </para>
/// <para>
/// <see cref="ShowMessage"/> and <see cref="Progress"/> run one at a time on a thread-pool thread, in the order the server sent the messages,
/// so they return promptly: a later server message waits for them, while the answers to the host's own requests do not. A host that touches
/// its UI posts to it. <see cref="ShowMessageRequest"/> and <see cref="ApplyWorkspaceEdit"/> return a task, so no server message waits
/// for them and they may take as long as the answer needs; a host that touches its UI awaits the UI thread inside them. A hook that throws is logged
/// and answers as an unset hook does.
/// </para>
/// </remarks>
public sealed class LanguageServerFeatureProviderHooks
{
	/// <summary>Receives a message that the server asks the host to show, or <see langword="null"/> to log it.</summary>
	public Action<WindowMessage>? ShowMessage { get; init; }

	/// <summary>
	/// Answers a message that offers actions with the title of the action the user chose,
	/// or <see langword="null"/> when the user chose none; a <see langword="null"/> hook logs the message and answers that the user chose none.
	/// </summary>
	/// <remarks>
	/// A title that names none of the message's actions counts as no choice.
	/// </remarks>
	public Func<WindowMessage, CancellationToken, Task<string?>>? ShowMessageRequest { get; init; }

	/// <summary>Receives a progress report of the server, or <see langword="null"/> to drop it.</summary>
	public Action<WorkDoneProgress>? Progress { get; init; }

	/// <summary>
	/// Applies an edit that the server asks the host to apply, and answers whether it did, or is <see langword="null"/> to answer that it did not.
	/// </summary>
	/// <remarks>
	/// The edit names its documents by their local file paths, and a host that tracks its documents applies it with <c>WorkspaceEditApplier</c>.
	/// An edit that the shared model cannot represent, such as one that creates, renames or deletes a file, is answered as not applied
	/// without reaching the hook. The edit can name any file the user can write, whatever the workspace roots are, so a host that confines the server
	/// to its workspace refuses an edit outside it. A hook that throws is logged, and the server is told only that the host did not apply the edit.
	/// </remarks>
	public Func<WorkspaceEdit, CancellationToken, Task<bool>>? ApplyWorkspaceEdit { get; init; }
}
