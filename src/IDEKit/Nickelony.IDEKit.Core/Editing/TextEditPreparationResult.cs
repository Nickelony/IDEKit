namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Contains prepared edits and deterministic issues for one edit batch.
/// </summary>
/// <remarks>
/// A result is either valid - no issues, editable operations ready to apply - or rejected -
/// issues and no operations. Create results through <see cref="Valid"/> and <see cref="Invalid"/>
/// so the contradictory combination cannot be represented. <see cref="Edits"/> is a
/// <see cref="PreparedTextEdits"/> batch, so it is already ordered and validated at construction;
/// apply or map it without re-validating.
/// </remarks>
public sealed class TextEditPreparationResult
{
	// A rejected batch has no operations, so every rejected result shares one immutable empty batch
	// instead of allocating a fresh carrier per rejection.
	private static readonly PreparedTextEdits s_emptyEdits = new([]);

	// A valid result carries no issues, so every valid result shares one immutable empty list
	// instead of allocating a wrapper per preparation.
	private static readonly IReadOnlyList<TextEditPreparationIssue> s_emptyIssues =
		Array.AsReadOnly(Array.Empty<TextEditPreparationIssue>());

	private TextEditPreparationResult(
		PreparedTextEdits edits,
		IReadOnlyList<TextEditPreparationIssue> issues)
	{
		Edits = edits;

		// Issues are copied so the result cannot observe a later caller-side mutation; the empty
		// case shares the cached list because there is nothing to copy.
		Issues = issues.Count == 0 ? s_emptyIssues : Array.AsReadOnly([.. issues]);
	}

	/// <summary>
	/// Creates a result for a valid edit batch.
	/// </summary>
	/// <param name="edits">The prepared, validated operations; the batch may be empty when the input contained no effective edits.</param>
	/// <returns>A valid result without issues.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="edits"/> is <see langword="null"/>.</exception>
	public static TextEditPreparationResult Valid(PreparedTextEdits edits)
	{
		ArgumentNullException.ThrowIfNull(edits);

		return new TextEditPreparationResult(edits, []);
	}

	/// <summary>
	/// Creates a result for a rejected edit batch.
	/// </summary>
	/// <param name="issues">The issues that rejected the batch; must not be empty.</param>
	/// <returns>A rejected result without operations.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="issues"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="issues"/> is empty.</exception>
	public static TextEditPreparationResult Invalid(IReadOnlyList<TextEditPreparationIssue> issues)
	{
		ArgumentNullException.ThrowIfNull(issues);

		if (issues.Count == 0)
			throw new ArgumentException("At least one issue is required for a rejected batch.", nameof(issues));

		return new TextEditPreparationResult(s_emptyEdits, issues);
	}

	/// <summary>
	/// Gets the prepared, validated edits. Empty when the batch was rejected.
	/// </summary>
	public PreparedTextEdits Edits { get; }

	/// <summary>
	/// Gets the deterministic preparation issues.
	/// </summary>
	/// <remarks>
	/// Issues for invalid edits appear first in edit order. Conflict issues follow a
	/// deterministic order: pairwise range conflicts are ordered by the left operation under the
	/// candidate order and then by the triggering operation. The collection is deterministic for a
	/// given input.
	/// </remarks>
	public IReadOnlyList<TextEditPreparationIssue> Issues { get; }

	/// <summary>
	/// Gets a value indicating whether the edit batch is valid and ready to be applied.
	/// </summary>
	public bool IsValid => Issues.Count == 0;
}
