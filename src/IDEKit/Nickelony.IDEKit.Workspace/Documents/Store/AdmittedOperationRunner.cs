namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Runs one file operation on a tracked document from its admission to its completion.</summary>
/// <remarks>
/// <para>
/// The runner admits the operation through the <see cref="OperationGate"/>, so a refused request never starts the plan, and it completes
/// the admission on every exit. The plan's own reservations are released first, then the operation and its disk gate, which is the order
/// the gate requires.
/// </para>
/// <para>
/// A plan with <see cref="AdmittedOperationCancellationMode.Linked"/> runs under a token linked to the store lifetime, and its cancellation and fault
/// hooks map an exception to the operation's own result. A plan without a hook lets that exception propagate.
/// </para>
/// </remarks>
internal sealed class AdmittedOperationRunner
{
	private readonly OperationGate _operationGate;
	private readonly StoreLifetime _lifetime;

	/// <param name="operationGate">The gate that admits the operation and releases its registration.</param>
	/// <param name="lifetime">The store lifetime, which a linked plan observes besides the caller's token.</param>
	internal AdmittedOperationRunner(OperationGate operationGate, StoreLifetime lifetime)
	{
		_operationGate = operationGate;
		_lifetime = lifetime;
	}

	/// <summary>Admits the operation and runs the plan's step.</summary>
	/// <typeparam name="TResult">The result type of the operation.</typeparam>
	/// <param name="plan">The steps that differ between operations.</param>
	/// <param name="cancellationToken">A token that can cancel the operation.</param>
	/// <returns>The refusal when the admission failed; otherwise, the result of the step or of the plan's cancellation or fault hook.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal async Task<TResult> RunAsync<TResult>(AdmittedOperationDescriptor<TResult> plan, CancellationToken cancellationToken)
		where TResult : class
	{
		OperationBeginResult begin = _operationGate.Begin(
			identity: plan.Identity,
			captureSnapshot: true,
			preGateCheck: plan.PreGateCheck,
			postGateSetup: plan.PostGateSetup
		);

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot, Operation: { } operation })
			return plan.CreateRefusal(begin);

		try
		{
			if (plan.Cancellation == AdmittedOperationCancellationMode.Caller)
				return await plan.ExecuteAsync(new AdmittedOperation(document, capturedSnapshot), cancellationToken).ConfigureAwait(false);

			using CancellationTokenSource linkedCancellation = _lifetime.CreateLinkedCancellation(cancellationToken);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			return await plan.ExecuteAsync(new AdmittedOperation(document, capturedSnapshot), linkedCancellation.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (plan.CreateCanceled is not null)
		{
			return plan.CreateCanceled(document);
		}
		catch (Exception exception) when (plan.CreateFaulted is not null && exception is not OperationCanceledException)
		{
			return plan.CreateFaulted(document, exception);
		}
		finally
		{
			// The admission completes even when the plan's release throws, because a held disk gate and registration would make a later disposal wait forever.
			try
			{
				plan.Release?.Invoke(document);
			}
			finally
			{
				_operationGate.Complete(operation);
			}
		}
	}
}
