namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>The steps of one admitted operation that differ from the others.</summary>
/// <typeparam name="TResult">The result type of the operation.</typeparam>
internal sealed class AdmittedOperationDescriptor<TResult>
	where TResult : class
{
	/// <summary>The document instance and version the request was made against.</summary>
	internal required WorkspaceDocumentRequestIdentity Identity { get; init; }

	/// <summary>Stops the operation before the disk gate is taken, such as a rename to the document's own path.</summary>
	internal Func<LogicalDocument, OperationPreGateOutcome>? PreGateCheck { get; init; }

	/// <summary>Runs under the store lock once the gate is held, to publish the operation's reservation.</summary>
	internal Action<LogicalDocument>? PostGateSetup { get; init; }

	/// <summary>Selects the token that <see cref="ExecuteAsync"/> receives.</summary>
	internal AdmittedOperationCancellationMode Cancellation { get; init; } = AdmittedOperationCancellationMode.Linked;

	/// <summary>Builds the result for a request the admission refused.</summary>
	internal required Func<OperationBeginResult, TResult> CreateRefusal { get; init; }

	/// <summary>Runs the operation's file-system step and tracking update and returns its result.</summary>
	internal required Func<AdmittedOperation, CancellationToken, Task<TResult>> ExecuteAsync { get; init; }

	/// <summary>Builds the result for a canceled step; without it, the cancellation propagates.</summary>
	internal Func<LogicalDocument, TResult>? CreateCanceled { get; init; }

	/// <summary>Builds the result for a faulted step; without it, the exception propagates.</summary>
	internal Func<LogicalDocument, Exception, TResult>? CreateFaulted { get; init; }

	/// <summary>Releases the reservations the operation published, before its registration completes.</summary>
	internal Action<LogicalDocument>? Release { get; init; }
}

/// <summary>Selects the token that an admitted operation's step runs under.</summary>
internal enum AdmittedOperationCancellationMode
{
	/// <summary>The step receives a token linked to the store lifetime, and a canceled token ends it before it starts.</summary>
	Linked,

	/// <summary>The step receives the caller's token and links the lifetime itself where it needs to.</summary>
	Caller,
}

/// <summary>The document an operation was admitted for, with the snapshot captured at admission.</summary>
/// <param name="Document">The tracked document the operation holds.</param>
/// <param name="CapturedSnapshot">The snapshot taken under the store lock when the operation was admitted.</param>
internal readonly record struct AdmittedOperation(LogicalDocument Document, WorkspaceDocumentSnapshot CapturedSnapshot);
