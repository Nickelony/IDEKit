namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Contains prepared edits and deterministic issues for one edit batch.</summary>
/// <remarks>
/// A result either succeeded - no issues and operations ready to apply - or failed - issues and no operations.
/// Create results through <see cref="Success"/> and <see cref="Failure"/> so the contradictory combination cannot be represented.
/// </remarks>
public sealed class TextEditPreparationResult
{
	// A failed result has no operations, so every failed result shares one immutable empty batch
	// instead of allocating a fresh carrier per failure.
	private static readonly PreparedTextEdits s_emptyEdits = new([]);

	// A successful result carries no issues, so every successful result shares one immutable empty list
	// instead of allocating a wrapper per preparation.
	private static readonly IReadOnlyList<TextEditPreparationIssue> s_emptyIssues = Array.AsReadOnly(Array.Empty<TextEditPreparationIssue>());

	private TextEditPreparationResult(PreparedTextEdits edits, IReadOnlyList<TextEditPreparationIssue> issues)
	{
		Edits = edits;

		// Issues are copied so the result cannot observe a later caller-side mutation; the empty
		// case shares the cached list because there is nothing to copy.
		Issues = issues.Count == 0 ? s_emptyIssues : Array.AsReadOnly([.. issues]);
	}

	/// <param name="edits">The prepared, validated operations; the batch may be empty when the input contained no effective edits.</param>
	/// <exception cref="ArgumentNullException"><paramref name="edits"/> is <see langword="null"/>.</exception>
	public static TextEditPreparationResult Success(PreparedTextEdits edits)
	{
		ArgumentNullException.ThrowIfNull(edits);
		return new TextEditPreparationResult(edits, []);
	}

	/// <param name="issues">The issues that rejected the batch; must not be empty.</param>
	/// <exception cref="ArgumentNullException"><paramref name="issues"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="issues"/> is empty.</exception>
	public static TextEditPreparationResult Failure(IReadOnlyList<TextEditPreparationIssue> issues)
	{
		ArgumentNullException.ThrowIfNull(issues);

		if (issues.Count == 0)
			throw new ArgumentException("At least one issue is required for a failed result.", nameof(issues));

		return new TextEditPreparationResult(s_emptyEdits, issues);
	}

	/// <summary>The prepared, validated edits; empty when the result failed.</summary>
	public PreparedTextEdits Edits { get; }

	/// <summary>The deterministic preparation issues.</summary>
	/// <remarks>
	/// Invalid-edit issues appear first, in edit order. Conflict issues are ordered by the left operation under the candidate order
	/// and then by the triggering operation.
	/// </remarks>
	public IReadOnlyList<TextEditPreparationIssue> Issues { get; }

	/// <summary>Whether the batch was prepared and is ready to apply.</summary>
	public bool Succeeded => Issues.Count == 0;
}
