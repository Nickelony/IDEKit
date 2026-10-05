namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the build, publication and recursion rules of <see cref="RuleSetCache{TRuleSet}"/>, which every regex highlighting definition holds.
/// </summary>
[TestClass]
public sealed class RuleSetCacheTests
{
	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void Constructor_NullBuild_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new RuleSetCache<object>(null!));

	[TestMethod]
	public void Value_IsBuiltLazilyAndCached()
	{
		int buildCount = 0;

		var cache = new RuleSetCache<object>(() =>
		{
			buildCount++;
			return new object();
		});

		Assert.AreEqual(0, buildCount);

		object first = cache.Value;

		Assert.AreEqual(1, buildCount);
		Assert.AreSame(first, cache.Value);
		Assert.AreEqual(1, buildCount);
	}

	[TestMethod]
	public void Value_RebuildsWhenCacheVersionChanges()
	{
		int version = 1;

		var cache = new RuleSetCache<object>(() => new object(), () => version);

		object first = cache.Value;

		Assert.AreSame(first, cache.Value);

		version = 2;

		object rebuilt = cache.Value;

		Assert.AreNotSame(first, rebuilt);
		Assert.AreSame(rebuilt, cache.Value);
	}

	[TestMethod]
	public void Changed_CacheVersionChange_IsRaisedOnceAndNotForTheFirstBuild()
	{
		int version = 1;
		int changedCalls = 0;

		var cache = new RuleSetCache<object>(() => new object(), () => version);

		cache.Changed += (_, _) => changedCalls++;

		_ = cache.Value;

		Assert.AreEqual(0, changedCalls);

		version = 2;

		_ = cache.Value;

		Assert.AreEqual(1, changedCalls);
	}

	[TestMethod]
	public void Changed_HandlerCanReadTheRebuiltRuleSet()
	{
		int version = 1;
		object? observed = null;

		var cache = new RuleSetCache<object>(() => new object(), () => version);

		cache.Changed += (_, _) => observed = cache.Value;

		_ = cache.Value;

		version = 2;

		object rebuilt = cache.Value;

		// The guard is released and the rebuilt rule set is published before the event is raised.
		Assert.AreSame(rebuilt, observed);
	}

	[TestMethod]
	public void Value_BuildReadingTheValue_ThrowsInvalidOperationException()
	{
		RuleSetCache<object>? cache = null;

		cache = new RuleSetCache<object>(() =>
		{
			_ = cache!.Value;
			return new object();
		});

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = cache.Value);
	}

	[TestMethod]
	public void Value_BuildReadingTheValueDuringRebuild_ThrowsInvalidOperationException()
	{
		int version = 1;
		int buildCalls = 0;

		RuleSetCache<object>? cache = null;

		cache = new RuleSetCache<object>(
			() =>
			{
				buildCalls++;

				// Only the rebuild re-enters the cache, so the published snapshot must not bypass the guard.
				if (buildCalls > 1)
					_ = cache!.Value;

				return new object();
			},

			() => version
		);

		_ = cache.Value;

		version = 2;

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = cache.Value);
	}

	[TestMethod]
	public void Value_VersionCallbackReadingTheValue_ThrowsInvalidOperationException()
	{
		RuleSetCache<object>? cache = null;

		cache = new RuleSetCache<object>(
			() => new object(),

			() =>
			{
				// A callback that reads its own cache would re-invoke itself through the property;
				// the guard covers the callback as well, so the access throws instead of recursing.
				_ = cache!.Value;
				return 0;
			}
		);

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = cache.Value);
	}

	[TestMethod]
	public void Value_BuildReadingAnotherCache_IsAllowed()
	{
		var other = new RuleSetCache<object>(() => new object());
		object? composed = null;

		var cache = new RuleSetCache<object>(() =>
		{
			// The guard is scoped per cache, so composing from another cache is not re-entry.
			composed = other.Value;
			return new object();
		});

		Assert.IsNotNull(cache.Value);
		Assert.IsNotNull(composed);
	}

	[TestMethod]
	public void Value_BuildThrows_DoesNotCacheTheFailureAndRecoversOnNextAccess()
	{
		int buildCalls = 0;

		var cache = new RuleSetCache<object>(() =>
		{
			buildCalls++;

			if (buildCalls == 1)
				throw new InvalidOperationException("The first build fails.");

			return new object();
		});

		Assert.ThrowsExactly<InvalidOperationException>(() => cache.Value);
		Assert.AreEqual(1, buildCalls);

		// The failed build was not cached, so the next access retries and succeeds.
		Assert.IsNotNull(cache.Value);
		Assert.AreEqual(2, buildCalls);
	}

	[TestMethod]
	public void Value_VersionedBuildThrows_ReleasesTheGuardSoTheNextAccessRetries()
	{
		int buildCalls = 0;

		var cache = new RuleSetCache<object>(
			() =>
			{
				buildCalls++;

				if (buildCalls == 1)
					throw new InvalidOperationException("The first build fails.");

				return new object();
			},

			() => 1
		);

		Assert.ThrowsExactly<InvalidOperationException>(() => cache.Value);
		Assert.IsNotNull(cache.Value);
		Assert.AreEqual(2, buildCalls);
	}

	[TestMethod]
	public void Value_ConcurrentAccessWithRebuilds_ReturnsBuiltValues()
	{
		int[] version = [1];

		var cache = new RuleSetCache<string>(() => "built", () => version[0]);

		Assert.IsNotNull(cache.Value);

		var results = new System.Collections.Concurrent.ConcurrentBag<string>();

		Parallel.For(
			0,
			200,

			index =>
			{
				if (index % 40 == 0)
					Interlocked.Increment(ref version[0]);

				results.Add(cache.Value);
			}
		);

		Assert.HasCount(200, results);
		Assert.IsTrue(results.All(static value => value == "built"));
	}

	[TestMethod]
	public void Changed_SameVersionConcurrentRebuilds_IsNotRaised()
	{
		int buildCalls = 0;

		using var secondBuildEntered = new ManualResetEventSlim(false);
		using var startGate = new Barrier(2);

		var cache = new RuleSetCache<object>(
			() =>
			{
				if (Interlocked.Increment(ref buildCalls) == 1)
				{
					// Hold the first build until the other builder is inside its own build, so both
					// publications replace a snapshot of the same version.
					secondBuildEntered.Wait(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
				}
				else
				{
					secondBuildEntered.Set();
				}

				return new object();
			},

			() => 1
		);

		int changedCalls = 0;
		cache.Changed += (_, _) => Interlocked.Increment(ref changedCalls);

		object? firstResult = null;
		object? secondResult = null;

		var first = new Thread(() =>
		{
			startGate.SignalAndWait(TestContext.CancellationToken);
			firstResult = cache.Value;
		})
		{
			IsBackground = true,
		};

		var second = new Thread(() =>
		{
			startGate.SignalAndWait(TestContext.CancellationToken);
			secondResult = cache.Value;
		})
		{
			IsBackground = true,
		};

		first.Start();
		second.Start();

		Assert.IsTrue(first.Join(TimeSpan.FromSeconds(15)), "The first build did not complete.");
		Assert.IsTrue(second.Join(TimeSpan.FromSeconds(15)), "The second build did not complete.");

		Assert.IsNotNull(firstResult);
		Assert.IsNotNull(secondResult);
		Assert.AreEqual(2, buildCalls);

		// The second publication replaced a snapshot of the same version, so no rebuild is reported;
		// the event only covers the replacement of a snapshot of a different version.
		Assert.AreEqual(0, changedCalls);
	}

	[TestMethod]
	public void Value_SameVersionConcurrentRebuilds_PublishOneValueThatLaterReadsKeepReturning()
	{
		int buildCalls = 0;

		using var secondBuildEntered = new ManualResetEventSlim(false);
		using var startGate = new Barrier(2);

		var cache = new RuleSetCache<object>(
			() =>
			{
				if (Interlocked.Increment(ref buildCalls) == 1)
					secondBuildEntered.Wait(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
				else
					secondBuildEntered.Set();

				return new object();
			},

			() => 1
		);

		object? firstResult = null;
		object? secondResult = null;

		var first = new Thread(() =>
		{
			startGate.SignalAndWait(TestContext.CancellationToken);
			firstResult = cache.Value;
		})
		{
			IsBackground = true,
		};

		var second = new Thread(() =>
		{
			startGate.SignalAndWait(TestContext.CancellationToken);
			secondResult = cache.Value;
		})
		{
			IsBackground = true,
		};

		first.Start();
		second.Start();

		Assert.IsTrue(first.Join(TimeSpan.FromSeconds(15)), "The first build did not complete.");
		Assert.IsTrue(second.Join(TimeSpan.FromSeconds(15)), "The second build did not complete.");

		object published = cache.Value;

		Assert.IsTrue(ReferenceEquals(published, firstResult) || ReferenceEquals(published, secondResult), "The published value is neither build.");
		Assert.AreSame(published, cache.Value);
		Assert.AreSame(published, cache.Value);
		Assert.AreEqual(2, buildCalls);
	}

	[TestMethod]
	public void Value_StaleBuildPublishedOverANewerOne_IsReplacedByTheNextAccess()
	{
		int[] version = [1];
		int buildCalls = 0;

		using var staleBuildEntered = new ManualResetEventSlim(false);
		using var releaseStaleBuild = new ManualResetEventSlim(false);

		var cache = new RuleSetCache<object>(
			() =>
			{
				if (Interlocked.Increment(ref buildCalls) == 1)
				{
					staleBuildEntered.Set();
					releaseStaleBuild.Wait(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
				}

				return new object();
			},

			() => Volatile.Read(ref version[0])
		);

		int changedCalls = 0;
		cache.Changed += (_, _) => Interlocked.Increment(ref changedCalls);

		object? stale = null;

		var staleReader = new Thread(() => stale = cache.Value) { IsBackground = true };

		staleReader.Start();

		Assert.IsTrue(staleBuildEntered.Wait(TimeSpan.FromSeconds(10), TestContext.CancellationToken), "The first build did not start.");

		// A newer version is built and published while the first build is still running.
		Volatile.Write(ref version[0], 2);

		object current = cache.Value;

		Assert.AreEqual(0, changedCalls);

		releaseStaleBuild.Set();

		Assert.IsTrue(staleReader.Join(TimeSpan.FromSeconds(15)), "The first build did not complete.");

		// The stale build publishes over the newer snapshot, which is reported as a rebuild, and the next access converges.
		Assert.AreEqual(1, changedCalls);

		object converged = cache.Value;

		Assert.AreEqual(3, buildCalls);
		Assert.AreEqual(2, changedCalls);
		Assert.AreNotSame(stale, converged);
		Assert.AreNotSame(current, converged);
		Assert.AreSame(converged, cache.Value);
		Assert.AreEqual(3, buildCalls);
	}

	[TestMethod]
	public void Value_VersionCallbackThrows_ReleasesTheGuardSoTheNextAccessSucceeds()
	{
		int calls = 0;

		var cache = new RuleSetCache<object>(
			() => new object(),

			() =>
			{
				if (++calls == 1)
					throw new InvalidOperationException("The first version read fails.");

				return 1;
			}
		);

		Assert.ThrowsExactly<InvalidOperationException>(() => cache.Value);
		Assert.IsNotNull(cache.Value);
	}

	[TestMethod]
	public void Value_ReadsTheVersionOncePerAccess()
	{
		int versionReads = 0;

		var cache = new RuleSetCache<object>(
			() => new object(),

			() =>
			{
				versionReads++;
				return 1;
			}
		);

		_ = cache.Value;
		Assert.AreEqual(1, versionReads);

		_ = cache.Value;
		Assert.AreEqual(2, versionReads);
	}

	[TestMethod]
	public void Changed_HandlerThrows_PropagatesAndLeavesTheRebuiltValuePublished()
	{
		int version = 1;

		var cache = new RuleSetCache<object>(() => new object(), () => version);

		_ = cache.Value;
		cache.Changed += (_, _) => throw new InvalidOperationException("The handler fails.");

		version = 2;

		Assert.ThrowsExactly<InvalidOperationException>(() => cache.Value);

		// The rebuilt value was published before the event, so the next access serves it without another rebuild.
		Assert.IsNotNull(cache.Value);
	}
}
