using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>A validated batch of text edit operations ordered from highest to lowest source offset.</summary>
/// <remarks>
/// The constructor validates the batch once - order, overlaps, and the replacement-before-insertion
/// order at one start offset - so an accepted instance can be applied or mapped without re-validating it. Multiple insertions at one offset
/// are valid and keep their list order. Create batches through <see cref="TextEditKernel.Prepare(ITextSnapshot, IEnumerable{TextIncrementalEdit})"/>
/// or <see cref="TextEditKernel.Prepare(IReadOnlyList{TextEditOperation})"/>, so a rejected batch is reported as a preparation issue
/// rather than thrown.
/// </remarks>
public sealed class PreparedTextEdits
{
	/// <exception cref="ArgumentException">
	/// <paramref name="operations"/> contains a <see langword="null"/> entry, is not ordered from highest to lowest source offset,
	/// contains overlapping operations, or orders an insertion before a replacement that starts at the same source offset.
	/// </exception>
	internal PreparedTextEdits(IReadOnlyList<TextEditOperation> operations)
	{
		// Copy first, so validation and the stored sequence see the same entries even when the
		// caller's list is mutated concurrently.
		TextEditOperation[] copy = [.. operations];

		ValidateOperations(copy);

		Operations = Array.AsReadOnly(copy);
	}

	/// <summary>Creates a batch from an operation array the edit kernel already validated and owns.</summary>
	/// <param name="operations">The kernel-validated operations in application order; the batch takes ownership of the array.</param>
	/// <remarks>
	/// Kernel-validated only: the caller must have validated the same rules as the validating constructor and must not keep or mutate
	/// the array afterwards.
	/// </remarks>
	internal static PreparedTextEdits CreateKernelValidated(TextEditOperation[] operations) => new(operations);

	private PreparedTextEdits(TextEditOperation[] ownedOperations) => Operations = Array.AsReadOnly(ownedOperations);

	/// <summary>The validated operations, ordered from highest to lowest source offset.</summary>
	/// <remarks>
	/// Operations that share a start offset are ordered so that applying the list gives their texts the edit-index order; a hand-built batch
	/// may order them differently, which changes the resulting text correspondingly.
	/// </remarks>
	public IReadOnlyList<TextEditOperation> Operations { get; }

	/// <summary>Applies the operations to a text and returns the edited text.</summary>
	/// <param name="text">The text the operations were prepared against.</param>
	/// <returns>The text with every operation applied.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">An operation range extends beyond the end of <paramref name="text"/>.</exception>
	/// <remarks>
	/// The operations do not overlap and arrive highest offset first, so applying them in order keeps each earlier offset valid.
	/// </remarks>
	public string ApplyTo(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		var builder = new System.Text.StringBuilder(text);

		foreach (TextEditOperation operation in Operations)
		{
			if (operation.Range.EndOffset > text.Length)
				throw new ArgumentOutOfRangeException(nameof(text), "An operation range extends beyond the end of the text.");

			builder.Remove(operation.Range.Offset, operation.Range.Length);
			builder.Insert(operation.Range.Offset, operation.NewText);
		}

		return builder.ToString();
	}

