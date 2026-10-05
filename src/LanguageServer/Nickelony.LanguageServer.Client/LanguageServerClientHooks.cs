using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>The delegates through which a host receives the server messages that the client does not surface by itself.</summary>
/// <remarks>
/// <para>
/// Every hook is optional. Without <see cref="ShowMessage"/> the client logs <c>window/showMessage</c>
/// at the level of the message type, and without <see cref="ShowMessageRequest"/> it logs <c>window/showMessageRequest</c> the same way
/// and answers <see langword="null"/>. Without <see cref="Progress"/> it drops <c>$/progress</c>, and without <see cref="ApplyWorkspaceEdit"/>
/// it answers a failure to <c>workspace/applyEdit</c>. A hook that is set also enables the client capability it needs,
/// <c>window.workDoneProgress</c> for <see cref="Progress"/> and <c>workspace.applyEdit</c> for <see cref="ApplyWorkspaceEdit"/>, so a server sends
/// those messages only to a client that handles them; without the hook the client advertises neither, whatever the host's capabilities payload says.
/// </para>
/// <para>
/// <see cref="ShowMessage"/> and <see cref="Progress"/> run one at a time on a thread-pool thread, in the order the server sent the messages,
/// so they must return promptly: a later server message waits for them, while the answers to the host's own requests do not. A host that touches
/// its UI posts to it. <see cref="ShowMessageRequest"/> and <see cref="ApplyWorkspaceEdit"/> return a task, so no server message waits
/// for them and they may take as long as the answer needs. A hook that throws is logged and answers as an unset hook does, and the token of a request
/// hook is canceled when the server cancels the request or the client is disposed.
/// </para>
/// </remarks>
public sealed class LanguageServerClientHooks
{
	/// <summary>Receives a <c>window/showMessage</c> notification, or <see langword="null"/> to log it.</summary>
	public Action<WindowMessageParams>? ShowMessage { get; init; }

	/// <summary>
	/// Answers a <c>window/showMessageRequest</c> request with the action the user chose,
	/// or <see langword="null"/> when the user chose none; a <see langword="null"/> hook logs the message and answers <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// An answer whose title matches none of the actions the server offered counts as no choice, because the server accepts only one of its own.
	/// </remarks>
	public Func<ShowMessageRequestParams, CancellationToken, Task<MessageActionItemPayload?>>? ShowMessageRequest { get; init; }

	/// <summary>Receives a <c>$/progress</c> notification that carries a work-done report, or <see langword="null"/> to drop it.</summary>
	public Action<ProgressParams>? Progress { get; init; }

	/// <summary>
	/// Answers a <c>workspace/applyEdit</c> request with whether the host applied the edit, or <see langword="null"/> to answer that it did not.
	/// </summary>
	/// <remarks>
	/// The edit can name any file the user can write, whatever the workspace roots are, so a host that confines the server to its workspace refuses
	/// an edit outside it. A hook that throws is logged, and the server is told only that the client did not apply the edit.
	/// </remarks>
	public Func<ApplyWorkspaceEditParams, CancellationToken, Task<ApplyWorkspaceEditResponse>>? ApplyWorkspaceEdit { get; init; }
}
