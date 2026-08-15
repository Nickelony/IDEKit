#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Covers the change-notification contract of the code-action controller: the margin repaints only when
/// the indicator state actually changed, so a request that publishes nothing does not raise
/// <see cref="TextCodeActionController.Changed"/>.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextCodeActionControllerNotificationTests
{
	[TestMethod]
	public void RefreshAsync_NoPublishedState_DoesNotRaiseChanged()
	{
		using var host = new CodeActionTestHost();
		using var controller = host.CreateController();

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		// The default host request returns an empty list, so the controller publishes nothing and clears
		// nothing; the indicator was never shown, so no change notification belongs to this refresh.
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(0, changedCount);
	}
}
