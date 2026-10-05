namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Rejects scheduler calls issued from inside a queued scheduler operation, so a queued delegate cannot deadlock against its own lane.
/// </summary>
/// <remarks>
/// The gate publishes the active operation in an <see cref="AsyncLocal{T}"/> for the duration of a queued delegate, so the check follows the flow
/// of the delegate through its awaits. Work that must outlive the operation starts inside <see cref="RunDetached"/>, which hides the operation
/// from it.
/// </remarks>
internal sealed class OperationReentrancyGate
{
	private readonly AsyncLocal<ActiveDocumentContext?> _activeDocumentContext = new();

	/// <summary>Runs an operation while publishing the active-operation context so nested scheduler calls are rejected.</summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="context">The active-operation context to publish.</param>
	/// <param name="operation">The operation to run.</param>
	/// <returns>A task that completes with the operation result.</returns>
	internal async Task<TResult> RunAsync<TResult>(ActiveDocumentContext context, Func<Task<TResult>> operation)
	{
		// This async method runs in its own copy of the execution context, which the runtime discards when it completes, so the
		// caller needs no restore of the context that is published here.
		_activeDocumentContext.Value = context;

		try
		{
			return await operation().ConfigureAwait(false);
		}
		finally
		{
			context.IsRunning = false;
		}
	}

	/// <summary>
	/// Runs one action with the flow-local active-operation context detached, so work the action starts is not treated as nested scheduler work.
	/// </summary>
	/// <param name="action">The action that synchronously starts the detached work.</param>
	/// <remarks>
	/// Only the synchronous start of <paramref name="action"/> runs inside the detached scope (that is where an async state machine or a queued
	/// <see cref="Task.Run(Action)"/> captures its context), so work the action starts observes no active-operation scope. The originating delegate
	/// must not await the detached work.
	/// </remarks>
	internal void RunDetached(Action action)
	{
		ActiveDocumentContext? previousContext = _activeDocumentContext.Value;

		// A call from outside a queued operation has no context to detach.
		if (previousContext is null)
		{
			action();
			return;
		}

		_activeDocumentContext.Value = null;

		try
		{
			action();
		}
		finally
		{
			_activeDocumentContext.Value = previousContext;
		}
	}

	/// <summary>Rejects an enqueue issued from inside a queued scheduler operation.</summary>
	/// <param name="memberName">The calling member name, used in the exception message.</param>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation, so awaiting new scheduler work could deadlock a lane.
	/// </exception>
	internal void EnsureNotInside(string memberName)
	{
		ActiveDocumentContext? context = _activeDocumentContext.Value;

		if (context is null || !context.IsRunning)
			return;

		throw new InvalidOperationException(
			$"'{memberName}' was called from inside a queued scheduler operation{context.DescribePaths()}. "
				+ "Queued delegates must not enqueue or wait for scheduler work; await the operation from the caller and queue follow-up work afterwards, "
				+ "or inline the nested work into the operation delegate instead."
		);
	}

	/// <summary>
	/// Tracks the document paths whose lane is currently executing an operation so that re-entrant scheduler calls can be rejected
	/// instead of deadlocking.
	/// </summary>
	/// <param name="normalizedFilePaths">The distinct normalized document paths whose operation is executing.</param>
	internal sealed class ActiveDocumentContext(string[] normalizedFilePaths)
	{
		private int _isRunning = 1;

		/// <summary>Whether the operation is still executing.</summary>
		/// <remarks>
		/// The operation's continuation clears the flag on the executing thread while another thread may probe the context, so the flag is accessed
		/// with <see cref="Volatile"/> semantics.
		/// </remarks>
		internal bool IsRunning
		{
			get => Volatile.Read(ref _isRunning) != 0;
			set => Volatile.Write(ref _isRunning, value ? 1 : 0);
		}

		/// <summary>Describes the document paths whose operation is executing for error messages.</summary>
		/// <returns>A suffix naming the affected paths.</returns>
		internal string DescribePaths()
		{
			return normalizedFilePaths.Length switch
			{
				1 => $" for '{normalizedFilePaths[0]}'",
				_ => $" for '{string.Join("' and '", normalizedFilePaths)}'",
			};
		}
	}
}