	/// <summary>Maps an offset from the source snapshot to the text after these operations are applied.</summary>
	/// <returns>The corresponding zero-based offset in the edited text.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> is negative.</exception>
	/// <remarks>
	/// An offset inside a replaced range maps to the same relative position within the replacement text, clamped to its end, and an offset
	/// at the range end maps after the replacement; an offset coinciding with an insertion stays before the inserted text. Each call walks
	/// the operations at or below the offset, so use <see cref="MapOffsets(ReadOnlySpan{int})"/> to map several offsets in one pass.
	/// </remarks>
	public int MapOffset(int offset)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);

		int operationIndex = Operations.Count - 1;
		int cumulativeDelta = 0;

		return MapOffsetCore(offset, Operations, ref operationIndex, ref cumulativeDelta);
	}

	/// <summary>
	/// Maps many offsets with one shared pass over the operations, so mapping several offsets over one large batch is cheaper than calling
	/// <see cref="MapOffset(int)"/> repeatedly.
	/// </summary>
	/// <param name="offsets">
	/// The zero-based source offsets to map; values past the end of the source text are accepted and shifted by the total length delta.
	/// </param>
	/// <returns>An array with one mapped offset per input element, in input order.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Any offset is negative.</exception>
	/// <remarks>
	/// Each result equals the <see cref="MapOffset(int)"/> result for the same element, and the walk visits the operations once regardless
	/// of how many offsets are mapped.
	/// </remarks>
	public int[] MapOffsets(ReadOnlySpan<int> offsets)
	{
		if (offsets.Length == 0)
			return [];

		// Validate first, so a negative offset cannot leave the result partially computed.
		for (int index = 0; index < offsets.Length; index++)
			ArgumentOutOfRangeException.ThrowIfNegative(offsets[index]);

		int[] sortedOffsets = offsets.ToArray();
		int[] originalIndexes = new int[sortedOffsets.Length];

		for (int index = 0; index < originalIndexes.Length; index++)
			originalIndexes[index] = index;

		Array.Sort(sortedOffsets, originalIndexes);

		int[] sortedResults = new int[sortedOffsets.Length];

		int operationIndex = Operations.Count - 1;
		int cumulativeDelta = 0;

		for (int index = 0; index < sortedOffsets.Length; index++)
			sortedResults[index] = MapOffsetCore(offset: sortedOffsets[index], Operations, ref operationIndex, ref cumulativeDelta);

		int[] results = new int[sortedOffsets.Length];

		for (int index = 0; index < originalIndexes.Length; index++)
			results[originalIndexes[index]] = sortedResults[index];

		return results;
	}

	/// <summary>
	/// Verifies the input contract (no <see langword="null"/> entry, highest-offset-first order, and the replacement-before-insertion order
	/// at one start offset) and rejects the conflicts found by the shared conflict detector.
	/// </summary>
	/// <remarks>
	/// At one start offset, the list must place a replacement before an insertion, so an insertion listed before a replacement is rejected.
	/// Two insertions at one offset are accepted in list order. Overlaps follow the same rule set as the preparation kernel, so a batch prepared
	/// by the kernel never fails construction.
	/// </remarks>
	private static void ValidateOperations(IReadOnlyList<TextEditOperation> operations)
	{
		TextEditOperation? previousOperation = null;

		foreach (TextEditOperation? operation in operations)
		{
			if (operation is null)
				throw new ArgumentException("The operations must not contain a null entry.", nameof(operations));

			// An operation that changes nothing cannot conflict with another operation or with the
			// required order, so it is skipped here and by the shared conflict detector.
			if (operation.IsNoOp)
				continue;

			if (previousOperation is null)
			{
				previousOperation = operation;
				continue;
			}

			if (operation.Range.Offset > previousOperation.Range.Offset)
				throw new ArgumentException("Operations must be supplied in highest-offset-first order.", nameof(operations));

			if (operation.Range.Offset == previousOperation.Range.Offset && previousOperation.Range.IsEmpty && !operation.Range.IsEmpty)
			{
				// Duplicate insertions are valid and matched by no conflict rule; this check only enforces
				// the carrier's ordering contract - an insertion must not precede a replacement that
				// starts at the same offset.
				throw new ArgumentException("An insertion must not precede a replacement that starts at the same source offset.", nameof(operations));
			}

			previousOperation = operation;
		}

		// Only the first conflict is needed: the batch is rejected either way, and collecting the rest
		// would materialize a quadratic diagnostic list for a pathological overlapping batch.
		foreach (TextEditConflict conflict in TextEditConflictDetector.FindConflicts(operations, maximumConflictCount: 1))
			throw new ArgumentException(TextEditConflictMessages.GetExceptionMessage(conflict.Kind), nameof(operations));
	}

	private static int MapOffsetCore(int offset, IReadOnlyList<TextEditOperation> operations, ref int operationIndex, ref int cumulativeDelta)
	{
		while (operationIndex >= 0)
		{
			TextEditOperation operation = operations[operationIndex];
			TextRange range = operation.Range;

			if (range.Offset > offset)
				break;

			if (offset == range.Offset || offset < range.EndOffset)
			{
				int relativeOffset = offset - range.Offset;
				int normalizedRelativeOffset = Math.Min(relativeOffset, operation.NewText.Length);

				return range.Offset + cumulativeDelta + normalizedRelativeOffset;
			}

			cumulativeDelta += operation.NewText.Length - range.Length;
			operationIndex--;
		}

		return offset + cumulativeDelta;
	}
}
