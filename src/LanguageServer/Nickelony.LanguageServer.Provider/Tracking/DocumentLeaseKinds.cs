namespace Nickelony.LanguageServer.Provider;

/// <summary>Selects which leases a <see cref="DocumentTracker.SynchronizeNormalized"/> call acquires for the document.</summary>
/// <remarks>
/// The values compose, so one call can acquire an open-document lease, a temporary request-driven lease, or both. <see cref="None"/> still mirrors
/// content and may create an idle server-open record, but acquires no lease that keeps the record tracked.
/// </remarks>
[Flags]
internal enum DocumentLeaseKinds
{
	/// <summary>Acquires no lease; the synchronization only mirrors content into the tracked state.</summary>
	None = 0,

	/// <summary>Acquires an open-document lease, which keeps the record tracked until it is released.</summary>
	Open = 1,

	/// <summary>Acquires a temporary request-driven lease, which keeps the record tracked until it is released.</summary>
	Request = 2,
}
