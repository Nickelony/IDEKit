namespace Nickelony.IDEKit.Testing;

/// <summary>Runs the randomized concurrent soak tests: it picks and logs the seed and runs the task bodies at once under a hang bound.</summary>
internal static class TestSoak
{
	/// <summary>The environment variable whose integer value replaces the random seed, so a failing run replays its operation scripts.</summary>
	internal const string SeedVariable = "NICKELONY_SOAK_SEED";

	/// <summary>Reads the seed from <see cref="SeedVariable"/> or picks a random one, and writes it to the test output.</summary>
	/// <param name="context">The context of the running test, which receives the seed line.</param>
	/// <returns>The seed, which every task derives its own random sequence from.</returns>
	internal static int ReadSeed(TestContext context)
	{
		int seed = int.TryParse(Environment.GetEnvironmentVariable(SeedVariable), out int fixedSeed) ? fixedSeed : Random.Shared.Next();

		context.WriteLine($"Soak seed: {seed}. Set {SeedVariable}={seed} to replay the operation scripts; the interleaving varies from run to run.");
		return seed;
	}

	/// <summary>Runs <paramref name="body"/> once per task index on the thread pool at the same time, and returns when all of them finish.</summary>
	/// <param name="taskCount">The number of concurrent tasks.</param>
	/// <param name="body">The work of one task, given its index.</param>
	/// <param name="seed">The seed, which every failure message names.</param>
	/// <param name="hangBound">How long all the tasks may run before the run counts as hung.</param>
	/// <remarks>
	/// The run fails when a task throws, when the tasks outlast the bound, or when a task fault went unobserved. The unobserved faults are collected
	/// across a full collection after the run, so a task that nobody awaited still reports.
	/// </remarks>
	internal static async Task RunAsync(int taskCount, Func<int, Task> body, int seed, TimeSpan hangBound)
	{
		// Faults of earlier tests surface here, before the watch starts, instead of being charged to this run.
		CollectGarbage();

		List<Exception> unobserved = [];

		void Watch(object? sender, UnobservedTaskExceptionEventArgs e)
		{
			lock (unobserved)
				unobserved.Add(e.Exception);

			e.SetObserved();
		}

		TaskScheduler.UnobservedTaskException += Watch;

		try
		{
			var all = Task.WhenAll(Enumerable.Range(0, taskCount).Select(index => Task.Run(() => body(index))));

			try
			{
				await all.WaitAsync(hangBound);
			}
			catch (TimeoutException)
			{
				Assert.Fail($"The soak tasks did not finish within {hangBound.TotalSeconds:0} s. Seed {seed}.");
			}
			catch (Exception exception)
			{
				Assert.Fail($"A soak task threw. Seed {seed}.{Environment.NewLine}{exception}");
			}

			CollectGarbage();
		}
		finally
		{
			TaskScheduler.UnobservedTaskException -= Watch;
		}

		lock (unobserved)
		{
			Assert.IsEmpty(
				unobserved,
				$"A task fault went unobserved. Seed {seed}.{Environment.NewLine}{string.Join(Environment.NewLine, unobserved)}"
			);
		}
	}

	private static void CollectGarbage()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}
}
