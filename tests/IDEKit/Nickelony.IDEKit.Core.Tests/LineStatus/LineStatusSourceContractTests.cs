namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the <see cref="ILineStatusSource"/> contract with a reference implementation: one-based,
/// ascending, de-duplicated line numbers; an empty list instead of <see langword="null"/> when
/// nothing is marked; and a result that is cached across the consumer's read passes.
/// </summary>
[TestClass]
public sealed class LineStatusSourceContractTests
{
	[TestMethod]
	public void GetMarkedLineNumbers_ReturnsOneBasedAscendingNumbers()
	{
		ILineStatusSource source = new ReferenceLineStatusSource(5, 1, 3);

		IReadOnlyList<int> marked = source.GetMarkedLineNumbers();

		CollectionAssert.AreEqual(new[] { 1, 3, 5 }, marked.ToArray());
		Assert.IsTrue(marked.All(line => line >= 1), "Marked lines are one-based.");
	}

	[TestMethod]
	public void GetMarkedLineNumbers_NothingMarked_ReturnsAnEmptyListNotNull()
	{
		ILineStatusSource source = new ReferenceLineStatusSource();

		// A source that cannot produce lines returns an empty list rather than null, because the
		// consumer walks the result directly.
		IReadOnlyList<int> marked = source.GetMarkedLineNumbers();

		Assert.IsNotNull(marked);
		Assert.AreEqual(0, marked.Count);
	}

	[TestMethod]
	public void GetMarkedLineNumbers_RepeatedReads_ComputeOnce()
	{
		var source = new ReferenceLineStatusSource(2, 4);

		CollectionAssert.AreEqual(new[] { 2, 4 }, source.GetMarkedLineNumbers().ToArray());
		CollectionAssert.AreEqual(new[] { 2, 4 }, source.GetMarkedLineNumbers().ToArray());

		// The consumer queries the source on every read pass, so the contract requires the source to
		// cache its result and recompute it only when its inputs actually change.
		Assert.AreEqual(1, source.Computations, "The source caches its result across reads.");
	}

	// A reference implementation that normalizes its inputs to the documented shape once and reuses
	// the normalized result for every read.
	private sealed class ReferenceLineStatusSource : ILineStatusSource
	{
		private readonly int[] _input;
		private IReadOnlyList<int>? _cache;

		public ReferenceLineStatusSource(params int[] markedLines) => _input = markedLines;

		public int Computations { get; private set; }

		public IReadOnlyList<int> GetMarkedLineNumbers() => _cache ??= Compute();

		private IReadOnlyList<int> Compute()
		{
			Computations++;

			return Array.AsReadOnly(_input.Where(line => line >= 1).Distinct().Order().ToArray());
		}
	}
}
