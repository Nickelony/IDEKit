using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed partial class ProviderContextTests
{
	private static string UnnormalizedScriptPath => Path.Combine(TestPaths.Root, "Scripts", "..", "Scripts", "a.test");

	[TestMethod]
	public void Constructor_ExposesTheSharedCollaboratorsItWasGivenAndNoRequestRunner()
	{
		using var client = new FakeLanguageServerClient();
		using var logger = new RecordingLogger();

		var documents = new DocumentTracker();
		var disposal = new ProviderLifetime();

		LanguageServerFeatureProviderOptions options = TestOptions.CaseInsensitive;

		var context = new ProviderContext(client, documents, disposal, providerDisplayName: "Example", options, logger);

		Assert.AreSame(client, context.Client);
		Assert.AreSame(documents, context.Documents);
		Assert.AreSame(disposal, context.Disposal);
		Assert.AreSame(options, context.Options);
		Assert.AreSame(logger, context.Logger);
		Assert.AreEqual("Example", context.ProviderDisplayName);

		// The request runner is a collaborator of the four features that send requests, not a member of what every collaborator shares.
		Assert.IsNull(
			typeof(ProviderContext).GetProperty("RequestRunner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
		);
	}

	[TestMethod]
	public void TryNormalizeDocumentEntryPath_WithAUsablePath_ReturnsTheNormalizedPathWithoutLogging()
	{
		using var logger = new RecordingLogger();
		ProviderContext context = TestProviderContext.Create(client: null, logger);

		bool normalized = context.TryNormalizeDocumentEntryPath(UnnormalizedScriptPath, "open", out string? normalizedFilePath);

		Assert.IsTrue(normalized);
		Assert.AreEqual(TestPaths.Script("a.test"), normalizedFilePath);
		Assert.IsEmpty(logger.Entries);
	}

	[TestMethod]
	[DataRow("relative.test")]
	[DataRow("")]
	[DataRow("   ")]
	public void TryNormalizeDocumentEntryPath_WithAnUnusablePath_ReturnsFalseAndLogsItWithTheOperationAtDebugLevel(string path)
	{
		using var logger = new RecordingLogger();
		ProviderContext context = TestProviderContext.Create(client: null, logger);

		bool normalized = context.TryNormalizeDocumentEntryPath(path, "open", out _);

		Assert.IsFalse(normalized);

		RecordingLogger.LogEntry entry = Assert.ContainsSingle(logger.Entries);

		Assert.AreEqual(LogLevel.Debug, entry.Level);
		Assert.Contains("Test open ignored the path", entry.Message);
		Assert.Contains($"'{path}'", entry.Message);
	}

	[TestMethod]
	public async Task ObserveBackgroundTask_ALoggedFault_IsReportedAsAWarningNamingTheOperation()
	{
		using var logger = new RecordingLogger();
		ProviderContext context = TestProviderContext.Create(client: null, logger);

		context.ObserveBackgroundTask(Task.FromException(new InvalidOperationException("boom")), "refresh");

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(() => logger.HasEntryAtLeast(LogLevel.Warning), TestPolling.DefaultTimeout).ConfigureAwait(false)
		);

		RecordingLogger.LogEntry entry = Assert.ContainsSingle(logger.Entries);

		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.Contains("'refresh'", entry.Message);
		Assert.AreEqual("boom", entry.ExceptionMessage);
	}

	[TestMethod]
	public async Task ObserveBackgroundTask_ACanceledTask_LogsNothing()
	{
		using var logger = new RecordingLogger();
		ProviderContext context = TestProviderContext.Create(client: null, logger);

		context.ObserveBackgroundTask(Task.FromCanceled(new CancellationToken(canceled: true)), "refresh");

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsEmpty(logger.Entries);
	}
}
