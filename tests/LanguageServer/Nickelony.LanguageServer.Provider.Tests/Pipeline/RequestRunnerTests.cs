using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class RequestRunnerTests
{
	private static RequestRunner CreateRunner(
		FakeLanguageServerClient? client,
		Func<CancellationToken, Task<bool>>? ensureConnectionStartedAsync = null,
		int restartThreshold = 2,
		TimeSpan? requestTimeout = null,
		ProviderLifetime? disposal = null,
		RequestTimeoutCounter? timeouts = null,
		ILogger? logger = null
	)
	{
		return new(
			new RequestRunnerContext(
				WorkspaceRootsDisplayText: @"C:\Workspace",
				client,

				Options: new LanguageServerFeatureProviderOptions
				{
					RequestTimeout = requestTimeout ?? TimeSpan.FromMilliseconds(50),
					RequestTimeoutRestartThreshold = restartThreshold,
				},

				Disposal: disposal ?? new ProviderLifetime(),
				EnsureConnectionStartedAsync: ensureConnectionStartedAsync ?? (_ => Task.FromResult(true)),
				Timeouts: timeouts ?? new RequestTimeoutCounter(restartThreshold),
				Logger: logger ?? NullLogger.Instance
			)
		);
	}

	private static Func<string, object, CancellationToken, Task<object?>> DelayUntilCanceled { get; } =
		async (_, _, cancellationToken) =>
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			return null;
		};

	[TestMethod]
	public async Task SendAsync_WhenNoClientIsConfigured_ReturnsTheFallbackValueWithoutStarting()
	{
		bool ensureStartedCalled = false;

		RequestRunner runner = CreateRunner(
			client: null,

			ensureConnectionStartedAsync: _ =>
			{
				ensureStartedCalled = true;
				return Task.FromResult(true);
			}
		);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.IsFalse(ensureStartedCalled);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheRequestSucceeds_ReturnsTheResponse()
	{
		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 7,
			SendRequestHandler = (_, _, _) => Task.FromResult<object?>("response"),
		};

		RequestRunner runner = CreateRunner(client);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("response", result);
		Assert.AreEqual(1, client.SendRequestCount);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheCallerTokenIsAlreadyCanceled_ThrowsOperationCanceled()
	{
		var client = new FakeLanguageServerClient { SendRequestHandler = DelayUntilCanceled };
		RequestRunner runner = CreateRunner(client);

		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		await Assert
			.ThrowsExactlyAsync<OperationCanceledException>(async () =>
				await runner
					.SendAsync<string>(
						method: "textDocument/hover",
						parameters: new object(),
						fallbackValue: "fallback",
						cancellationToken: cancellationTokenSource.Token
					)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		Assert.AreEqual(0, client.SendRequestCount);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheCallerCancelsDuringTheRequest_ThrowsOperationCanceled()
	{
		var client = new FakeLanguageServerClient { SendRequestHandler = DelayUntilCanceled };

		RequestRunner runner = CreateRunner(client, requestTimeout: TimeSpan.FromSeconds(30));

		using var cancellationTokenSource = new CancellationTokenSource();

		Task<string> requestTask = runner.SendAsync(
			method: "textDocument/hover",
			parameters: new object(),
			fallbackValue: "fallback",
			cancellationToken: cancellationTokenSource.Token
		);

		cancellationTokenSource.Cancel();

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await requestTask.ConfigureAwait(false)).ConfigureAwait(false);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheProviderIsDisposedDuringTheRequest_ReturnsTheFallbackValueWithoutWaitingForTheTimeout()
	{
		var disposal = new ProviderLifetime();
		var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var client = new FakeLanguageServerClient
		{
			SendRequestHandler = async (_, _, cancellationToken) =>
			{
				requestStarted.TrySetResult();
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

				return null;
			},
		};

		RequestRunner runner = CreateRunner(client, restartThreshold: 1, requestTimeout: TimeSpan.FromMinutes(5), disposal: disposal);

		Task<string> request = runner.SendAsync(
			method: "textDocument/hover",
			parameters: new object(),
			fallbackValue: "fallback",
			cancellationToken: CancellationToken.None
		);

		await requestStarted.Task.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		disposal.Signal();

		// The disposal token is linked into the attempt, so the request ends at once instead of at its five-minute timeout.
		Assert.AreEqual("fallback", await request.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false));
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheRequestTimesOut_ReturnsTheFallbackValueAndCountsOneTimeout()
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 3, SendRequestHandler = DelayUntilCanceled };
		RequestRunner runner = CreateRunner(client, restartThreshold: 2);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenTimeoutsReachTheThreshold_MarksTheConnectionUnhealthyOncePerGeneration()
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 5, SendRequestHandler = DelayUntilCanceled };
		RequestRunner runner = CreateRunner(client, restartThreshold: 2);

		for (int i = 0; i < 3; i++)
		{
			await runner
				.SendAsync(
					method: "textDocument/hover",
					parameters: new object(),
					fallbackValue: "fallback",
					cancellationToken: CancellationToken.None
				)
				.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
				.ConfigureAwait(false);
		}

		Assert.AreSequenceEqual([5], [.. client.MarkedUnhealthyGenerations]);
	}

	[TestMethod]
	public async Task SendAsync_WithTimeoutRestartsDisabled_NeverMarksTheConnectionUnhealthy()
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 7, SendRequestHandler = DelayUntilCanceled };
		RequestRunner runner = CreateRunner(client, restartThreshold: 0);

		for (int i = 0; i < 3; i++)
		{
			string? result = await runner
				.SendAsync(
					method: "textDocument/hover",
					parameters: new object(),
					fallbackValue: "fallback",
					cancellationToken: CancellationToken.None
				)
				.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.AreEqual("fallback", result);
		}

		// A zero threshold disables timeout-driven restarts: repeated timeouts stay observable only as
		// fallbacks and never mark the connection unhealthy.
		Assert.AreEqual(3, client.SendRequestCount);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheMarkAttemptIsRejectedByTheClient_StillReturnsTheFallbackValue()
	{
		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 5,
			TryMarkConnectionUnhealthyResult = false,
			SendRequestHandler = DelayUntilCanceled,
		};

		RequestRunner runner = CreateRunner(client, restartThreshold: 1);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreSequenceEqual([5], [.. client.MarkedUnhealthyGenerations]);
	}

	[TestMethod]
	public async Task SendAsync_WhenTimeoutsSpanTwoGenerations_CountsThemPerGeneration()
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 5, SendRequestHandler = DelayUntilCanceled };
		RequestRunner runner = CreateRunner(client, restartThreshold: 2);

		await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		client.ConnectionGeneration = 6;

		await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenARejectionBreaksTheTimeoutStreak_DoesNotMarkTheConnectionUnhealthy()
	{
		int callCount = 0;

		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 5,

			SendRequestHandler = (method, parameters, cancellationToken) =>
			{
				callCount++;

				return callCount == 2
					? Task.FromException<object?>(new LanguageServerRequestRejectedException(-32603, "Simulated rejection.", null))
					: DelayUntilCanceled(method, parameters, cancellationToken);
			},
		};

		RequestRunner runner = CreateRunner(client, restartThreshold: 2);

		// timeout -> rejection -> timeout: the rejection proves the connection is responsive, so the streak must
		// restart instead of reaching the threshold and forcing a connection restart.
		for (int i = 0; i < 3; i++)
		{
			await runner
				.SendAsync(
					method: "textDocument/hover",
					parameters: new object(),
					fallbackValue: "fallback",
					cancellationToken: CancellationToken.None
				)
				.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
				.ConfigureAwait(false);
		}

		Assert.AreEqual(3, client.SendRequestCount);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task TimeoutReset_ClearsTheConsecutiveTimeoutCount()
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 5, SendRequestHandler = DelayUntilCanceled };
		var timeouts = new RequestTimeoutCounter(restartThreshold: 2);

		RequestRunner runner = CreateRunner(client, restartThreshold: 2, timeouts: timeouts);

		await runner
			.SendAsync(method: "m", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		timeouts.Reset(5);

		await runner
			.SendAsync(method: "m", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.IsEmpty(client.MarkedUnhealthyGenerations);

		await runner
			.SendAsync(method: "m", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreSequenceEqual([5], [.. client.MarkedUnhealthyGenerations]);
	}

	[TestMethod]
	public async Task TimeoutReset_FromASupersededGeneration_DoesNotClearTheActiveStreak()
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 5, SendRequestHandler = DelayUntilCanceled };
		var timeouts = new RequestTimeoutCounter(restartThreshold: 2);

		RequestRunner runner = CreateRunner(client, restartThreshold: 2, timeouts: timeouts);

		await runner
			.SendAsync(method: "m", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		client.ConnectionGeneration = 6;

		await runner
			.SendAsync(method: "m", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		// A late settlement from the superseded generation must not wipe the active generation's streak,
		// or stragglers overlapping a restart could evade the restart threshold.
		timeouts.Reset(5);

		await runner
			.SendAsync(method: "m", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreSequenceEqual([6], [.. client.MarkedUnhealthyGenerations]);
	}

	[TestMethod]
	public async Task SendAsync_WhenAnUnownedCancellationOccurs_ReturnsTheFallbackValueWithoutMarking()
	{
		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 5,
			SendRequestHandler = (_, _, _) => throw new OperationCanceledException(),
		};

		RequestRunner runner = CreateRunner(client, restartThreshold: 1);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheClientIsDisposedMidSend_ReturnsTheFallbackValue()
	{
		var disposal = new ProviderLifetime();

		var client = new FakeLanguageServerClient
		{
			// The provider starts disposing while the send is in flight, and the client reports its own disposal.
			SendRequestHandler = (_, _, _) =>
			{
				disposal.Signal();
				throw new ObjectDisposedException(nameof(FakeLanguageServerClient));
			},
		};

		RequestRunner runner = CreateRunner(client, disposal: disposal);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
	}

	[TestMethod]
	public async Task SendAsync_WhenObjectDisposedIsRaisedWithoutDisposalState_PropagatesTheException()
	{
		var client = new FakeLanguageServerClient
		{
			SendRequestHandler = (_, _, _) => throw new ObjectDisposedException(nameof(FakeLanguageServerClient)),
		};

		RequestRunner runner = CreateRunner(client);

		await Assert
			.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
				await runner
					.SendAsync(
						method: "textDocument/hover",
						parameters: new object(),
						fallbackValue: "fallback",
						cancellationToken: CancellationToken.None
					)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheConnectionChanged_RetriesOnceOnTheRefreshedGeneration()
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 7 };
		int handlerCallCount = 0;

		client.SendRequestHandler = (_, _, _) =>
		{
			handlerCallCount++;

			if (handlerCallCount == 1)
				throw new LanguageServerConnectionSupersededException();

			return Task.FromResult<object?>("response");
		};

		int ensureStartedCount = 0;

		RequestRunner runner = CreateRunner(
			client,

			ensureConnectionStartedAsync: _ =>
			{
				ensureStartedCount++;
				client.ConnectionGeneration = 8;
				return Task.FromResult(true);
			}
		);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("response", result);
		Assert.AreEqual(2, client.SendRequestCount);
		Assert.AreEqual(1, ensureStartedCount);
		Assert.AreSequenceEqual([7, 8], [.. client.SendRequestGenerations]);
	}

	[TestMethod]
	public async Task SendAsync_WhenEnsureStartedFails_ReturnsTheFallbackValueWithoutRetrying()
	{
		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 7,
			SendRequestHandler = (_, _, _) => throw new LanguageServerConnectionUnavailableException(),
		};

		RequestRunner runner = CreateRunner(client, ensureConnectionStartedAsync: _ => Task.FromResult(false));

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheRetryAlsoFailsWithConnectionUnavailable_ReturnsTheFallbackValue()
	{
		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 7,
			SendRequestHandler = (_, _, _) => throw new LanguageServerConnectionUnavailableException(),
		};

		RequestRunner runner = CreateRunner(client);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(2, client.SendRequestCount);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheRetryTimesOut_RecordsTheTimeoutAgainstTheRefreshedGeneration()
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 7 };
		int handlerCallCount = 0;

		client.SendRequestHandler = async (_, _, cancellationToken) =>
		{
			handlerCallCount++;

			if (handlerCallCount == 1)
				throw new LanguageServerConnectionUnavailableException();

			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			return null;
		};

		RequestRunner runner = CreateRunner(client, restartThreshold: 1);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(2, client.SendRequestCount);
		Assert.AreSequenceEqual([7], [.. client.MarkedUnhealthyGenerations]);
	}

	[TestMethod]
	public async Task SendAsync_WhenNoReadySessionIsReportedOnThePrimaryAttempt_RetriesOnce()
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 7 };
		int handlerCallCount = 0;

		client.SendRequestHandler = (_, _, _) =>
		{
			handlerCallCount++;

			if (handlerCallCount == 1)
				throw new IOException("The language server connection is not ready.");

			return Task.FromResult<object?>("response");
		};

		RequestRunner runner = CreateRunner(client);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("response", result);
		Assert.AreEqual(2, client.SendRequestCount);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenNoReadySessionPersists_ReturnsTheFallbackValue()
	{
		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 7,
			SendRequestHandler = (_, _, _) => throw new IOException("The language server connection is not ready."),
		};

		RequestRunner runner = CreateRunner(client);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(2, client.SendRequestCount);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheServerRejectsTheRequest_ReturnsTheFallbackValueWithoutRetrying()
	{
		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 7,
			SendRequestHandler = (_, _, _) => throw new LanguageServerRequestRejectedException(-32602, "Invalid parameters.", null),
		};

		RequestRunner runner = CreateRunner(client);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	[DataRow(-32801, LogLevel.Debug)]
	[DataRow(-32800, LogLevel.Debug)]
	[DataRow(-32602, LogLevel.Warning)]
	public async Task SendAsync_WhenTheServerRejectsTheRequest_LogsADroppedRequestAtDebugAndAFaultAsAWarning(int errorCode, LogLevel expectedLevel)
	{
		using var logger = new RecordingLogger();

		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 7,
			SendRequestHandler = (_, _, _) => throw new LanguageServerRequestRejectedException(errorCode, "Rejected.", null),
		};

		RequestRunner runner = CreateRunner(client, logger: logger);

		await runner
			.SendAsync(
				method: "textDocument/semanticTokens/full",
				parameters: new object(),
				fallbackValue: "fallback",
				cancellationToken: CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.AreEqual(expectedLevel, logger.Entries.Single().Level);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheResponsePayloadCannotBeProcessed_ReturnsTheFallbackValueWithoutMarking()
	{
		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 5,
			SendRequestHandler = (_, _, _) => throw new JsonException("Simulated unprocessable response payload."),
		};

		RequestRunner runner = CreateRunner(client);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		// An unusable response payload is a completed exchange with a responsive connection: the documented fallback
		// value is returned and the connection is not marked unhealthy.
		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendOnceAsync_ARequestThatOutlastsTheRequestTimeout_IsNotAbandonedAndNotCounted()
	{
		var timeouts = new RequestTimeoutCounter(restartThreshold: 1);

		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 4,

			SendRequestHandler = async (_, _, cancellationToken) =>
			{
				await Task.Delay(TimeSpan.FromMilliseconds(400.0), cancellationToken).ConfigureAwait(false);
				return "done";
			},
		};

		RequestRunner runner = CreateRunner(client, requestTimeout: TimeSpan.FromMilliseconds(50.0), restartThreshold: 1, timeouts: timeouts);

		(bool succeeded, string? response) = await runner
			.SendOnceAsync<string>("workspace/executeCommand", new object(), CancellationToken.None)
			.ConfigureAwait(false);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("done", response);
		Assert.AreEqual(1, client.SendRequestCount);
		Assert.AreEqual(0, timeouts.ConsecutiveTimeouts);
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task SendOutcomeAsync_ARequestThatOutlastsTheRequestTimeout_IsAbandonedAndCounted()
	{
		var timeouts = new RequestTimeoutCounter(restartThreshold: 5);

		var client = new FakeLanguageServerClient { ConnectionGeneration = 4, SendRequestHandler = DelayUntilCanceled };

		RequestRunner runner = CreateRunner(client, requestTimeout: TimeSpan.FromMilliseconds(50.0), restartThreshold: 5, timeouts: timeouts);

		(bool succeeded, _) = await runner.SendOutcomeAsync<string>("textDocument/hover", new object(), CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(succeeded);
		Assert.AreEqual(1, timeouts.ConsecutiveTimeouts);
	}

	[TestMethod]
	public async Task SendOnceAsync_WhenTheCallerCancels_ThrowsOperationCanceled()
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 4, SendRequestHandler = DelayUntilCanceled };
		RequestRunner runner = CreateRunner(client, requestTimeout: TimeSpan.FromMilliseconds(50.0));

		using var cancellationTokenSource = new CancellationTokenSource();

		Task<(bool Succeeded, string? Response)> requestTask = runner.SendOnceAsync<string>(
			"workspace/executeCommand",
			new object(),
			cancellationTokenSource.Token
		);

		// The request outlives the request timeout, so only the caller's token can end it.
		await Task.Delay(TimeSpan.FromMilliseconds(200.0), TestContext.CancellationToken).ConfigureAwait(false);
		Assert.IsFalse(requestTask.IsCompleted);

		cancellationTokenSource.Cancel();

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await requestTask.ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendOnceAsync_WhenTheProviderIsDisposed_ReturnsNotSucceeded()
	{
		var disposal = new ProviderLifetime();
		var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 4,

			SendRequestHandler = async (_, _, cancellationToken) =>
			{
				requestStarted.TrySetResult();
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

				return null;
			},
		};

		RequestRunner runner = CreateRunner(client, disposal: disposal);

		Task<(bool Succeeded, string? Response)> requestTask = runner.SendOnceAsync<string>(
			"workspace/executeCommand",
			new object(),
			CancellationToken.None
		);

		await requestStarted.Task.ConfigureAwait(false);
		disposal.Signal();

		(bool succeeded, _) = await requestTask.ConfigureAwait(false);

		Assert.IsFalse(succeeded);
	}

	[TestMethod]
	[DataRow(typeof(LanguageServerConnectionUnavailableException))]
	[DataRow(typeof(LanguageServerConnectionSupersededException))]
	[DataRow(typeof(IOException))]
	public async Task SendOnceAsync_AtAConnectionBoundary_IsNotSentAgain(Type boundary)
	{
		int ensureStartedCount = 0;

		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 7,
			SendRequestHandler = (_, _, _) => throw (Exception)Activator.CreateInstance(boundary)!,
		};

		RequestRunner runner = CreateRunner(
			client,

			ensureConnectionStartedAsync: _ =>
			{
				ensureStartedCount++;
				return Task.FromResult(true);
			}
		);

		(bool succeeded, string? response) = await runner
			.SendOnceAsync<string>("workspace/executeCommand", new object(), CancellationToken.None)
			.ConfigureAwait(false);

		// The server may already have acted on the first send, so the runner reports no answer and does not send the request again.
		Assert.IsFalse(succeeded);
		Assert.IsNull(response);
		Assert.AreEqual(1, client.SendRequestCount);
		Assert.AreEqual(0, ensureStartedCount);
	}

	[TestMethod]
	[DataRow(typeof(LanguageServerConnectionUnavailableException))]
	[DataRow(typeof(LanguageServerConnectionSupersededException))]
	public async Task SendOutcomeAsync_AtAConnectionBoundary_IsSentAgainOnceTheConnectionIsReady(Type boundary)
	{
		int handlerCalls = 0;

		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 7,

			SendRequestHandler = (_, _, _) =>
				++handlerCalls == 1 ? throw (Exception)Activator.CreateInstance(boundary)! : Task.FromResult<object?>("again"),
		};

		RequestRunner runner = CreateRunner(client);

		(bool succeeded, string? response) = await runner
			.SendOutcomeAsync<string>("textDocument/hover", new object(), CancellationToken.None)
			.ConfigureAwait(false);

		Assert.IsTrue(succeeded);
		Assert.AreEqual("again", response);
		Assert.AreEqual(2, client.SendRequestCount);
	}

	// The per-document coordinator relies on these two contract tests: it logs a warning for every exception that reaches it, so a
	// failure the runner stops converting would surface there.
	[TestMethod]
	[DataRow(typeof(IOException))]
	[DataRow(typeof(LanguageServerConnectionUnavailableException))]
	[DataRow(typeof(LanguageServerConnectionSupersededException))]
	[DataRow(typeof(LanguageServerRequestRejectedException))]
	[DataRow(typeof(JsonException))]
	[DataRow(typeof(OperationCanceledException))]
	public async Task SendAsync_WhenTheClientFailsOnTheConnectionOrTheResponse_ReturnsTheFallbackValueAndThrowsNothing(Type failure)
	{
		var client = new FakeLanguageServerClient { ConnectionGeneration = 7, SendRequestHandler = (_, _, _) => throw CreateFailure(failure) };

		RequestRunner runner = CreateRunner(client);

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
	}

	[TestMethod]
	[DataRow(typeof(IOException))]
	[DataRow(typeof(ObjectDisposedException))]
	[DataRow(typeof(OperationCanceledException))]
	public async Task SendAsync_WhenPreparingTheRetryFails_ReturnsTheFallbackValueAndThrowsNothing(Type failure)
	{
		var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 7,
			SendRequestHandler = (_, _, _) => throw new LanguageServerConnectionUnavailableException(),
		};

		RequestRunner runner = CreateRunner(client, ensureConnectionStartedAsync: _ => throw CreateFailure(failure));

		string? result = await runner
			.SendAsync(method: "textDocument/hover", parameters: new object(), fallbackValue: "fallback", cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(1, client.SendRequestCount);
	}

	private static Exception CreateFailure(Type failure)
	{
		if (failure == typeof(LanguageServerRequestRejectedException))
			return new LanguageServerRequestRejectedException(-32603, "Rejected.", null);

		if (failure == typeof(ObjectDisposedException))
			return new ObjectDisposedException(nameof(FakeLanguageServerClient));

		return (Exception)Activator.CreateInstance(failure)!;
	}

	public TestContext? TestContext { get; set; }
}
