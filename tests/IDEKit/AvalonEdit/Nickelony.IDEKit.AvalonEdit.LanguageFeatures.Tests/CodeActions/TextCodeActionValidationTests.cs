namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// The AvalonEdit binding of the shared <see cref="TextCodeActionValidationTestsBase"/> validation
/// scenarios; it puts the worker thread into the COM single-threaded apartment WPF's dispatcher needs.
/// </summary>
[STATestClass]
public sealed class TextCodeActionValidationTests : TextCodeActionValidationTestsBase
{
	protected override void ConfigureWorkerThread(Thread thread)
		=> thread.SetApartmentState(ApartmentState.STA);
}
