namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the range checks of the provider options, which each property runs when it is assigned.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderOptionsTests
{
	[TestMethod]
	public void Default_HoldsTheDocumentedValues()
	{
		LanguageServerFeatureProviderOptions options = LanguageServerFeatureProviderOptions.Default;

		Assert.AreEqual(TimeSpan.FromSeconds(10), options.RequestTimeout);
		Assert.AreEqual(2, options.RequestTimeoutRestartThreshold);
		Assert.AreEqual(3, options.HardStartupFailureThreshold);
		Assert.AreEqual(16, options.MaxTrackedIdleDocuments);
		Assert.AreEqual(LocalPathComparisonPolicy.ForCurrentPlatform, options.PathComparison);
	}

	[TestMethod]
	public void RequestTimeout_NotGreaterThanZero_ThrowsArgumentOutOfRangeException()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new LanguageServerFeatureProviderOptions { RequestTimeout = TimeSpan.Zero });

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			_ = new LanguageServerFeatureProviderOptions { RequestTimeout = TimeSpan.FromMilliseconds(-2) }
		);
	}

	[TestMethod]
	public void RequestTimeout_BeyondInt32Milliseconds_ThrowsArgumentOutOfRangeException()
	{
		var tooLarge = TimeSpan.FromMilliseconds((double)int.MaxValue + 1);

		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			_ = new LanguageServerFeatureProviderOptions { RequestTimeout = tooLarge }
		);

		Assert.AreEqual(tooLarge, exception.ActualValue);
	}

	[TestMethod]
	public void RequestTimeout_AtItsBounds_IsAccepted()
	{
		Assert.AreEqual(TimeSpan.FromTicks(1), new LanguageServerFeatureProviderOptions { RequestTimeout = TimeSpan.FromTicks(1) }.RequestTimeout);

		Assert.AreEqual(
			TimeSpan.FromMilliseconds(int.MaxValue),
			new LanguageServerFeatureProviderOptions { RequestTimeout = TimeSpan.FromMilliseconds(int.MaxValue) }.RequestTimeout
		);
	}

	[TestMethod]
	public void RequestTimeoutRestartThreshold_Negative_ThrowsArgumentOutOfRangeException() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new LanguageServerFeatureProviderOptions { RequestTimeoutRestartThreshold = -1 });

	[TestMethod]
	public void RequestTimeoutRestartThreshold_Zero_IsAcceptedAndDisablesTimeoutRestarts() =>
		Assert.AreEqual(0, new LanguageServerFeatureProviderOptions { RequestTimeoutRestartThreshold = 0 }.RequestTimeoutRestartThreshold);

	[TestMethod]
	public void HardStartupFailureThreshold_BelowOne_ThrowsArgumentOutOfRangeException() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new LanguageServerFeatureProviderOptions { HardStartupFailureThreshold = 0 });

	[TestMethod]
	public void HardStartupFailureThreshold_One_IsAccepted() =>
		Assert.AreEqual(1, new LanguageServerFeatureProviderOptions { HardStartupFailureThreshold = 1 }.HardStartupFailureThreshold);

	[TestMethod]
	public void MaxTrackedIdleDocuments_Negative_ThrowsArgumentOutOfRangeException() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new LanguageServerFeatureProviderOptions { MaxTrackedIdleDocuments = -1 });

	[TestMethod]
	public void MaxTrackedIdleDocuments_Zero_IsAcceptedAndDisablesIdleRetention() =>
		Assert.AreEqual(0, new LanguageServerFeatureProviderOptions { MaxTrackedIdleDocuments = 0 }.MaxTrackedIdleDocuments);

	[TestMethod]
	public void With_AnOutOfRangeValue_ThrowsArgumentOutOfRangeException()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			_ = LanguageServerFeatureProviderOptions.Default with { MaxTrackedIdleDocuments = -1 }
		);
	}

	[TestMethod]
	public void With_AValidValue_KeepsTheOtherValues()
	{
		LanguageServerFeatureProviderOptions options = LanguageServerFeatureProviderOptions.Default with { HardStartupFailureThreshold = 5 };

		Assert.AreEqual(5, options.HardStartupFailureThreshold);
		Assert.AreEqual(LanguageServerFeatureProviderOptions.Default.RequestTimeout, options.RequestTimeout);
		Assert.AreEqual(LanguageServerFeatureProviderOptions.Default.MaxTrackedIdleDocuments, options.MaxTrackedIdleDocuments);
	}
}
