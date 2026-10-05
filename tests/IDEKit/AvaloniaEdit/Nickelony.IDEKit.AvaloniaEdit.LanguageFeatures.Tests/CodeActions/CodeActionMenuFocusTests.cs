using Avalonia.Controls;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>Covers the keyboard state of the code-action menu that the Avalonia popup leaves to the presenter.</summary>
[STATestClass]
public sealed class CodeActionMenuFocusTests
{
	[TestMethod]
	public void TryOpenMenu_GivesTheFirstItemTheFocusAndTheSelection_SoEnterAppliesIt()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) =>
				Task.FromResult<IReadOnlyList<CodeAction>>([
					CodeActionPresenterTestHost.CreateItem("Fix it"),
					CodeActionPresenterTestHost.CreateItem("Extract"),
				]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(presenter.TryOpenMenu());

		ContextMenu menu = host.Menus[^1];
		var first = (MenuItem)menu.Items[0]!;

		DispatcherTestHelpers.PumpUntil(() => first.IsFocused);

		Assert.AreEqual(0, menu.SelectedIndex);
		Assert.IsFalse(((MenuItem)menu.Items[1]!).IsFocused);
	}

	[TestMethod]
	public void TryOpenMenu_ThatTheHostClosedMeanwhile_LeavesNoFocusBehind()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(presenter.TryOpenMenu());

		ContextMenu menu = host.Menus[^1];
		presenter.CloseMenu();
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsFalse(((MenuItem)menu.Items[0]!).IsFocused);
	}
}
