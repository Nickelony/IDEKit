using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class BackgroundFailurePolicyTests
{
	private static ProviderLifetime Disposing()
	{
		var disposal = new ProviderLifetime();

		disposal.Signal();

		return disposal;
	}

	[TestMethod]
	public void LevelOf_AnObjectDisposedExceptionAfterTheDisposalStarted_IsSilent() =>
		Assert.IsNull(BackgroundFailurePolicy.LevelOf(new ObjectDisposedException("client"), Disposing()));

	[TestMethod]
	public void LevelOf_AnObjectDisposedExceptionWhileTheProviderIsLive_IsADebugDiagnostic() =>
		Assert.AreEqual(LogLevel.Debug, BackgroundFailurePolicy.LevelOf(new ObjectDisposedException("client"), new ProviderLifetime()));

	[TestMethod]
	[DataRow(typeof(IOException), false)]
	[DataRow(typeof(IOException), true)]
	[DataRow(typeof(LanguageServerConnectionUnavailableException), false)]
	[DataRow(typeof(LanguageServerConnectionUnavailableException), true)]
	public void LevelOf_AConnectionFailure_IsADebugDiagnosticWhetherOrNotTheProviderIsDisposing(Type failure, bool disposing)
	{
		ProviderLifetime disposal = disposing ? Disposing() : new ProviderLifetime();
		Assert.AreEqual(LogLevel.Debug, BackgroundFailurePolicy.LevelOf((Exception)Activator.CreateInstance(failure)!, disposal));
	}

	[TestMethod]
	[DataRow(typeof(InvalidOperationException), false)]
	[DataRow(typeof(InvalidOperationException), true)]
	[DataRow(typeof(JsonException), false)]
	[DataRow(typeof(JsonException), true)]
	public void LevelOf_AnyOtherFailure_IsAWarningWhetherOrNotTheProviderIsDisposing(Type failure, bool disposing)
	{
		ProviderLifetime disposal = disposing ? Disposing() : new ProviderLifetime();
		Assert.AreEqual(LogLevel.Warning, BackgroundFailurePolicy.LevelOf((Exception)Activator.CreateInstance(failure)!, disposal));
	}
}
