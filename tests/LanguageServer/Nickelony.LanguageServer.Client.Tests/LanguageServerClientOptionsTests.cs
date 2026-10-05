namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class LanguageServerClientOptionsTests
{
	[TestMethod]
	public void WithNullSettingsProvider_ThrowsArgumentNullException()
	{
		// A settings provider is always required: the default pushes an empty payload, so an explicit null is a
		// contract violation the options reject at assignment instead of failing the later handshake.
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			LanguageServerClientOptions.Default with
			{
				SettingsProvider = null!,
			}
		);

		Assert.AreEqual("SettingsProvider", exception.ParamName);
	}

	[TestMethod]
	public void RejectsInvalidTimeoutValues()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			LanguageServerClientOptions.Default with
			{
				InitializeRequestTimeout = TimeSpan.Zero,
			}
		);

		ArgumentOutOfRangeException infiniteTimeoutException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			LanguageServerClientOptions.Default with
			{
				ShutdownRequestTimeout = Timeout.InfiniteTimeSpan,
			}
		);

		Assert.Contains("Infinite timeouts are not supported", infiniteTimeoutException.Message);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			LanguageServerClientOptions.Default with
			{
				DisposeWaitTimeout = TimeSpan.FromMilliseconds(-1),
			}
		);
	}

	[TestMethod]
	public void RejectsTimeoutsAboveThePlatformTimerLimit()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			LanguageServerClientOptions.Default with
			{
				InitializeRequestTimeout = TimeSpan.FromDays(50),
			}
		);

		Assert.Contains("platform limit", exception.Message);
	}

	[TestMethod]
	public void Default_HasTheDocumentedDefaults()
	{
		LanguageServerClientOptions defaults = LanguageServerClientOptions.Default;

		Assert.AreEqual(TimeSpan.FromSeconds(20), defaults.InitializeRequestTimeout);
		Assert.IsTrue(defaults.RequireTextDocumentSynchronization);
		Assert.IsEmpty(defaults.SessionStartNotificationsProvider([TestPaths.Root]));
	}

	[TestMethod]
	public void SessionStartNotificationsProvider_RejectsNull()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			LanguageServerClientOptions.Default with
			{
				SessionStartNotificationsProvider = null!,
			}
		);

		Assert.AreEqual("SessionStartNotificationsProvider", exception.ParamName);
	}

	[TestMethod]
	public void ClientCapabilitiesProvider_RejectsNull()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			LanguageServerClientOptions.Default with
			{
				ClientCapabilitiesProvider = null!,
			}
		);

		Assert.AreEqual("ClientCapabilitiesProvider", exception.ParamName);
	}

	[TestMethod]
	public void InitializationOptionsProvider_RejectsNull()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			LanguageServerClientOptions.Default with
			{
				InitializationOptionsProvider = null!,
			}
		);

		Assert.AreEqual("InitializationOptionsProvider", exception.ParamName);
	}
}
