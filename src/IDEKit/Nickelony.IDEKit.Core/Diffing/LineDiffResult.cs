using System.Collections.Frozen;

namespace Nickelony.IDEKit.Core.Diffing;

/// <summary>Describes the changed lines between two line-split texts.</summary>
/// <remarks>
/// <see cref="ChangedLineNumbers"/> is an immutable frozen set with unspecified enumeration order, unaffected by later changes to the input lists.
/// Equality has value semantics, so a host can cache the last result and skip re-rendering when a diff repeats. The result is a marker diff:
/// it reports which current lines changed, not hunks with added/removed classification.
/// </remarks>
public sealed record LineDiffResult
{
	private LineDiffResult(IReadOnlySet<int> changedLineNumbers, bool isApproximate)
	{
		ChangedLineNumbers = changedLineNumbers;
		IsApproximate = isApproximate;
	}

	/// <param name="changedLineNumbers">
	/// The one-based line numbers in the current text that were inserted or modified relative to the baseline.
	/// </param>
	/// <param name="isApproximate">
	/// <see langword="true"/> when the bounded search gave up - its work budget or recursion-depth bound was exhausted, or it could not make progress
	/// on a split - and reported the whole affected section as changed; otherwise, <see langword="false"/>. An approximate result still contains
	/// every changed line (it is a superset), and can additionally contain lines that did not change.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="changedLineNumbers"/> is <see langword="null"/>.</exception>
	public static LineDiffResult Create(IReadOnlySet<int> changedLineNumbers, bool isApproximate)
	{
		ArgumentNullException.ThrowIfNull(changedLineNumbers);
		return new LineDiffResult(changedLineNumbers.ToFrozenSet(), isApproximate);
	}

	/// <summary>The one-based line numbers in the current text that were inserted or modified relative to the baseline.</summary>
	public IReadOnlySet<int> ChangedLineNumbers { get; }

	/// <summary>Whether the bounded search gave up and reported the whole affected section as changed instead of the exact minimal result.</summary>
	public bool IsApproximate { get; }

	/// <summary>Determines whether the other result carries the same changed lines and the same approximation flag.</summary>
	/// <remarks>
	/// The set member is compared with <see cref="IReadOnlySet{T}.SetEquals"/>,
	/// because <see cref="System.Collections.Frozen.FrozenSet{T}"/> (the production representation) does not implement value equality,
	/// so the record's compiler-generated comparison would compare by reference.
	/// </remarks>
	public bool Equals(LineDiffResult? other)
	{
		return other is not null
			&& IsApproximate == other.IsApproximate
			&& ChangedLineNumbers.Count == other.ChangedLineNumbers.Count
			&& ChangedLineNumbers.SetEquals(other.ChangedLineNumbers);
	}

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		// The seed folds in the count so that sets with different cardinalities - for example {1, 2}
		// and {3}, whose members XOR to the same value - do not collide. The XOR over the members
		// stays order-independent, because FrozenSet enumeration order is not guaranteed and the
		// set-based equality must keep matching the hash.
		int hash = HashCode.Combine(IsApproximate, ChangedLineNumbers.Count);

		foreach (int lineNumber in ChangedLineNumbers)
			hash ^= lineNumber;

		return hash;
	}
}
