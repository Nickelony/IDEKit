namespace Nickelony.LanguageServer.Provider;

/// <summary>Describes one workspace change forwarding failure together with the affected batch context.</summary>
/// <param name="Code">One of the <see cref="WorkspaceChangeForwardingFailureCodes"/> values, which names the cause.</param>
/// <param name="Exception">The underlying forwarding exception.</param>
/// <param name="BatchCount">The number of file changes in the affected batch.</param>
/// <param name="FirstPath">The first affected path, when available.</param>
/// <param name="WasDropped">Whether the batch was dropped instead of buffered for replay.</param>
internal readonly record struct WorkspaceChangeForwardingFailure(
	string Code,
	Exception Exception,
	int BatchCount,
	string? FirstPath,
	bool WasDropped
);
