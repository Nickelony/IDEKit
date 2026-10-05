namespace Nickelony.LanguageServer.Provider;

/// <summary>Tracks host-owned and request-owned leases for a mirrored document.</summary>
/// <remarks>
/// This type is safe for concurrent callers. The lease counts live in one 64-bit word so composite reads such as <see cref="IsIdle"/> observe
/// a single consistent value instead of two independently changing counters.
/// </remarks>
internal sealed class DocumentLeaseCounts
{
	// The open-document count occupies the high 32 bits and the request count the low 32 bits; the counters are
	// updated with interlocked operations on the combined word.
	private long _leaseCounts;

	/// <param name="openLeaseCount">The initial number of open-document leases. Negative values are treated as zero.</param>
	/// <param name="requestLeaseCount">The initial number of request-owned leases. Negative values are treated as zero.</param>
	internal DocumentLeaseCounts(int openLeaseCount = 0, int requestLeaseCount = 0) =>
		_leaseCounts = PackLeaseCounts(Math.Max(0, openLeaseCount), Math.Max(0, requestLeaseCount));

	internal int OpenLeaseCount => (int)(Volatile.Read(ref _leaseCounts) >> 32);

	internal int RequestLeaseCount => (int)(uint)Volatile.Read(ref _leaseCounts);

	internal bool HasOpenLeases => (Volatile.Read(ref _leaseCounts) >> 32) > 0;

	internal bool IsIdle => Volatile.Read(ref _leaseCounts) == 0;

	internal void AcquireOpen() => Interlocked.Add(ref _leaseCounts, 1L << 32);

	internal void AcquireRequest() => Interlocked.Increment(ref _leaseCounts);

	/// <summary>Releases one open-document lease.</summary>
	/// <remarks>
	/// A release with no outstanding open lease is ignored; pre-read <see cref="OpenLeaseCount"/> to observe an imbalance (the read is not atomic
	/// with the release).
	/// </remarks>
	internal void ReleaseOpen() => DecrementIfPositive(openLeaseCountHalf: true);

	/// <summary>Releases one request-owned lease.</summary>
	/// <remarks>
	/// A release with no outstanding request lease is ignored; pre-read <see cref="RequestLeaseCount"/> to observe an imbalance
	/// (the read is not atomic with the release).
	/// </remarks>
	internal void ReleaseRequest() => DecrementIfPositive(openLeaseCountHalf: false);

	/// <summary>Packs the lease counts into the single tracked word.</summary>
	/// <param name="openLeaseCount">The open-document lease count.</param>
	/// <param name="requestLeaseCount">The request-owned lease count.</param>
	/// <returns>The packed lease counts.</returns>
	private static long PackLeaseCounts(int openLeaseCount, int requestLeaseCount) => ((long)openLeaseCount << 32) | (uint)requestLeaseCount;

	/// <summary>Decrements one lease count half when it is still positive.</summary>
	/// <param name="openLeaseCountHalf">Whether the open-document count should be decremented instead of the request count.</param>
	private void DecrementIfPositive(bool openLeaseCountHalf)
	{
		while (true)
		{
			long currentValue = Volatile.Read(ref _leaseCounts);
			int currentHalf = openLeaseCountHalf ? (int)(currentValue >> 32) : (int)(uint)currentValue;

			if (currentHalf == 0)
				return;

			long updatedValue = openLeaseCountHalf ? currentValue - (1L << 32) : currentValue - 1;

			if (Interlocked.CompareExchange(ref _leaseCounts, updatedValue, currentValue) == currentValue)
				return;
		}
	}
}
