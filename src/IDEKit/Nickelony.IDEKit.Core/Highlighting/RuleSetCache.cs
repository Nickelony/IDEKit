using System.Diagnostics;

namespace Nickelony.IDEKit.Core.Highlighting;

/// <summary>Builds a rule set on first access, serves it to concurrent readers, and rebuilds it when a cache version changes.</summary>
/// <typeparam name="TRuleSet">The engine's rule set type.</typeparam>
/// <remarks>
/// <para>
/// A cache over static data (no cache version) builds once through a <see cref="Lazy{T}"/>; a cache over live data
/// rebuilds when its cache version changes. No lock is held while the cache-version callback or the build delegate runs, so an access never blocks
/// on host code: two threads that observe a version change can build concurrently, and each installs its rule set with an atomic publication
/// that replaces the snapshot current at that moment. A publication that replaces a snapshot of a different version raises <see cref="Changed"/>;
/// the first build does not. A publication that loses a race against a later one is replaced on the next access, so the served rule set converges
/// on the current version.
/// </para>
/// <para>
/// A failed build is not cached, so the next access retries it.
/// </para>
/// </remarks>
public sealed class RuleSetCache<TRuleSet>
	where TRuleSet : class
{
	/// <summary>
	/// Stores a built rule set together with the cache version that produced it, so a reader can never observe a rule set and a version
	/// from different builds.
	/// </summary>
	private sealed class Snapshot(TRuleSet ruleSet, int version)
	{
		internal TRuleSet RuleSet { get; } = ruleSet;
		internal int Version { get; } = version;
	}

	/// <summary>
	/// Tracks, per thread, the caches being processed on it, whether their rule set is being built or their version callback is running.
	/// </summary>
	/// <remarks>
	/// The guard is thread-local so a concurrent build on another thread cannot interfere with it, and it is per cache so a build that reads
	/// another cache stays possible.
	/// </remarks>
	[ThreadStatic]
	private static HashSet<RuleSetCache<TRuleSet>>? s_buildsInProgress;

	private readonly Func<TRuleSet> _build;
	private readonly Func<int>? _cacheVersion;
	private readonly Lazy<TRuleSet>? _staticRuleSet;

	private Snapshot? _snapshot;

	/// <param name="build">Builds the rule set; it must not read <see cref="Value"/> of this cache.</param>
	/// <param name="cacheVersion">
	/// Returns the current version of the data the rule set derives from, or <see langword="null"/> to build
	/// the rule set once. A callback must not read <see cref="Value"/> of this cache.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="build"/> is <see langword="null"/>.</exception>
	public RuleSetCache(Func<TRuleSet> build, Func<int>? cacheVersion = null)
	{
		ArgumentNullException.ThrowIfNull(build);

		_build = build;
		_cacheVersion = cacheVersion;

		// PublicationOnly keeps a failed build from being cached, so a transient failure does not poison the cache.
		if (_cacheVersion is null)
			_staticRuleSet = new Lazy<TRuleSet>(BuildStatic, LazyThreadSafetyMode.PublicationOnly);
	}

	/// <summary>Raised after the rule set is rebuilt because the cache version changed.</summary>
	/// <remarks>
	/// <para>
	/// The event is raised after the build guard is released, so a handler can read <see cref="Value"/> and receive the freshly published rule set.
	/// The initial build does not raise it.
	/// </para>
	/// <para>
	/// The event is raised on the thread that triggered the rebuild. A handler that throws propagates the exception out of the <see cref="Value"/>
	/// access that triggered the rebuild.
	/// </para>
	/// </remarks>
	public event EventHandler? Changed;

	/// <summary>The rule set for the current cache version, built on first access.</summary>
	/// <exception cref="InvalidOperationException">
	/// The property was read from the cache-version callback or from the build delegate on the building thread; the access throws
	/// instead of recursing.
	/// </exception>
	/// <remarks>
	/// The cache-version callback is read once per access.
	/// </remarks>
	public TRuleSet Value
	{
		get
		{
			// The guard runs first, so any access during this thread's own build or version callback fails fast
			// whether or not a snapshot already exists.
			if (s_buildsInProgress?.Contains(this) == true)
			{
				throw new InvalidOperationException(
					"The rule set must not be read from the cache version callback or the build delegate; the access would recurse instead of returning a rule set."
				);
			}

			// Static and versioned are mutually exclusive: the constructor creates the lazy path only when there is no cache version.
			if (_cacheVersion is null)
			{
				Debug.Assert(_staticRuleSet is not null, "The static rule set is created whenever there is no cache version.");
				return _staticRuleSet.Value;
			}

			Snapshot? snapshot = Volatile.Read(ref _snapshot);

			// The version is read once per access, so a non-idempotent callback cannot make the snapshot and the
			// rebuild decision disagree. The guard is armed while the callback runs, because a callback that reads
			// this cache would otherwise re-invoke itself through the property.
			int version;

			EnterGuard();

			try
			{
				version = _cacheVersion();
			}
			finally
			{
				ExitGuard();
			}

			if (snapshot is not null && snapshot.Version == version)
				return snapshot.RuleSet;

			return BuildAndPublish(version);
		}
	}

	/// <summary>Builds the rule set of a cache over static data under the guard.</summary>
	/// <returns>The built rule set.</returns>
	private TRuleSet BuildStatic()
	{
		EnterGuard();

		try
		{
			return _build();
		}
		finally
		{
			ExitGuard();
		}
	}

	/// <summary>
	/// Builds the rule set, publishes it as the new snapshot, and raises <see cref="Changed"/> when it replaced a snapshot of a different version.
	/// </summary>
	/// <param name="version">The cache version the built rule set belongs to.</param>
	/// <returns>The built rule set.</returns>
	private TRuleSet BuildAndPublish(int version)
	{
		EnterGuard();

		TRuleSet ruleSet;
		bool rebuilt;

		try
		{
			ruleSet = _build();

			// The snapshot is published against the snapshot that is current at publication time, not against the one
			// this thread read before building: a concurrent build that published first is observed, so the event
			// reports what this publication actually replaced.
			var newSnapshot = new Snapshot(ruleSet, version);
			Snapshot? replaced;

			do
			{
				replaced = Volatile.Read(ref _snapshot);
				rebuilt = replaced is not null && replaced.Version != version;
			} while (Interlocked.CompareExchange(ref _snapshot, newSnapshot, replaced) != replaced);
		}
		finally
		{
			ExitGuard();
		}

		// The event is raised after the guard is released: a handler typically reinstalls the editor's highlighter,
		// which reads the rule set synchronously.
		if (rebuilt)
			Changed?.Invoke(this, EventArgs.Empty);

		return ruleSet;
	}

	private void EnterGuard() => (s_buildsInProgress ??= []).Add(this);

	private void ExitGuard()
	{
		HashSet<RuleSetCache<TRuleSet>>? buildsInProgress = s_buildsInProgress;

		if (buildsInProgress is null)
			return;

		buildsInProgress.Remove(this);

		if (buildsInProgress.Count == 0)
			s_buildsInProgress = null;
	}
}
