using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;

namespace Nickelony.IDEKit.LanguageFeatures.CodeActions;

/// <summary>Represents a single code action (quick fix or refactoring) offered for a document range.</summary>
/// <remarks>
/// <para>
/// An action carries its changes as a <see cref="WorkspaceEdit"/>, so a consumer applies every document change as one atomic operation,
/// a <see cref="CodeActionCommand"/> that the provider runs after the edit is applied, or opaque <see cref="Data"/> that identifies the action
/// to the provider that produced it, or any of the three. A host applies <see cref="Edit"/> when it is present, and it hands <see cref="Data"/>
/// back to the provider unread.
/// </para>
/// <para>
/// A provider
/// that implements <see cref="ICodeActionResolver"/> resolves an action without an edit before the host applies it, and one that implements
/// <see cref="ICodeActionCommandRunner"/> runs the command; a provider omits an action it can neither resolve nor run, instead of offering
/// one that does nothing. Record equality compares the title, kind, preferred flag, edit, command, and data.
/// </para>
/// </remarks>
public sealed record CodeAction
{
	/// <param name="title">The human-readable action title.</param>
	/// <param name="edit">The workspace edit the action applies, or <see langword="null"/> when the action carries none.</param>
	/// <param name="kind">
	/// The provider-reported action kind (for example <c>quickfix</c> or <c>refactor.rewrite</c>), or <see langword="null"/> when the action
	/// carries no kind; a blank value is treated as absent, and surrounding whitespace is trimmed from other values.
	/// </param>
	/// <param name="isPreferred">Whether the provider marks this action as preferred among otherwise equivalent actions.</param>
	/// <param name="data">
	/// Opaque data the host hands back to the provider that produced the action,
	/// or <see langword="null"/> when it carries none; the library never inspects it.
	/// </param>
	/// <param name="command">
	/// The command the provider runs after the action's edit is applied, or <see langword="null"/> when the action carries none.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="title"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="title"/> is blank.</exception>
	public CodeAction(
		string title,
		WorkspaceEdit? edit = null,
		string? kind = null,
		bool isPreferred = false,
		object? data = null,
		CodeActionCommand? command = null
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(title);

		Title = title;
		Edit = edit;
		Kind = OptionalTextNormalizer.Normalize(kind);
		IsPreferred = isPreferred;
		Data = data;
		Command = command;
	}

	/// <summary>The human-readable action title.</summary>
	public string Title { get; }

	/// <summary>
	/// The action kind reported by the provider (for example <c>quickfix</c> or <c>refactor.rewrite</c>), or <see langword="null"/> when the provider
	/// did not specify one.
	/// </summary>
	public string? Kind { get; }

	/// <summary>Indicates whether the provider marks this action as preferred among otherwise equivalent actions.</summary>
	public bool IsPreferred { get; }

	/// <summary>The workspace edit the action applies, or <see langword="null"/> when the action carries none.</summary>
	public WorkspaceEdit? Edit { get; }

	/// <summary>The command the provider runs after the action's edit is applied, or <see langword="null"/> when the action carries none.</summary>
	public CodeActionCommand? Command { get; }

	/// <summary>
	/// The opaque data the host hands back to the provider that produced the action, or <see langword="null"/> when it carries none.
	/// </summary>
	public object? Data { get; }
}
