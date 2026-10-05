namespace Nickelony.IDEKit.Workspace;

/// <summary>Describes a failure returned by a workspace document operation.</summary>
/// <param name="Code">A stable category for the failure.</param>
/// <param name="Message">A human-readable failure message.</param>
/// <param name="Exception">The underlying exception when one was available; otherwise, <see langword="null"/>.</param>
/// <remarks>
/// Use <see cref="Message"/> for display or logging rather than depending on <see cref="Exception"/> being present.
/// </remarks>
public sealed record WorkspaceOperationFailure(string Code, string Message, Exception? Exception = null);
