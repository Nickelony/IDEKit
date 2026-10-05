namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Counts the nested delete leases of a text view and makes its editor read-only while one is held.</summary>
/// <param name="getReadOnly">Reads the read-only value of the editor.</param>
/// <param name="setReadOnly">Writes the read-only value of the editor.</param>
/// <remarks>
/// <para>
/// The first entry saves the read-only value and sets it, and the release to depth zero restores the saved value.
/// A release always counts down, because the coordinator never retries one: a restore that throws stays pending, and the next release to depth zero
/// or a close retries it.
/// </para>
/// <para>
/// An entry while a restore is pending keeps the saved value, because the editor still shows the read-only state the lease set.
/// </para>
/// </remarks>
internal sealed class DeleteLeaseCounter(Func<bool> getReadOnly, Action<bool> setReadOnly)
{
	private int _depth;

	private bool _savedReadOnly;
	private bool _isRestorePending;

	/// <summary>Enters one lease, saving and setting the read-only value on the first entry.</summary>
	internal WorkspaceDocumentViewDeleteLeaseResult Enter()
	{
		if (_depth == 0 && !_isRestorePending)
		{
			try
			{
				bool savedReadOnly = getReadOnly();
				setReadOnly(true);
				_savedReadOnly = savedReadOnly;
			}
			catch (Exception exception)
			{
				return Failed(exception);
			}
		}

		_depth++;
		return new WorkspaceDocumentViewDeleteLeaseResult(WorkspaceDocumentViewDeleteLeaseOutcome.Entered);
	}

	/// <summary>Releases one lease, restoring the saved read-only value on the release to depth zero.</summary>
	/// <remarks>
	/// A release with none held reports <see cref="WorkspaceDocumentViewDeleteLeaseOutcome.Released"/> and touches nothing.
	/// </remarks>
	internal WorkspaceDocumentViewDeleteLeaseResult Release()
	{
		if (_depth == 0)
			return new WorkspaceDocumentViewDeleteLeaseResult(WorkspaceDocumentViewDeleteLeaseOutcome.Released);

		_depth--;

		if (_depth == 0)
		{
			_isRestorePending = true;

			if (Restore() is { } exception)
				return Failed(exception);
		}

		return new WorkspaceDocumentViewDeleteLeaseResult(WorkspaceDocumentViewDeleteLeaseOutcome.Released);
	}

	/// <summary>Retries a pending restore while no lease is held, as a close does.</summary>
	/// <returns>The exception of a restore that failed again, or <see langword="null"/>.</returns>
	internal Exception? RetryRestore() => _depth == 0 && _isRestorePending ? Restore() : null;

	private static WorkspaceDocumentViewDeleteLeaseResult Failed(Exception exception)
	{
		return new(
			WorkspaceDocumentViewDeleteLeaseOutcome.Failed,
			new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, exception.Message, exception)
		);
	}

	// Writes the saved value and clears the pending restore, or keeps it pending and returns the exception when the editor throws.
	private Exception? Restore()
	{
		try
		{
			setReadOnly(_savedReadOnly);
			_isRestorePending = false;
			return null;
		}
		catch (Exception exception)
		{
			return exception;
		}
	}
}
