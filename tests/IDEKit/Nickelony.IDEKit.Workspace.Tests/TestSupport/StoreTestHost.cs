using System.Runtime.ExceptionServices;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// The lock, the tracker, the stop gate and the operation gate of a store, wired the way the store wires them, for the tests of one collaborator.
/// </summary>
internal class StoreTestHost
{
	internal StoreTestHost(LocalPathComparisonPolicy? pathComparison = null)
	{
		Options = new WorkspaceOptions { PathComparison = pathComparison ?? LocalPathComparisonPolicy.CaseSensitive };
		Tracker = new WorkspaceDocumentTracker(SyncRoot, Options);
		Gate = new StopGate<OperationLease>(SyncRoot);
		OperationGate = new OperationGate(SyncRoot, Tracker, stopGate: Gate, owner: this);
	}

	internal object SyncRoot { get; } = new();

	internal WorkspaceOptions Options { get; }

	internal WorkspaceDocumentTracker Tracker { get; }

	internal StopGate<OperationLease> Gate { get; }

	internal OperationGate OperationGate { get; }

	/// <summary>Starts tracking a document, the way an open does.</summary>
	/// <param name="documentId">The normalized document id.</param>
	/// <param name="onDiskStamp">The stamp the store tracks for the file; a missing file when omitted.</param>
	internal LogicalDocument AddDocument(string documentId, FileStamp? onDiskStamp = null)
	{
		var document = new LogicalDocument(
			SyncRoot,
			documentKey: new WorkspaceDocumentKey(Guid.NewGuid()),
			documentId,
			displayPath: documentId,
			content: "content",
			fileFormat: TestSnapshots.FileFormat,
			onDiskStamp: onDiskStamp ?? FileStamp.Missing,
			noBomEncodings: [TextEncoding.Utf8]
		);

		lock (SyncRoot)
			Tracker.AddDocument(documentId, document);

		return document;
	}

	/// <summary>Replaces the logical content of a tracked document under the store lock, as an edit of a view does.</summary>
	internal void Edit(LogicalDocument document, string content)
	{
		lock (SyncRoot)
			document.ReplaceContent(content, document.FileFormat);
	}

	/// <summary>Runs a step under the store lock, as the store does.</summary>
	internal TResult Locked<TResult>(Func<WorkspaceDocumentTracker, TResult> step)
	{
		lock (SyncRoot)
			return step(Tracker);
	}

	/// <summary>Runs a step under the store lock, as the store does.</summary>
	internal void Locked(Action<WorkspaceDocumentTracker> step)
	{
		lock (SyncRoot)
			step(Tracker);
	}

	/// <summary>Begins the stop, as the store's disposal does, and returns the operations the stop would wait for.</summary>
	internal IReadOnlyList<OperationLease> Stop() => BeginStop().Join();

	/// <summary>Starts the stop on a thread of its own, which takes the store's lock the way disposal does.</summary>
	/// <remarks>
	/// The thread does not carry the mark of an operation the test admitted, because a stop requested from inside an operation throws.
	/// </remarks>
	internal PendingStop BeginStop() => new(SyncRoot, Gate);

	/// <summary>A stop running on a thread of its own.</summary>
	internal sealed class PendingStop
	{
		private readonly Thread _thread;

		private IReadOnlyList<OperationLease> _operations = [];
		private Exception? _failure;

		internal PendingStop(object syncRoot, StopGate<OperationLease> gate)
		{
			using (ExecutionContext.SuppressFlow())
			{
				_thread = new Thread(() =>
				{
					try
					{
						lock (syncRoot)
						{
							gate.TryBeginStop(out _, out _operations);
							gate.RecordStopTask(Task.CompletedTask);
						}
					}
					catch (Exception exception)
					{
						_failure = exception;
					}
				});

				_thread.Start();
			}
		}

		/// <summary>Indicates whether the thread is blocked, which for a stop that has not entered the lock means it waits for the lock.</summary>
		internal bool IsBlocked => (_thread.ThreadState & ThreadState.WaitSleepJoin) != 0;

		/// <summary>Waits for the stop and returns the operations it would wait for, or rethrows the failure of its thread.</summary>
		internal IReadOnlyList<OperationLease> Join()
		{
			_thread.Join();

			if (_failure is not null)
				ExceptionDispatchInfo.Capture(_failure).Throw();

			return _operations;
		}
	}
}
