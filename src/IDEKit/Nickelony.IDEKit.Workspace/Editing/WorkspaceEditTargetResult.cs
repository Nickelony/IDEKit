using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Editing;

/// <summary>
/// Describes one target's prepared and runtime application state.
/// </summary>
/// <remarks>
/// <see cref="ActualVersion"/> is <see langword="null"/> when no post-application version was
/// observed, which includes <see cref="WorkspaceEditTargetOutcome.Unknown"/> targets and targets
/// that were not applied. <see cref="Failure"/> is populated only when a target-specific failure
/// detail exists: a deterministic rejection, an unknown outcome, or the cascade rejection recorded
/// for a later target of a document whose replacement was not applied. Targets that were not
/// attempted because the application stopped are marked
/// <see cref="WorkspaceEditTargetOutcome.NotApplied"/> without a failure.
/// </remarks>
public sealed record WorkspaceEditTargetResult
{
	// The initializers only satisfy nullable analysis; every construction path assigns the fields
	// through the validating init accessors below.
	private readonly string _targetId = string.Empty;
	private readonly int _preparedOperationCount;

	/// <summary>
	/// Gets the stable target identifier supplied for this target (typically the document's file path).
	/// </summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public required string TargetId
	{
		get => _targetId;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_targetId = value;
		}
	}

	/// <summary>
	/// Gets the target version captured during preflight.
	/// </summary>
	public required long ExpectedVersion { get; init; }

	/// <summary>
	/// Gets the document version observed while applying this target (from the store result or an
	/// earlier target of the same document); <see langword="null"/> when none was observed.
	/// </summary>
	public long? ActualVersion { get; init; }

	/// <summary>
	/// Gets the number of replacement operations the target contributes to the application: one when
	/// its content or format differs from the before state, otherwise zero (a no-op).
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	public required int PreparedOperationCount
	{
		get => _preparedOperationCount;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegative(value);
			_preparedOperationCount = value;
		}
	}

	/// <summary>
	/// Gets the target application state.
	/// </summary>
	public required WorkspaceEditTargetOutcome Outcome { get; init; }

	/// <summary>
	/// Gets the target-specific failure detail, when available.
	/// </summary>
	public WorkspaceOperationFailure? Failure { get; init; }
}
