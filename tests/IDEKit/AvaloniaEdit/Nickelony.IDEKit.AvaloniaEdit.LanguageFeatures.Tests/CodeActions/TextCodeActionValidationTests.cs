namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// The AvaloniaEdit binding of the shared <see cref="TextCodeActionValidationTestsBase"/> validation
/// scenarios; Avalonia's dispatcher rejects the off-thread construction without a COM apartment, so the
/// worker thread needs no setup.
/// </summary>
[AvaloniaTestClass]
public sealed class TextCodeActionValidationTests : TextCodeActionValidationTestsBase
{
	protected override void ConfigureWorkerThread(Thread thread)
	{
		// Avalonia (unlike WPF) does not require the COM single-threaded apartment, so no setup is needed.
	}
}
