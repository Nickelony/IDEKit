namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Identifies a deterministic text-edit preparation issue.</summary>
public sealed record TextEditPreparationIssue
{
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public TextEditPreparationIssue(TextEditPreparationIssueKind kind, int editIndex, int? relatedEditIndex, string message)
	{
		ArgumentNullException.ThrowIfNull(message);

		Kind = kind;
		EditIndex = editIndex;
		RelatedEditIndex = relatedEditIndex;
		Message = message;
	}

	/// <summary>The machine-readable category of the issue; branch on this instead of matching <see cref="Message"/> text.</summary>
	public TextEditPreparationIssueKind Kind { get; }

	/// <summary>The index of the edit the issue concerns; for a conflict, the edit that triggered it.</summary>
	public int EditIndex { get; }

	/// <summary>The index of the other conflicting edit, or <see langword="null"/> when the issue concerns a single edit.</summary>
	public int? RelatedEditIndex { get; }

	/// <summary>The human-readable description of the issue.</summary>
	public string Message { get; }
}
