namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Covers the stop gate's admission, completion, memoized stop and refusal of a stop from inside an operation.</summary>
[TestClass]
public sealed class StopGateTests
{
	private readonly object _syncRoot = new();

	[TestMethod]
	public void Admit_TracksTheOperationUntilItCompletes()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Task operation = NewOperation();
		IReadOnlyList<Task> inFlight;

		AdmitFromAnotherFlow(gate, operation);

		lock (_syncRoot)
			gate.TryBeginStop(out _, out inFlight);

		Assert.HasCount(1, inFlight);
		Assert.AreSame(operation, inFlight[0]);
	}

	[TestMethod]
	public void Complete_RemovesTheOperationFromTheStopSnapshot()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Task operation = Task.CompletedTask;
		IReadOnlyList<Task> inFlight;

		lock (_syncRoot)
		{
			gate.Admit(operation, owner: this);
			gate.Complete(operation);
			gate.TryBeginStop(out _, out inFlight);
		}

		Assert.IsEmpty(inFlight);
	}

	[TestMethod]
	public void Admit_AfterTheStopBegan_ThrowsNamingTheOwnerAndTracksNothing()
	{
		var gate = new StopGate<Task>(_syncRoot);
		IReadOnlyList<Task> inFlight;

		lock (_syncRoot)
		{
			gate.TryBeginStop(out _, out _);
			gate.RecordStopTask(Task.CompletedTask);

			ObjectDisposedException exception = Assert.ThrowsExactly<ObjectDisposedException>(() => gate.Admit(Task.CompletedTask, owner: this));

			Assert.Contains(nameof(StopGateTests), exception.ObjectName ?? string.Empty);

			gate.TryBeginStop(out _, out inFlight);
		}

		Assert.IsEmpty(inFlight);
	}

	[TestMethod]
	public void TryBeginStop_WhenTheFirstStopRecordedNoTask_ThrowsInsteadOfStartingASecondStop()
	{
		var gate = new StopGate<Task>(_syncRoot);

		lock (_syncRoot)
		{
			gate.TryBeginStop(out _, out _);
			Assert.ThrowsExactly<InvalidOperationException>(() => gate.TryBeginStop(out _, out _));
		}
	}

	[TestMethod]
	public void TryBeginStop_FirstCall_BeginsTheStopAndReturnsTheInFlightOperations()
	{
		var gate = new StopGate<Task>(_syncRoot);

		lock (_syncRoot)
		{
			Assert.IsFalse(gate.IsStopping);

			bool began = gate.TryBeginStop(out Task? stopTask, out IReadOnlyList<Task> operations);

			Assert.IsTrue(began);
			Assert.IsNull(stopTask);
			Assert.IsEmpty(operations);
			Assert.IsTrue(gate.IsStopping);
		}
	}

	[TestMethod]
	public void TryBeginStop_AfterTheStopTaskWasRecorded_ReturnsTheMemoizedTask()
	{
		var gate = new StopGate<Task>(_syncRoot);
		Task stopTask = Task.FromResult(1);

		lock (_syncRoot)
		{
			gate.TryBeginStop(out _, out _);
			gate.RecordStopTask(stopTask);

			bool began = gate.TryBeginStop(out Task? memoized, out IReadOnlyList<Task> operations);

			Assert.IsFalse(began);
			Assert.AreSame(stopTask, memoized);
			Assert.IsEmpty(operations);
		}
	}

	[TestMethod]
	public void ThrowIfStopping_BeforeTheStop_DoesNothing()
	{
		var gate = new StopGate<Task>(_syncRoot);

		lock (_syncRoot)
			gate.ThrowIfStopping(this);
	}

	[TestMethod]
	public void ThrowIfStopping_AfterTheStopBegan_Throws()
	{
		var gate = new StopGate<Task>(_syncRoot);

		lock (_syncRoot)
		{
			gate.TryBeginStop(out _, out _);
			Assert.ThrowsExactly<ObjectDisposedException>(() => gate.ThrowIfStopping(this));
		}
	}

	[TestMethod]
	public void TryBeginStop_FromTheFlowOfAnOperationInFlight_ThrowsAndBeginsNoStop()
	{
		var gate = new StopGate<Task>(_syncRoot);
		Task operation = NewOperation();

		Task.Run(() =>
			{
				lock (_syncRoot)
				{
					gate.Admit(operation, owner: this);
					Assert.ThrowsExactly<InvalidOperationException>(() => gate.TryBeginStop(out _, out _));
				}
			})
			.GetAwaiter()
			.GetResult();

		IReadOnlyList<Task> inFlight;

		lock (_syncRoot)
		{
			Assert.IsFalse(gate.IsStopping);
			Assert.IsTrue(gate.TryBeginStop(out _, out inFlight));
		}

		Assert.AreSame(operation, inFlight.Single());
	}

	[TestMethod]
	public void TryBeginStop_ForAGateWhoseStopWaits_FromTheFlowOfAnOperationInFlight_ThrowsAndBeginsNoStop()
	{
		var gate = new StopGate<Task>(_syncRoot, stopWaitsForOperations: true);
		Task operation = NewOperation();

		Task.Run(() =>
			{
				lock (_syncRoot)
				{
					gate.Admit(operation, owner: this);

					Assert.ThrowsExactly<InvalidOperationException>(() => gate.TryBeginStop(out _, out _));
					Assert.IsFalse(gate.IsStopping);
				}
			})
			.GetAwaiter()
			.GetResult();
	}

	[TestMethod]
	public void TryBeginStop_ForAGateWhoseStopDoesNotWait_FromTheFlowOfAnOperationInFlight_BeginsTheStopAndReturnsThatOperation()
	{
		var gate = new StopGate<Task>(_syncRoot, stopWaitsForOperations: false);
		Task operation = NewOperation();

		bool began = false;
		bool stopping = false;

		IReadOnlyList<Task> inFlight = [];

		Task.Run(() =>
			{
				lock (_syncRoot)
				{
					gate.Admit(operation, owner: this);

					began = gate.TryBeginStop(out _, out inFlight);
					stopping = gate.IsStopping;
				}
			})
			.GetAwaiter()
			.GetResult();

		// The owner's release follows the exit of the operation that requested the stop, so the snapshot names it.
		Assert.IsTrue(began);
		Assert.IsTrue(stopping);
		Assert.AreSame(operation, inFlight.Single());
	}

	[TestMethod]
	public void TryBeginStop_ForAGateWhoseStopDoesNotWait_RepeatedFromTheFlowOfAnOperationInFlight_ReturnsTheMemoizedTask()
	{
		var gate = new StopGate<Task>(_syncRoot, stopWaitsForOperations: false);

		Task operation = NewOperation();
		Task stopTask = NewOperation();

		bool repeatedBegan = true;
		Task? memoized = null;

		Task.Run(() =>
			{
				lock (_syncRoot)
				{
					gate.Admit(operation, owner: this);

					Assert.IsTrue(gate.TryBeginStop(out _, out _));
					gate.RecordStopTask(stopTask);

					repeatedBegan = gate.TryBeginStop(out memoized, out _);
				}
			})
			.GetAwaiter()
			.GetResult();

		Assert.IsFalse(repeatedBegan);
		Assert.AreSame(stopTask, memoized);
	}

	[TestMethod]
	public void TryBeginStop_FromAFlowThatTheOperationStarted_Throws()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Task operation = NewOperation();
		InvalidOperationException? thrown = null;

		Task.Run(async () =>
			{
				lock (_syncRoot)
					gate.Admit(operation, owner: this);

				// The mark follows the continuation and the work the operation hands to the thread pool.
				await Task.Yield();
				await Task.Run(() => thrown = TryBeginStopFromThisFlow(gate));
			})
			.GetAwaiter()
			.GetResult();

		Assert.IsNotNull(thrown);
	}

	[TestMethod]
	public void TryBeginStop_FromTheFlowOfAnOperationInFlightAfterTheStopBegan_StillThrows()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Task operation = NewOperation();

		Action insideOperation = Task.Run(() =>
			{
				lock (_syncRoot)
					gate.Admit(operation, owner: this);

				return gate.Carry(() =>
				{
					InvalidOperationException? refused = TryBeginStopFromThisFlow(gate);

					Assert.IsNotNull(refused);
					Assert.Contains("inside an operation", refused.Message);
				});
			})
			.GetAwaiter()
			.GetResult();

		lock (_syncRoot)
		{
			Assert.IsTrue(gate.TryBeginStop(out _, out _));
			gate.RecordStopTask(Task.CompletedTask);
		}

		// The memoized stop task waits for the same operation, so repeating the stop from inside it
		// cannot complete either.
		insideOperation();
	}

	[TestMethod]
	public void TryBeginStop_AfterTheOperationCompleted_BeginsTheStopFromItsFlow()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Task operation = NewOperation();
		bool began = false;

		Task.Run(() =>
			{
				lock (_syncRoot)
				{
					gate.Admit(operation, owner: this);
					gate.Complete(operation);

					began = gate.TryBeginStop(out _, out _);
				}
			})
			.GetAwaiter()
			.GetResult();

		Assert.IsTrue(began);
	}

	[TestMethod]
	public void TryBeginStop_WhenOnlyTheOuterOperationRemainsInFlight_Throws()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Task outer = NewOperation();
		Task inner = NewOperation();

		Task.Run(() =>
			{
				lock (_syncRoot)
				{
					gate.Admit(outer, owner: this);
					gate.Admit(inner, owner: this);

					gate.Complete(inner);

					Assert.ThrowsExactly<InvalidOperationException>(() => gate.TryBeginStop(out _, out _));
				}
			})
			.GetAwaiter()
			.GetResult();
	}

	[TestMethod]
	public async Task TryBeginStop_FromTheCallerOfTheAdmittingMethod_BeginsTheStopWhileTheOperationRuns()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Task operation = NewOperation();
		IReadOnlyList<Task> inFlight;

		await AdmitInAnAsyncMethod(gate, operation);

		lock (_syncRoot)
			Assert.IsTrue(gate.TryBeginStop(out _, out inFlight));

		Assert.AreSame(operation, inFlight.Single());
	}

	[TestMethod]
	public void TryBeginStop_ForAnOperationOfAnotherGate_BeginsTheStop()
	{
		var gate = new StopGate<Task>(_syncRoot);
		object otherSyncRoot = new();
		var otherGate = new StopGate<Task>(otherSyncRoot);
		bool began = false;

		Task.Run(() =>
			{
				lock (_syncRoot)
					gate.Admit(NewOperation(), owner: this);

				lock (otherSyncRoot)
					began = otherGate.TryBeginStop(out _, out _);
			})
			.GetAwaiter()
			.GetResult();

		Assert.IsTrue(began);
	}

	[TestMethod]
	public void Carry_WhenTheCallingFlowIsInsideNoOperation_ReturnsTheActionItself()
	{
		var gate = new StopGate<Task>(_syncRoot);

		static void DoNothing()
		{ }

		Assert.AreSame(DoNothing, gate.Carry(DoNothing));
	}

	[TestMethod]
	public void Carry_OnAThreadThatDoesNotFlowTheContext_RestoresTheMarkOnlyForTheAction()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Task operation = NewOperation();

		InvalidOperationException? inside = null;
		InvalidOperationException? after = null;

		Action carried = Task.Run(() =>
			{
				lock (_syncRoot)
					gate.Admit(operation, owner: this);

				return gate.Carry(() => inside = TryBeginStopFromThisFlow(gate));
			})
			.GetAwaiter()
			.GetResult();

		// A dispatcher that queues the action to a thread of its own may not flow the execution context.
		var thread = new Thread(() =>
		{
			carried();
			after = TryBeginStopFromThisFlow(gate);
		});

		using (ExecutionContext.SuppressFlow())
			thread.Start();

		thread.Join();

		Assert.IsNotNull(inside);
		Assert.IsNull(after);
	}

	[TestMethod]
	public void Carry_InsideAnotherOperationsFlow_RestoresThatOperationsMarkAfterTheAction()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Task carriedOperation = NewOperation();
		Task flowOperation = NewOperation();

		InvalidOperationException? inside = null;
		InvalidOperationException? after = null;

		Action carried = Task.Run(() =>
			{
				lock (_syncRoot)
					gate.Admit(carriedOperation, owner: this);

				return gate.Carry(() => inside = TryBeginStopFromThisFlow(gate));
			})
			.GetAwaiter()
			.GetResult();

		Task.Run(() =>
			{
				lock (_syncRoot)
					gate.Admit(flowOperation, owner: this);

				carried();
				after = TryBeginStopFromThisFlow(gate);
			})
			.GetAwaiter()
			.GetResult();

		// The carried action restores the mark the flow had before it, not an empty one, so the flow's own operation still refuses a stop.
		Assert.IsNotNull(inside);
		Assert.IsNotNull(after);
	}

	[TestMethod]
	public void StateMembers_WithoutTheOwnersLock_Throw()
	{
		var gate = new StopGate<Task>(_syncRoot);
		Task operation = NewOperation();

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = gate.IsStopping);
		Assert.ThrowsExactly<InvalidOperationException>(() => gate.ThrowIfStopping(this));
		Assert.ThrowsExactly<InvalidOperationException>(() => gate.Admit(operation, owner: this));
		Assert.ThrowsExactly<InvalidOperationException>(() => gate.Complete(operation));
		Assert.ThrowsExactly<InvalidOperationException>(() => gate.TryBeginStop(out _, out _));
		Assert.ThrowsExactly<InvalidOperationException>(() => gate.RecordStopTask(operation));
	}

	[TestMethod]
	public void Admit_WithoutTheOwnersLock_TracksNothing()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Assert.ThrowsExactly<InvalidOperationException>(() => gate.Admit(NewOperation(), owner: this));

		IReadOnlyList<Task> inFlight;

		lock (_syncRoot)
			gate.TryBeginStop(out _, out inFlight);

		Assert.IsEmpty(inFlight);
	}

	[TestMethod]
	public void PublicMembers_GivenNull_ThrowArgumentNullException()
	{
		var gate = new StopGate<Task>(_syncRoot);

		Assert.ThrowsExactly<ArgumentNullException>(() => new StopGate<Task>(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => new StopGate<Task>(null!, stopWaitsForOperations: false));
		Assert.ThrowsExactly<ArgumentNullException>(() => gate.Carry(null!));

		lock (_syncRoot)
		{
			Assert.ThrowsExactly<ArgumentNullException>(() => gate.ThrowIfStopping(null!));
			Assert.ThrowsExactly<ArgumentNullException>(() => gate.Admit(null!, owner: this));
			Assert.ThrowsExactly<ArgumentNullException>(() => gate.Admit(NewOperation(), owner: null!));
			Assert.ThrowsExactly<ArgumentNullException>(() => gate.Complete(null!));
			Assert.ThrowsExactly<ArgumentNullException>(() => gate.RecordStopTask(null!));
		}
	}

	// A flow the mark of an operation does not reach, as the owner's asynchronous methods keep the mark out of their callers.
	private void AdmitFromAnotherFlow(StopGate<Task> gate, Task operation)
	{
		Task.Run(() =>
			{
				lock (_syncRoot)
					gate.Admit(operation, owner: this);
			})
			.GetAwaiter()
			.GetResult();
	}

	private async Task AdmitInAnAsyncMethod(StopGate<Task> gate, Task operation)
	{
		lock (_syncRoot)
			gate.Admit(operation, owner: this);

		await Task.Yield();
	}

	private InvalidOperationException? TryBeginStopFromThisFlow(StopGate<Task> gate)
	{
		lock (_syncRoot)
		{
			try
			{
				gate.TryBeginStop(out _, out _);
			}
			catch (InvalidOperationException exception)
			{
				return exception;
			}
		}

		return null;
	}

	private static Task NewOperation() => new TaskCompletionSource().Task;
}
