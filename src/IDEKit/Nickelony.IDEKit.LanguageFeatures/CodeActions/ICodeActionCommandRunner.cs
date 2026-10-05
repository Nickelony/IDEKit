namespace Nickelony.IDEKit.LanguageFeatures.CodeActions;

/// <summary>Runs the command of a code action after the action's edit is applied.</summary>
/// <remarks>
/// A provider implements this contract beside <see cref="ICodeActionProvider"/> when its actions can carry a <see cref="CodeActionCommand"/>,
/// and it offers an action only when it can run the action's command. The command may change documents on its own account, for example by asking
/// the host to apply an edit, so the caller runs it after it applied <see cref="CodeAction.Edit"/> and not before.
/// </remarks>
public interface ICodeActionCommandRunner
{
	/// <summary>Runs the command.</summary>
	/// <param name="command">The command of an action that this provider offered.</param>
	/// <param name="cancellationToken">A token that can cancel the run.</param>
	/// <returns>
	/// <see langword="true"/> when the provider ran the command; otherwise, <see langword="false"/>, for example when the command
	/// is not one of its own, the server is unavailable, or the server rejected the command.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
	Task<bool> TryRunAsync(CodeActionCommand command, CancellationToken cancellationToken = default);
}
