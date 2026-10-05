namespace Nickelony.IDEKit.Workspace;

/// <summary>Reports that the process cannot resolve an encoding, typically because no code-page provider is registered.</summary>
/// <remarks>
/// A <see cref="NotSupportedException"/> for a caller of the public members. The store maps
/// it to the <see cref="WorkspaceOperationFailureCodes.EncodingUnavailable"/> failure code.
/// </remarks>
internal sealed class EncodingUnavailableException : NotSupportedException
{
	internal EncodingUnavailableException(string message, Exception innerException)
		: base(message, innerException) { }
}
