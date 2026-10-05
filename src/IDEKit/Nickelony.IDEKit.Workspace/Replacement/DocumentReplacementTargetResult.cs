namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>Describes one target's prepared and runtime application state.</summary>
/// <remarks>
/// <see cref="ActualVersion"/> is <see langword="null"/> when no post-application version was observed: an unknown outcome,
/// a target that was not applied, or one that was not attempted. <see cref="Failure"/> is set only for a deterministic rejection, an unknown outcome,
/// or the cascade rejection of a later target of a document whose replacement was not applied; a target that was not attempted carries no failure.
/// </remarks>
public sealed record DocumentReplacementTargetResult
{
	// The initializers only satisfy nullable analysis; every construction path assigns the fields
	// through the validating init accessors below.
	private readonly string _documentId = string.Empty;
	private readonly int _preparedOperationCount;

	/// <summary>The identifier of the document this result belongs to, as the host supplied it, typically its file path.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public required string DocumentId
	{
		get => _documentId;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_documentId = value;
		}
	}

	/// <summary>The target version captured during preflight.</summary>
	public required long ExpectedVersion { get; init; }

	/// <summary>The document version observed while applying this target, from the store result or an earlier target of the same document.</summary>
	public long? ActualVersion { get; init; }

	/// <summary>
	/// The number of replacement operations the target contributes: one when its content or format differs from the before state, otherwise zero.
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

	public required DocumentReplacementTargetOutcome Outcome { get; init; }

	/// <summary>The target-specific failure detail, when available.</summary>
	public WorkspaceOperationFailure? Failure { get; init; }
}
