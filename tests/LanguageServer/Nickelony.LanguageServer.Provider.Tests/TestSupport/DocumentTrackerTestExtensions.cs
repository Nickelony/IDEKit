namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Synchronizes a document by its local file path, normalizing it the way the provider does before a path reaches the tracker.</summary>
internal static class DocumentTrackerTestExtensions
{
	internal static DocumentSynchronizationDirective? Synchronize(
		this DocumentTracker tracker,
		string filePath,
		string content,
		DocumentLeaseKinds leases = DocumentLeaseKinds.None,
		bool includeChangeRange = true,
		DocumentRequestLease? requestLease = null
	)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
		return tracker.SynchronizeNormalized(normalizedFilePath: LocalPath.Normalize(filePath), content, leases, includeChangeRange, requestLease);
	}
}
