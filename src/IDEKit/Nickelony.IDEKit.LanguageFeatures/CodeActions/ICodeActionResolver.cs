namespace Nickelony.IDEKit.LanguageFeatures.CodeActions;

/// <summary>Resolves a code action that a provider offered without its edit.</summary>
/// <remarks>
/// <para>
/// A provider implements this contract beside <see cref="ICodeActionProvider"/> when it offers actions cheaply and computes their edits on demand,
/// as a language server does for <c>codeAction/resolve</c>. The host or controller that applies an action calls it for the one action the user chose,
/// never for every offered action.
/// </para>
/// <para>
/// The resolution answers the text the action was offered for, so a provider returns the action unchanged when the document
/// text differs from it, whether the document changed before the call or while the provider computed the edit. A caller that applies the result
/// still checks the editor's text after the call returns, because only the caller sees the editor.
/// </para>
/// </remarks>
public interface ICodeActionResolver
{
	/// <summary>Resolves the supplied action into one that carries the edit and the command the provider computes for it.</summary>
	/// <param name="action">An action that this provider offered.</param>
	/// <param name="cancellationToken">A token that can cancel the resolution.</param>
	/// <returns>
	/// The resolved action, or <paramref name="action"/> unchanged
	/// when the provider cannot resolve it: the action is not one of its own, the text it was offered for changed, or the resolution failed.
	/// The result is never <see langword="null"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
	Task<CodeAction> ResolveAsync(CodeAction action, CancellationToken cancellationToken = default);
}
