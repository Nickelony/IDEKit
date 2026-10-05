using Nickelony.IDEKit.Core.Paths;

namespace Nickelony.IDEKit.Workspace;

/// <summary>Carries the workspace document options that the components comparing document identities share.</summary>
/// <remarks>
/// One value is shared by every component that resolves document identities, so the path comparison cannot drift
/// between them - a case-only rename would otherwise resolve differently on each side. A component handed the store instead reads the resolved
/// policy through <c>IWorkspaceDocumentReader.Options</c>. The per-call options of a single open are <c>WorkspaceDocumentOpenOptions</c>.
/// </remarks>
public sealed record WorkspaceOptions
{
	public static WorkspaceOptions Default { get; } = new();

	/// <summary>The comparison used for document identity paths.</summary>
	/// <remarks>
	/// Defaults to the operating system's comparison; supply an explicit value when the file system's semantics differ, for example a case-sensitive
	/// volume on macOS.
	/// </remarks>
	public LocalPathComparisonPolicy PathComparison { get; init; } = LocalPathComparisonPolicy.ForCurrentPlatform;
}
