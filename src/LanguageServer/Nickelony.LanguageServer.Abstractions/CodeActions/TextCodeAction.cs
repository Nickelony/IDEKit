using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents a single code action (quick fix or refactoring) offered for a document range.
/// </summary>
/// <remarks>
/// <para>
/// The action's changes are carried as a <see cref="TextWorkspaceEdit"/> so a consumer can apply
/// every document change as one atomic operation. Actions that a server expresses as a command
/// instead of an edit cannot be represented by the shared edit model; providers omit them rather
/// than offering an action that cannot run.
/// </para>
/// <para>
/// Record equality compares the title, kind, preferred flag, and the workspace edit by value.
/// </para>
/// </remarks>
public sealed record TextCodeAction
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeAction"/> record.
	/// </summary>
	/// <param name="title">The human-readable action title.</param>
	/// <param name="edit">The workspace edit the action applies.</param>
	/// <param name="kind">
	/// The server-reported action kind (for example <c>quickfix</c> or <c>refactor.rewrite</c>), or
	/// <see langword="null"/> when the action carries no kind. A blank value is treated as absent, and
	/// surrounding whitespace is trimmed from other values.
	/// </param>
	/// <param name="isPreferred">
	/// Whether the server marks this action as preferred among otherwise equivalent actions.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="title"/> or <paramref name="edit"/> is <see langword="null"/>.
	/// </exception>
	public TextCodeAction(string title, TextWorkspaceEdit edit, string? kind = null, bool isPreferred = false)
	{
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(edit);

		Title = title;
		Edit = edit;
		Kind = OptionalText.Normalize(kind);
		IsPreferred = isPreferred;
	}

	/// <summary>
	/// Gets the human-readable action title.
	/// </summary>
	public string Title { get; }

	/// <summary>
	/// Gets the action kind reported by the server (for example <c>quickfix</c> or <c>refactor.rewrite</c>), or
	/// <see langword="null"/> when the server did not specify one.
	/// </summary>
	public string? Kind { get; }

	/// <summary>
	/// Gets a value indicating whether the server marks this action as preferred among otherwise
	/// equivalent actions.
	/// </summary>
	public bool IsPreferred { get; }

	/// <summary>
	/// Gets the workspace edit the action applies.
	/// </summary>
	public TextWorkspaceEdit Edit { get; }

	/// <summary>
	/// Determines whether the supplied action has the same title, kind, preferred flag, and edit.
	/// </summary>
	/// <param name="other">The action to compare with.</param>
	/// <returns><see langword="true"/> when the values are equal; otherwise, <see langword="false"/>.</returns>
	public bool Equals(TextCodeAction? other)
	{
		if (other is null)
			return false;

		if (ReferenceEquals(this, other))
			return true;

		return string.Equals(Title, other.Title, StringComparison.Ordinal)
			&& string.Equals(Kind, other.Kind, StringComparison.Ordinal)
			&& IsPreferred == other.IsPreferred
			&& Edit.Equals(other.Edit);
	}

	/// <summary>
	/// Serves as the hash function for <see cref="TextCodeAction"/>.
	/// </summary>
	/// <returns>A hash code over the title, kind, preferred flag, and edit.</returns>
	public override int GetHashCode()
		=> HashCode.Combine(Title, Kind, IsPreferred, Edit);
}
