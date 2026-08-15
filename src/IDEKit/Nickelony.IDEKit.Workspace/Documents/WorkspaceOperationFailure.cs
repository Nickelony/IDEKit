namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Describes a failure returned by a workspace document operation.
/// </summary>
/// <remarks>
/// <see cref="Code"/> is a stable category for the failure. <see cref="Exception"/> may contain the
/// underlying exception when one was available; callers should use <see cref="Message"/> for display
/// or logging rather than depending on an exception being present.
/// </remarks>
/// <param name="Code">A stable category for the failure.</param>
/// <param name="Message">A human-readable failure message.</param>
/// <param name="Exception">The underlying exception when one was available; otherwise, <see langword="null"/>.</param>
public sealed record WorkspaceOperationFailure(
	string Code,
	string Message,
	Exception? Exception = null);
