using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Identifies a deterministic text-edit preparation issue.
/// </summary>
public sealed record TextEditPreparationIssue
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextEditPreparationIssue"/> record.
	/// </summary>
	/// <param name="kind">The machine-readable category of the issue.</param>
	/// <param name="editIndex">
	/// The index of the edit within the caller's batch: the same index that <see cref="TextEditOperation.EditIndex"/>
	/// carries for a prepared operation; for a conflict, this is the edit that triggered it.
	/// </param>
	/// <param name="relatedEditIndex">
	/// The index of the other edit in the conflicting pair, or <see langword="null"/> when the
	/// issue concerns a single edit. Multiple insertions at one offset are valid, so no
	/// duplicate-insertion issue exists.
	/// </param>
	/// <param name="message">The human-readable description of the issue.</param>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public TextEditPreparationIssue(
		TextEditPreparationIssueKind kind,
		int editIndex,
		int? relatedEditIndex,
		string message)
	{
		ArgumentNullException.ThrowIfNull(message);

		Kind = kind;
		EditIndex = editIndex;
		RelatedEditIndex = relatedEditIndex;
		Message = message;
	}

	/// <summary>
	/// Gets the machine-readable category of the issue. Branch on this instead of matching
	/// <see cref="Message"/> text.
	/// </summary>
	public TextEditPreparationIssueKind Kind { get; }

	/// <summary>
	/// Gets the index of the edit the issue concerns.
	/// </summary>
	public int EditIndex { get; }

	/// <summary>
	/// Gets the index of the other conflicting edit, or <see langword="null"/> when the
	/// issue concerns a single edit.
	/// </summary>
	public int? RelatedEditIndex { get; }

	/// <summary>
	/// Gets the human-readable description of the issue.
	/// </summary>
	public string Message { get; }
}
