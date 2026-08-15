#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using FontWeights = Avalonia.Media.FontWeight;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FontWeights = System.Windows.FontWeights;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextCodeActionControllerMenuTests
{
	[TestMethod]
	public void TryOpenActions_WithActions_OpensTheMenuWithTheSkinAndItems()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>(
		[
			CodeActionTestHost.CreateItem("Fix it", isPreferred: true),
			CodeActionTestHost.CreateItem("Extract expression", kind: "refactor.extract")
		]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsTrue(controller.TryOpenActions());
		Assert.IsTrue(controller.IsActionsOpen);
		Assert.IsTrue(host.ConfigureMenuInvoked);

		ContextMenu menu = host.Menus[^1];

		Assert.AreSame(host.Editor.TextArea, menu.PlacementTarget);
		Assert.AreSame(Brushes.Black, menu.Background);
		Assert.AreSame(Brushes.White, menu.Foreground);
		Assert.AreSame(Brushes.Gray, menu.BorderBrush);
		Assert.AreEqual(TextCodeActionMenuOptions.Default.MaxHeight, menu.MaxHeight);
#if AVALONIAEDIT

		// Avalonia's ContextMenu is always light-dismissed, so the WPF StaysOpen = false flag has no
		// equivalent property to assert; the light-dismiss behavior is covered by the external-close test
		// below instead of by a menu property.
#else
		Assert.IsFalse(menu.StaysOpen);
#endif
		Assert.AreEqual(2, menu.Items.Count);

		var preferredItem = (MenuItem)menu.Items[0]!;
		var plainItem = (MenuItem)menu.Items[1]!;

		Assert.AreEqual("Fix it", ((TextBlock)preferredItem.Header!).Text);
		Assert.AreEqual(FontWeights.Bold, ((TextBlock)preferredItem.Header!).FontWeight);
		Assert.AreEqual("Extract expression", ((TextBlock)plainItem.Header!).Text);
		Assert.AreEqual(FontWeights.Normal, ((TextBlock)plainItem.Header!).FontWeight);

		controller.CloseActions();

		Assert.IsFalse(controller.IsActionsOpen);
	}

	[TestMethod]
	public void MenuItemClick_InvokesTheExecuteHookWithTheItem()
	{
		using var host = new CodeActionTestHost();
		var payload = new object();
		host.RequestCodeActionsAsync = (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>(
			[CodeActionTestHost.CreateItem("Fix it", payload: payload)]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		var menuItem = (MenuItem)host.Menus[^1].Items[0]!;

		menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

		Assert.AreEqual(1, host.Executed.Count);
		Assert.AreSame(payload, host.Executed[0].Payload);
	}

	[TestMethod]
	public void TryOpenActions_Twice_ReplacesTheOpenMenu()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsTrue(controller.TryOpenActions());
		Assert.IsTrue(controller.TryOpenActions());

		Assert.AreEqual(2, host.Menus.Count);
		Assert.IsFalse(host.Menus[0].IsOpen);
		Assert.IsTrue(host.Menus[1].IsOpen);
		Assert.IsTrue(controller.IsActionsOpen);
	}

	[TestMethod]
	public void Dispose_WhenMenuOpen_ClosesTheMenu()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		controller.Dispose();

		Assert.IsFalse(controller.IsActionsOpen);
	}

	[TestMethod]
	public void TryOpenActions_WithConfigureMenuItemHook_ConfiguresEveryCreatedItem()
	{
		using var host = new CodeActionTestHost();
#if AVALONIAEDIT

		// Avalonia tooltips are an attached property (ToolTip.Tip) instead of the WPF MenuItem.ToolTip
		// property, so the hook configures the item through ToolTip.SetTip and the assertion reads it back
		// through ToolTip.GetTip.
		host.ConfigureMenuItem = static (menuItem, item) => ToolTip.SetTip(menuItem, item.Title);
#else
		host.ConfigureMenuItem = static (menuItem, item) => menuItem.ToolTip = item.Title;
#endif
		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>(
		[
			CodeActionTestHost.CreateItem("Fix it", isPreferred: true),
			CodeActionTestHost.CreateItem("Extract expression")
		]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		ContextMenu menu = host.Menus[^1];

		Assert.IsTrue(host.ConfigureMenuItemInvoked);
		Assert.AreEqual(2, menu.Items.Count);
#if AVALONIAEDIT
		Assert.AreEqual("Fix it", ToolTip.GetTip((MenuItem)menu.Items[0]!));
		Assert.AreEqual("Extract expression", ToolTip.GetTip((MenuItem)menu.Items[1]!));
#else
		Assert.AreEqual("Fix it", ((MenuItem)menu.Items[0]).ToolTip);
		Assert.AreEqual("Extract expression", ((MenuItem)menu.Items[1]).ToolTip);
#endif
	}

	[TestMethod]
	public void TryOpenActions_CustomAnchorOptions_MoveTheMenuByTheConfiguredDelta()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		Point baselineAnchor;

		using (TextCodeActionController baseline = host.CreateController())
		{
			CodeActionTestHost.RunToCompletion(baseline.RefreshAsync());
			Assert.IsTrue(baseline.TryOpenActions());

			ContextMenu baselineMenu = host.Menus[^1];
			baselineAnchor = new Point(baselineMenu.HorizontalOffset, baselineMenu.VerticalOffset);
		}

		var menuOptions = TextCodeActionMenuOptions.Default with
		{
			AnchorXOffset = 12.0,
			CaretAnchorYOffset = 7.0
		};

		using var controller = host.CreateController(menuOptions: menuOptions);

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		ContextMenu menu = host.Menus[^1];

		// The configured offsets move the menu by the delta to the defaults, independent of the layout math
		// that produced the baseline position.
		Assert.AreEqual(baselineAnchor.X + 10.0, menu.HorizontalOffset, 1e-6);
		Assert.AreEqual(baselineAnchor.Y + 5.0, menu.VerticalOffset, 1e-6);
	}

	[TestMethod]
	public void ExternalMenuClose_UntracksTheMenuAndAllowsReopening()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsTrue(controller.TryOpenActions());
		Assert.IsTrue(controller.IsActionsOpen);

		// The menu closes itself (Escape, focus loss, or a click elsewhere) without the controller asking.
#if AVALONIAEDIT
		// Avalonia's ContextMenu.IsOpen setter is not public, so the external close is issued through
		// Close(), which raises the same Closed event a light-dismiss would.
		host.Menus[^1].Close();
#else
		host.Menus[^1].IsOpen = false;
#endif

		// The external close reaches the presenter through the menu's Closed event.
		DispatcherTestUtils.PumpUntil(() => !controller.IsActionsOpen);

		// The presenter untracked the closed menu, so the next open simply opens a fresh one.
		Assert.IsTrue(controller.TryOpenActions());
		Assert.IsTrue(controller.IsActionsOpen);
	}

	[TestMethod]
	public void CloseActions_RestoresTheFocusTheEditorHadBeforeTheMenuOpened()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		host.Editor.Focus();
		Assert.IsTrue(controller.TryOpenActions());
		Assert.IsTrue(controller.IsActionsOpen);

		controller.CloseActions();

		Assert.IsFalse(controller.IsActionsOpen);

		// The editor keeps working after the menu closes: keyboard focus returned to the element that had it
		// before the menu opened.
		Assert.IsTrue(host.Editor.IsKeyboardFocusWithin);
	}

	[TestMethod]
	public void ContextChange_ToDifferentLine_ClosesTheOpenMenu()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		ContextMenu menu = host.Menus[^1];

		// The caret moves to a line without actions; clearing the state must not leave an open menu whose
		// items belong to the previous line.
		host.Editor.CaretOffset = 4;

		Assert.IsFalse(controller.HasActions);
		Assert.IsFalse(controller.IsActionsOpen);
		Assert.IsFalse(menu.IsOpen);
	}

	[TestMethod]
	public void RefreshAsync_ReplacedActions_ClosesTheOpenMenu()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		ContextMenu menu = host.Menus[^1];
		TextCodeActionItem replacement = CodeActionTestHost.CreateItem("Extract expression");

		host.RequestCodeActionsAsync = (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([replacement]);

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		// The published snapshot was replaced, so the menu that presented the previous snapshot closes and
		// only the new items stay available.
		Assert.IsFalse(controller.IsActionsOpen);
		Assert.IsFalse(menu.IsOpen);
		Assert.AreSame(replacement, controller.Actions[0]);
	}

	[TestMethod]
	public void RefreshAsync_EmptyResult_ClosesTheOpenMenu()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		ContextMenu menu = host.Menus[^1];

		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]);
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.IsFalse(controller.IsActionsOpen);
		Assert.IsFalse(menu.IsOpen);
	}
}
