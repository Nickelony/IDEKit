namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the rejection of scheduler calls that a queued operation issues, and the detached start that escapes the rejection.</summary>
[TestClass]
public sealed class OperationReentrancyGateTests
{
	private static readonly string s_first = TestPaths.Script("first.test");
	private static readonly string s_second = TestPaths.Script("second.test");

	[TestMethod]
	public void EnsureNotInside_OutsideAnOperation_DoesNothing()
	{
		var gate = new OperationReentrancyGate();
		gate.EnsureNotInside("Member");
	}

	[TestMethod]
	public async Task EnsureNotInside_InsideARunningOperation_ThrowsNamingTheMemberAndThePath()
	{
		var gate = new OperationReentrancyGate();

		InvalidOperationException exception = await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() =>
				gate.RunAsync(
					new OperationReentrancyGate.ActiveDocumentContext([s_first]),

					() =>
					{
						gate.EnsureNotInside("Member");
						return Task.FromResult(true);
					}
				)
			)
			.ConfigureAwait(false);

		Assert.Contains("'Member' was called from inside a queued scheduler operation", exception.Message);
		Assert.Contains($" for '{s_first}'", exception.Message);
	}

	[TestMethod]
	public async Task EnsureNotInside_InsideAnExclusiveOperation_NamesBothPaths()
	{
		var gate = new OperationReentrancyGate();

		InvalidOperationException exception = await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() =>
				gate.RunAsync(
					new OperationReentrancyGate.ActiveDocumentContext([s_first, s_second]),

					() =>
					{
						gate.EnsureNotInside("Member");
						return Task.FromResult(true);
					}
				)
			)
			.ConfigureAwait(false);

		Assert.Contains($" for '{s_first}' and '{s_second}'", exception.Message);
	}

	[TestMethod]
	public async Task EnsureNotInside_FromWorkThatOutlivesTheOperation_DoesNothing()
	{
		var gate = new OperationReentrancyGate();
		var release = new TaskCompletionSource();

		Task lateCheck = Task.CompletedTask;

		await gate.RunAsync(
				new OperationReentrancyGate.ActiveDocumentContext([s_first]),

				() =>
				{
					// The task captures the flow of the operation, so it still sees the context after the operation ended.
					lateCheck = Task.Run(async () =>
					{
						await release.Task.ConfigureAwait(false);
						gate.EnsureNotInside("Member");
					});

					return Task.FromResult(true);
				}
			)
			.ConfigureAwait(false);

		release.SetResult();

		await lateCheck.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RunAsync_RestoresTheOuterContextAfterANestedOperation()
	{
		var gate = new OperationReentrancyGate();

		InvalidOperationException exception = await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() =>
				gate.RunAsync(
					new OperationReentrancyGate.ActiveDocumentContext([s_first]),

					async () =>
					{
						await gate.RunAsync(new OperationReentrancyGate.ActiveDocumentContext([s_second]), () => Task.FromResult(true))
							.ConfigureAwait(false);

						gate.EnsureNotInside("Member");
						return true;
					}
				)
			)
			.ConfigureAwait(false);

		Assert.Contains($" for '{s_first}'", exception.Message);
		Assert.DoesNotContain(s_second, exception.Message);
	}

	[TestMethod]
	public async Task RunAsync_AfterItCompletes_LeavesNoContextBehind()
	{
		var gate = new OperationReentrancyGate();

		await gate.RunAsync(new OperationReentrancyGate.ActiveDocumentContext([s_first]), () => Task.FromResult(true)).ConfigureAwait(false);

		gate.EnsureNotInside("Member");
	}

	[TestMethod]
	public async Task RunDetached_HidesTheOperationFromWorkTheActionStartsAndRestoresItAfterwards()
	{
		var gate = new OperationReentrancyGate();
		Task? detachedCheck = null;

		InvalidOperationException exception = await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() =>
				gate.RunAsync(
					new OperationReentrancyGate.ActiveDocumentContext([s_first]),

					() =>
					{
						gate.RunDetached(() => detachedCheck = Task.Run(() => gate.EnsureNotInside("Detached")));

						// The detached work saw no operation, but the starting flow is still inside it.
						gate.EnsureNotInside("Member");
						return Task.FromResult(true);
					}
				)
			)
			.ConfigureAwait(false);

		Assert.Contains("'Member'", exception.Message);
		Assert.IsNotNull(detachedCheck);

		await detachedCheck.ConfigureAwait(false);
	}

	[TestMethod]
	public void RunDetached_OutsideAnOperation_RunsTheActionOnce()
	{
		var gate = new OperationReentrancyGate();
		int runs = 0;

		gate.RunDetached(() => runs++);

		Assert.AreEqual(1, runs);
	}
}
