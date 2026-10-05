using Nickelony.IDEKit.Workspace.FileSystem;
using static Nickelony.IDEKit.Workspace.Tests.FileSystemTestHelpers;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Covers the retry of a replace, a move or a delete directly: which failures it repeats, how often, and what it checks between attempts.
/// </summary>
[TestClass]
public sealed class TransientFailureRetryTests
{
	public required TestContext TestContext { get; set; }

	[TestMethod]
	[DataRow(SharingViolation, true)]
	[DataRow(LockViolation, true)]
	[DataRow(UnableToRemoveReplaced, true)]
	[DataRow(UnableToMoveReplacement, false)]
	[DataRow(UnableToMoveReplacement2, false)]
	[DataRow(GenericIOFailure, false)]
	public void IsSharingFailure_FollowsTheHResultOfTheFailure(int hResult, bool expected) =>
		Assert.AreEqual(expected, TransientFailureRetry.IsSharingFailure(new IOException("The file is in use.", hResult)));

	[TestMethod]
	public async Task RunAsync_AnOperationThatFailsOnce_RunsAgainAndReturnsNull()
	{
		int calls = 0;

		string? rejected = await TransientFailureRetry.RunAsync<string>(
			() =>
			{
				if (++calls == 1)
					throw new IOException("The file is in use.", SharingViolation);
			},

			checkPreconditionsAsync: null,
			TestContext.CancellationToken
		);

		Assert.IsNull(rejected);
		Assert.AreEqual(2, calls);
	}

	[TestMethod]
	public async Task RunAsync_AnOperationThatKeepsFailing_RunsOnceMoreThanItHasDelaysAndThrowsTheLastFailure()
	{
		int calls = 0;

		IOException exception = await Assert.ThrowsExactlyAsync<IOException>(() =>
			TransientFailureRetry.RunAsync<string>(
				() =>
				{
					calls++;
					throw new IOException($"Attempt {calls}.", LockViolation);
				},

				checkPreconditionsAsync: null,
				TestContext.CancellationToken
			)
		);

		Assert.AreEqual(5, calls);
		Assert.AreEqual("Attempt 5.", exception.Message);
	}

	[TestMethod]
	public async Task RunAsync_AFailureThatIsNotTransient_RunsOnce()
	{
		int calls = 0;

		await Assert.ThrowsExactlyAsync<IOException>(() =>
			TransientFailureRetry.RunAsync<string>(
				() =>
				{
					calls++;
					throw new IOException("The replacement was removed.", UnableToMoveReplacement);
				},

				checkPreconditionsAsync: null,
				TestContext.CancellationToken
			)
		);

		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public async Task RunAsync_ChecksThePreconditionsBeforeEachRetry()
	{
		int calls = 0;
		int checks = 0;

		await Assert.ThrowsExactlyAsync<IOException>(() =>
			TransientFailureRetry.RunAsync(
				() =>
				{
					calls++;
					throw new IOException("The file is in use.", SharingViolation);
				},

				_ =>
				{
					checks++;
					return Task.FromResult<string?>(null);
				},

				TestContext.CancellationToken
			)
		);

		Assert.AreEqual(5, calls);
		Assert.AreEqual(4, checks);
	}

	[TestMethod]
	public async Task RunAsync_ARejectedPrecondition_EndsTheRetryWithTheRejection()
	{
		int calls = 0;

		string? rejected = await TransientFailureRetry.RunAsync(
			() =>
			{
				calls++;
				throw new IOException("The file is in use.", SharingViolation);
			},

			_ => Task.FromResult<string?>("changed meanwhile"),
			TestContext.CancellationToken
		);

		Assert.AreEqual("changed meanwhile", rejected);
		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public async Task RunAsync_ATokenCanceledDuringTheDelay_StopsTheRetry()
	{
		using CancellationTokenSource cancellation = new();
		int calls = 0;

		await Assert.ThrowsAsync<OperationCanceledException>(() =>
			TransientFailureRetry.RunAsync<string>(
				() =>
				{
					calls++;
					cancellation.Cancel();
					throw new IOException("The file is in use.", SharingViolation);
				},

				checkPreconditionsAsync: null,
				cancellation.Token
			)
		);

		Assert.AreEqual(1, calls);
	}
}
