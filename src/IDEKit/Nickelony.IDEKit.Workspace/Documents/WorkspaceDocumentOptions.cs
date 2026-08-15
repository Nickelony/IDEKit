using Nickelony.IDEKit.Core.Pathing;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Carries the workspace document options that the components comparing document identities share.
/// </summary>
/// <remarks>
/// <para>
/// One value is passed to the document store, the file system, and the reload coordinator, so the
/// path comparison cannot drift between them: a host that runs the store under one comparison and
/// the file system under another sees a case-only rename resolve differently on each side. A
/// component that is handed the store instead of the options reads the resolved policy back through
/// <see cref="IWorkspaceDocumentReader.PathComparison"/>.
/// </para>
/// <para>
/// These are the composition-level options of the layer; the per-call options of a single open are
/// <see cref="WorkspaceDocumentOpenOptions"/>.
/// </para>
/// </remarks>
public sealed record WorkspaceDocumentOptions
{
	/// <summary>
	/// Gets the options value that follows the operating system: case-insensitive on Windows and
	/// macOS, and ordinal on other platforms.
	/// </summary>
	public static WorkspaceDocumentOptions Default { get; } = new();

	/// <summary>
	/// Gets the comparison used for document identity paths. The default follows the operating
	/// system; supply an explicit value when the file system's semantics differ from the operating
	/// system, for example a case-sensitive volume on macOS.
	/// </summary>
	public LocalPathComparisonPolicy PathComparison { get; init; } = LocalPathComparisonPolicy.ForCurrentPlatform;
}
