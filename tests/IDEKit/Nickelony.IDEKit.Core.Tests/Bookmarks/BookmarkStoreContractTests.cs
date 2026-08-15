namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the <see cref="IBookmarkStore"/> contract with a host-bridge reference implementation: a
/// save/load round trip over normalized one-based line numbers, an empty result that is distinct
/// from a failed load, and the documented null-argument guards.
/// </summary>
[TestClass]
public sealed class BookmarkStoreContractTests
{
	[TestMethod]
	public void TrySave_ThenTryLoad_RoundTripsTheNumbers()
	{
		IBookmarkStore store = new ReferenceBookmarkStore();

		Assert.IsTrue(store.TrySave("script.lua", [5, 1, 5]));
		Assert.IsTrue(store.TryLoad("script.lua", out IReadOnlyList<int> restored));
		CollectionAssert.AreEqual(new[] { 1, 5 }, restored.ToArray());
	}

	[TestMethod]
	public void TryLoad_NothingStored_ReportsSuccessWithAnEmptyList()
	{
		IBookmarkStore store = new ReferenceBookmarkStore();

		// "Nothing was stored" is a successful load with an empty list, not a failed one, so a caller
		// can tell it apart from a storage failure.
		Assert.IsTrue(store.TryLoad("script.lua", out IReadOnlyList<int> restored));
		Assert.IsNotNull(restored);
		Assert.AreEqual(0, restored.Count);
	}

	[TestMethod]
	public void TryLoad_FailedLoad_ReportsFalseAndStaysDistinctFromAnEmptyResult()
	{
		var store = new ReferenceBookmarkStore();
		store.FailLoad("script.lua");

		Assert.IsFalse(store.TryLoad("script.lua", out IReadOnlyList<int> restored));
		Assert.IsNotNull(restored);
	}

	[TestMethod]
	public void TrySave_NullArguments_Throw()
	{
		IBookmarkStore store = new ReferenceBookmarkStore();

		Assert.ThrowsExactly<ArgumentNullException>(() => store.TrySave(null!, [1]));
		Assert.ThrowsExactly<ArgumentNullException>(() => store.TrySave("script.lua", null!));
	}

	[TestMethod]
	public void TryLoad_NullFilePath_Throws()
	{
		IBookmarkStore store = new ReferenceBookmarkStore();

		Assert.ThrowsExactly<ArgumentNullException>(() => store.TryLoad(null!, out _));
	}

	// A reference implementation backed by an in-memory dictionary, so the interface contract is
	// exercised without any file I/O - the concrete BookmarkSidecarStore covers the persistence.
	private sealed class ReferenceBookmarkStore : IBookmarkStore
	{
		private readonly Dictionary<string, IReadOnlyList<int>> _stored = new(StringComparer.Ordinal);
		private readonly HashSet<string> _failingLoads = new(StringComparer.Ordinal);

		public void FailLoad(string filePath) => _failingLoads.Add(filePath);

		public bool TrySave(string filePath, IReadOnlyList<int> bookmarkLineNumbers)
		{
			ArgumentNullException.ThrowIfNull(filePath);
			ArgumentNullException.ThrowIfNull(bookmarkLineNumbers);

			// The contract stores one-based, ascending, de-duplicated numbers; an empty set clears the
			// stored entry.
			_stored[filePath] = bookmarkLineNumbers
				.Where(line => line >= 1)
				.Distinct()
				.Order()
				.ToArray();

			return true;
		}

		public bool TryLoad(string filePath, out IReadOnlyList<int> bookmarkLineNumbers)
		{
			ArgumentNullException.ThrowIfNull(filePath);

			if (_failingLoads.Contains(filePath))
			{
				bookmarkLineNumbers = Array.Empty<int>();
				return false;
			}

			bookmarkLineNumbers = _stored.TryGetValue(filePath, out IReadOnlyList<int>? stored)
				? stored
				: Array.Empty<int>();

			return true;
		}
	}
}
