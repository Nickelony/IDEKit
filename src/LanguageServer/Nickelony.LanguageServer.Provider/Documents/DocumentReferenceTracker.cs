namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Tracks host-owned and request-owned references for a mirrored document.
/// </summary>
/// <remarks>
/// This type is safe for concurrent callers. Both counts live in one 64-bit word so composite reads such as
/// <see cref="IsIdle"/> observe a single consistent value instead of two independently changing counters.
/// The mutators are internal on purpose: only the tracked-document store changes ownership, so external callers
/// cannot corrupt the store's eviction and close accounting, while the counts stay publicly readable.
/// </remarks>
public sealed class DocumentReferenceTracker
{
	// The open-document count occupies the high 32 bits and the request count the low 32 bits; both counters are
	// updated with interlocked operations on the combined word.
	private long _referenceCounts;

	/// <summary>
	/// Initializes a new instance of the <see cref="DocumentReferenceTracker"/> class.
	/// </summary>
	/// <param name="openReferenceCount">The initial number of open-document references. Negative values are treated as zero.</param>
	/// <param name="requestReferenceCount">The initial number of request-owned references. Negative values are treated as zero.</param>
	public DocumentReferenceTracker(int openReferenceCount = 0, int requestReferenceCount = 0)
		=> _referenceCounts = PackReferenceCounts(Math.Max(0, openReferenceCount), Math.Max(0, requestReferenceCount));

	/// <summary>
	/// Gets the number of open-document references.
	/// </summary>
	public int OpenReferenceCount => (int)(Volatile.Read(ref _referenceCounts) >> 32);

	/// <summary>
	/// Gets the number of request-owned references.
	/// </summary>
	public int RequestReferenceCount => (int)(uint)Volatile.Read(ref _referenceCounts);

	/// <summary>
	/// Gets a value indicating whether at least one open-document reference is active.
	/// </summary>
	public bool HasOpenReferences => (Volatile.Read(ref _referenceCounts) >> 32) > 0;

	/// <summary>
	/// Gets a value indicating whether no references remain.
	/// </summary>
	public bool IsIdle => Volatile.Read(ref _referenceCounts) == 0;

	/// <summary>
	/// Adds one open-document reference.
	/// </summary>
	internal void AcquireOpen()
		=> Interlocked.Add(ref _referenceCounts, 1L << 32);

	/// <summary>
	/// Adds one request-owned reference.
	/// </summary>
	internal void AcquireRequest()
		=> Interlocked.Increment(ref _referenceCounts);

	/// <summary>
	/// Releases one open-document reference. A release with no outstanding open reference is ignored; pre-read
	/// <see cref="OpenReferenceCount"/> to observe an imbalance (the read is not atomic with the release).
	/// </summary>
	internal void ReleaseOpen()
		=> DecrementIfPositive(openReferenceCountHalf: true);

	/// <summary>
	/// Releases one request-owned reference. A release with no outstanding request reference is ignored; pre-read
	/// <see cref="RequestReferenceCount"/> to observe an imbalance (the read is not atomic with the release).
	/// </summary>
	internal void ReleaseRequest()
		=> DecrementIfPositive(openReferenceCountHalf: false);

	/// <summary>
	/// Packs both reference counts into the single tracked word.
	/// </summary>
	/// <param name="openReferenceCount">The open-document reference count.</param>
	/// <param name="requestReferenceCount">The request-owned reference count.</param>
	/// <returns>The packed reference counts.</returns>
	private static long PackReferenceCounts(int openReferenceCount, int requestReferenceCount)
		=> ((long)openReferenceCount << 32) | (uint)requestReferenceCount;

	/// <summary>
	/// Decrements one reference count half when it is still positive.
	/// </summary>
	/// <param name="openReferenceCountHalf">Whether the open-document count should be decremented instead of the request count.</param>
	private void DecrementIfPositive(bool openReferenceCountHalf)
	{
		while (true)
		{
			long currentValue = Volatile.Read(ref _referenceCounts);
			int currentHalf = openReferenceCountHalf ? (int)(currentValue >> 32) : (int)(uint)currentValue;

			if (currentHalf == 0)
				return;

			long updatedValue = openReferenceCountHalf
				? currentValue - (1L << 32)
				: currentValue - 1;

			if (Interlocked.CompareExchange(ref _referenceCounts, updatedValue, currentValue) == currentValue)
				return;
		}
	}
}
