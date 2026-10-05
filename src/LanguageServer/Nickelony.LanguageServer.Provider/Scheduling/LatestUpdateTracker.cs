using System.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Tracks, for each normalized document path, the newest latest-only update and the update that is running, and cancels the running one on request.
/// </summary>
/// <param name="syncRoot">The scheduler's lock, which guards the registrations together with the operation lanes.</param>
/// <param name="pathComparison">The policy that decides which paths share a registration.</param>
/// <remarks>
/// <para>
/// A newer update supersedes the registered one without canceling it while it runs: the older update stays in its lane and is skipped
/// when it has not started by its turn. A canceled update that has not started is skipped the same way, because the cancellation clears
/// the registered slot.
/// </para>
/// <para>
/// The members that change a registration run inside the scheduler's critical sections, so they require the caller to hold the scheduler's lock;
/// <see cref="Cancel"/> and <see cref="CancelAll"/> take it themselves and cancel outside it, so no cancellation callback runs while scheduler state
/// is locked.
/// </para>
/// </remarks>
internal sealed class LatestUpdateTracker(object syncRoot, LocalPathComparisonPolicy pathComparison)
{
	private readonly Dictionary<string, Entry> _entries = new(pathComparison.Comparer);

	/// <summary>Registers a new newest update for a document path, which supersedes the one registered before it.</summary>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	/// <returns>The registration of the new update.</returns>
	/// <remarks>
	/// The caller must hold the scheduler's lock and queue the update's node on the document's lane in the same critical section, so a lane
	/// never drains while one of its updates is registered.
	/// </remarks>
	internal UpdateHandle Register(string normalizedFilePath)
	{
		AssertLockHeld();

		if (!_entries.TryGetValue(normalizedFilePath, out Entry? entry))
		{
			entry = new Entry();
			_entries[normalizedFilePath] = entry;
		}

		var registration = new UpdateHandle();
		entry.Newest = registration;

		return registration;
	}

	/// <summary>Marks an update as running when it is still the newest registration for its document path.</summary>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	/// <param name="registration">The registration to mark as running.</param>
	/// <returns>The cancellation source of the running update, or <see langword="null"/> when the update may not run.</returns>
	/// <remarks>
	/// The caller must hold the scheduler's lock. A superseded registration must not run even when its pending start races
	/// the enqueue that replaced it. The running registration stays tracked so a cancellation can reach an update that a superseding enqueue removed
	/// from the newest slot while it kept running.
	/// </remarks>
	internal CancellationTokenSource? Start(string normalizedFilePath, UpdateHandle registration)
	{
		AssertLockHeld();

		if (!_entries.TryGetValue(normalizedFilePath, out Entry? entry) || !ReferenceEquals(entry.Newest, registration))
			return null;

		entry.Running = registration;
		return registration.Start();
	}

	/// <summary>Forgets a settled update, and the document path once no registration remains.</summary>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	/// <param name="registration">The registration of the settled update.</param>
	/// <remarks>
	/// The caller must hold the scheduler's lock, clear the lane tail in the same critical section, and dispose the registration afterwards.
	/// </remarks>
	internal void Complete(string normalizedFilePath, UpdateHandle registration)
	{
		AssertLockHeld();

		if (!_entries.TryGetValue(normalizedFilePath, out Entry? entry))
			return;

		if (ReferenceEquals(entry.Newest, registration))
			entry.Newest = null;

		if (ReferenceEquals(entry.Running, registration))
			entry.Running = null;

		RemoveIfEmpty(normalizedFilePath, entry);
	}

	/// <summary>Cancels the running update of a document path and stops its pending update from starting.</summary>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	/// <remarks>
	/// A pending update is skipped and settles without running its delegate, because the cancellation clears the registered slot. The running update
	/// is canceled even when a newer update superseded it, so an enqueue cannot hide it from this call.
	/// </remarks>
	internal void Cancel(string normalizedFilePath)
	{
		UpdateHandle? running;

		lock (syncRoot)
		{
			if (!_entries.TryGetValue(normalizedFilePath, out Entry? entry))
				return;

			running = entry.Running;
			entry.Newest = null;

			RemoveIfEmpty(normalizedFilePath, entry);
		}

		running?.Cancel();
	}

	/// <summary>Cancels every running update and stops every pending update from starting.</summary>
	internal void CancelAll()
	{
		var running = new List<UpdateHandle>();

		lock (syncRoot)
		{
			foreach ((string normalizedFilePath, Entry entry) in _entries)
			{
				if (entry.Running is { } registration)
					running.Add(registration);

				entry.Newest = null;

				RemoveIfEmpty(normalizedFilePath, entry);
			}
		}

		for (int i = 0; i < running.Count; i++)
			running[i].Cancel();
	}

	/// <summary>Removes the entry of a document path once it holds neither a newest nor a running registration.</summary>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	/// <param name="entry">The entry of the path.</param>
	/// <remarks>
	/// The caller must hold the scheduler's lock.
	/// </remarks>
	private void RemoveIfEmpty(string normalizedFilePath, Entry entry)
	{
		if (entry.Newest is null && entry.Running is null)
			_entries.Remove(normalizedFilePath);
	}

	[Conditional("DEBUG")]
	private void AssertLockHeld() => Debug.Assert(Monitor.IsEntered(syncRoot), "The scheduler lock must be held.");

	/// <summary>The newest and the running update of one document path.</summary>
	private sealed class Entry
	{
		/// <summary>The handle of the newest update, if any.</summary>
		internal UpdateHandle? Newest { get; set; }

		/// <summary>The handle of the update that is currently running, if any.</summary>
		internal UpdateHandle? Running { get; set; }
	}

	/// <summary>Tracks one latest-only update and lazily owns the cancellation source of the running update.</summary>
	internal sealed class UpdateHandle : IDisposable
	{
		private readonly object _stateLock = new();
		private CancellationTokenSource? _source;

		/// <summary>Creates the cancellation source of the running update.</summary>
		/// <returns>The new cancellation source, which <see cref="Dispose"/> releases.</returns>
		internal CancellationTokenSource Start()
		{
			lock (_stateLock)
				return _source = new CancellationTokenSource();
		}

		/// <summary>Cancels the running update; a pending update has no source to cancel.</summary>
		internal void Cancel()
		{
			CancellationTokenSource? source;

			lock (_stateLock)
				source = _source;

			if (source is null)
				return;

			try
			{
				source.Cancel();
			}
			catch (ObjectDisposedException)
			{
				// The update completed and disposed its source while the cancel was in flight.
			}
			catch (AggregateException)
			{
				// Cancellation callbacks registered on the running update's token source can throw arbitrary exceptions;
				// a callback failure must not escape the public cancel surface.
			}
		}

		/// <summary>Releases the cancellation source once the update settled.</summary>
		public void Dispose()
		{
			CancellationTokenSource? source;

			lock (_stateLock)
			{
				source = _source;
				_source = null;
			}

			source?.Dispose();
		}
	}
}
