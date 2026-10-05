namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>Lists the stable failure codes that <see cref="DocumentReplacementApplier"/> reports for a target.</summary>
/// <remarks>
/// <see cref="WorkspaceOperationFailure.Code"/> carries one of these values for a target failure. The codes of the document authority
/// are in <see cref="WorkspaceOperationFailureCodes"/>.
/// </remarks>
public static class DocumentReplacementFailureCodes
{
	/// <summary>A prepared target was rejected without the document being mutated.</summary>
	public const string TargetNotChanged = "TargetNotChanged";

	/// <summary>A prepared target was not attempted because an earlier target for the same document was rejected.</summary>
	public const string TargetSkipped = "TargetSkipped";

	/// <summary>
	/// A prepared target returned a replacement outcome outside the document authority's vocabulary, so its final state is unknown.
	/// </summary>
	public const string TargetOutcomeUnknown = "TargetOutcomeUnknown";

	/// <summary>A prepared target's replacement threw, so its final state is unknown.</summary>
	public const string TargetApplicationFailed = "TargetApplicationFailed";
}
