using System.Collections.Concurrent;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Holds the apply that each view's publication started, so an operation can wait until the store applied what a view published.</summary>
/// <remarks>
/// An apply runs fire-and-forget from the raise of <see cref="IWorkspaceDocumentView.ApplyRequested"/>, and a view
/// can raise again before the earlier apply finished, so the entry of a view is the combination of its applies in flight. An apply never faults,
/// because the coordinator records a failed apply, and a dispatcher fault while it ends the lost publication, as unsynchronized view state,
/// so waiting on the entries never throws.
/// </remarks>
internal sealed class InFlightApplyTracker
{
	private readonly ConcurrentDictionary<IWorkspaceDocumentView, Task> _applies = new(ReferenceEqualityComparer.Instance);

	internal void Track(IWorkspaceDocumentView view, Task apply)
	{
		if (apply.IsCompleted)
			return;

		Task combined = _applies.AddOrUpdate(view, apply, (_, earlier) => Task.WhenAll(earlier, apply));

		// The entry goes once it is done, unless a later apply of the view replaced it in the meantime.
		_ = combined.ContinueWith(
			continuationFunction: _ => _applies.TryRemove(KeyValuePair.Create(view, combined)),
			cancellationToken: CancellationToken.None,
			continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
			scheduler: TaskScheduler.Default
		);
	}

	internal Task WhenAppliedAsync(IEnumerable<IWorkspaceDocumentView> views) =>
		Task.WhenAll(views.Select(view => _applies.TryGetValue(view, out Task? apply) ? apply : Task.CompletedTask));
}
