namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>Holds the behavior choices of a <see cref="DocumentReplacementApplier"/>.</summary>
public sealed record DocumentReplacementApplierOptions
{
	/// <summary>The options that stop at the first target that is not applied.</summary>
	public static DocumentReplacementApplierOptions Default { get; } = new();

	/// <summary>
	/// <see langword="true"/> to continue past a rejected or unknown target and aggregate the per-target outcomes, as an LSP client
	/// applies a workspace edit; <see langword="false"/> (the default) to stop at the first target that is not applied and mark the remaining targets
	/// not applied.
	/// </summary>
	/// <remarks>
	/// Later targets for a document whose replacement was not applied are skipped either way, and cancellation still stops the application.
	/// </remarks>
	public bool ContinueOnFailure { get; init; }
}
