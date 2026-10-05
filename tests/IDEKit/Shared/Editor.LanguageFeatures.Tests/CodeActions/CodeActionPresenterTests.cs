#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
using FontWeights = Avalonia.Media.FontWeight;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
using FontWeights = System.Windows.FontWeights;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Covers the code-action presenter: opening the themed menu for the controller's actions, anchoring it, closing it when the published
/// set goes stale, and the guards of its public members. The controller coordination is covered by the controller tests, which run without an editor.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CodeActionPresenterTests
{
	[TestMethod]
	public void MenuTheme_AssignedAfterConstruction_ColorsTheNextMenu()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		var theme = new CodeActionMenuTheme
		{
			Background = Brushes.Navy,
			Foreground = Brushes.Yellow,
			BorderBrush = Brushes.Red,
		};

		// A theme switch assigns the theme in place, so the presenter and its margin stay where they are.
		presenter.MenuTheme = theme;

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(presenter.TryOpenMenu());
		Assert.AreSame(theme, presenter.MenuTheme);
		Assert.AreSame(Brushes.Navy, host.Menus[^1].Background);
		Assert.AreSame(Brushes.Yellow, host.Menus[^1].Foreground);
		Assert.AreSame(Brushes.Red, host.Menus[^1].BorderBrush);
	}

	[TestMethod]
	public void TryOpenMenu_WithActions_OpensTheMenuWithTheThemeAndItems()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) =>
				Task.FromResult<IReadOnlyList<CodeAction>>([
					CodeActionPresenterTestHost.CreateItem("Fix it", isPreferred: true),
					CodeActionPresenterTestHost.CreateItem("Extract expression", kind: "refactor.extract"),
				]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(presenter.TryOpenMenu());
		Assert.IsTrue(presenter.IsMenuOpen);
		Assert.IsTrue(host.ConfigureMenuInvoked);

		ContextMenu menu = host.Menus[^1];

		Assert.AreSame(host.Editor.TextArea, menu.PlacementTarget);
		Assert.AreSame(Brushes.Black, menu.Background);
		Assert.AreSame(Brushes.White, menu.Foreground);
		Assert.AreSame(Brushes.Gray, menu.BorderBrush);
		Assert.AreEqual(CodeActionMenuOptions.Default.MaxHeight, menu.MaxHeight);

		CodeActionTestHelpers.AssertMenuDismissesOnOutsideInteraction(menu);
		Assert.AreEqual(2, menu.Items.Count);

		var preferredItem = (MenuItem)menu.Items[0]!;
		var plainItem = (MenuItem)menu.Items[1]!;

		Assert.AreEqual("Fix it", ((TextBlock)preferredItem.Header!).Text);
		Assert.AreEqual(FontWeights.Bold, ((TextBlock)preferredItem.Header!).FontWeight);
		Assert.AreEqual("Extract expression", ((TextBlock)plainItem.Header!).Text);
		Assert.AreEqual(FontWeights.Normal, ((TextBlock)plainItem.Header!).FontWeight);

		presenter.CloseMenu();

		Assert.IsFalse(presenter.IsMenuOpen);
	}

	[TestMethod]
	public void MenuItemClick_InvokesTheExecuteHookWithTheItem()
	{
		object payload = new();

		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = (_, _) =>
				Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it", payload: payload)]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(presenter.TryOpenMenu());

		var menuItem = (MenuItem)host.Menus[^1].Items[0]!;

		menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

		Assert.HasCount(1, host.Executed);
		Assert.AreSame(payload, host.Executed[0].Data);
	}

	[TestMethod]
	public void MenuItemClick_ThrowingExecuteHook_IsContainedAndLogged()
	{
		var logger = new RecordingLogger();

		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
			ExecuteActionAsync = static _ => throw new InvalidOperationException("Execute failed."),
		};

		using CodeActionController controller = host.CreateController(logger: logger);
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(presenter.TryOpenMenu());

		// Invoke the action through the real menu item so the execute pipeline runs.
		var menuItem = (MenuItem)host.Menus[^1].Items[0]!;

		menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menuItem));

		DispatcherTestHelpers.PumpUntil(() => logger.Entries.Count > 0);

		Assert.HasCount(1, host.Executed);
		Assert.AreEqual(1031, logger.Entries[^1].EventId.Id);
	}

	[TestMethod]
	public void TryOpenMenu_Twice_ReplacesTheOpenMenu()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(presenter.TryOpenMenu());
		Assert.IsTrue(presenter.TryOpenMenu());

		Assert.HasCount(2, host.Menus);
		Assert.IsFalse(host.Menus[0].IsOpen);
		Assert.IsTrue(host.Menus[1].IsOpen);
		Assert.IsTrue(presenter.IsMenuOpen);
	}

	[TestMethod]
	public void TryOpenMenu_WithoutActions_ReturnsFalse()
	{
		using var host = new CodeActionPresenterTestHost();

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		Assert.IsFalse(presenter.TryOpenMenu());
		Assert.IsFalse(presenter.IsMenuOpen);
	}

	[TestMethod]
	public void TryOpenMenu_EditorNotConnectedToPresentationSource_ReturnsFalse()
	{
		var editor = TestHost.CreateEditor("one");

		using CodeActionController controller = CodeActionPresenterTestHost.CreateUnhostedController(
			editor,
			static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")])
		);

		using var presenter = new CodeActionPresenter(editor.TextArea, controller, CodeActionPresenterTestHost.CreatePresentation());

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		Assert.IsFalse(presenter.TryOpenMenu());
		Assert.IsFalse(presenter.IsMenuOpen);
	}

	[TestMethod]
	public void TryOpenMenuAtAnchor_EditorNotConnectedToPresentationSource_ReturnsFalse()
	{
		var editor = TestHost.CreateEditor("one");

		using CodeActionController controller = CodeActionPresenterTestHost.CreateUnhostedController(
			editor,
			static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")])
		);

		using var presenter = new CodeActionPresenter(editor.TextArea, controller, CodeActionPresenterTestHost.CreatePresentation());

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		Assert.IsFalse(presenter.TryOpenMenu(1, new Point(0.0, 0.0)));
		Assert.IsFalse(presenter.IsMenuOpen);
	}

	[TestMethod]
	public void TryOpenMenuAtAnchor_IndicatorLine_OpensTheMenuAtTheAnchor()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		var anchor = new Point(12.0, 34.0);

		Assert.IsTrue(presenter.TryOpenMenu(1, anchor));
		Assert.IsTrue(presenter.IsMenuOpen);

		ContextMenu menu = host.Menus[^1];

		Assert.AreSame(host.Editor.TextArea, menu.PlacementTarget);
		CodeActionTestHelpers.AssertMenuPlacedAtAnchor(menu);

		Assert.AreEqual(12.0, menu.HorizontalOffset);
		Assert.AreEqual(34.0, menu.VerticalOffset);
	}

	[TestMethod]
	public void TryOpenMenuAtAnchor_LineWithoutIndicator_ReturnsFalse()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		// The caret is on line 1, so the indicator belongs to line 1; a host anchor for line 2 is stale.
		Assert.IsFalse(presenter.TryOpenMenu(2, new Point(0.0, 0.0)));
		Assert.IsFalse(presenter.IsMenuOpen);
	}

	[TestMethod]
	public void TryOpenMenu_WithConfigureMenuItemHook_ConfiguresEveryCreatedItem()
	{
		using var host = new CodeActionPresenterTestHost();
		host.ConfigureMenuItem = static (menuItem, item) => CodeActionTestHelpers.SetItemTooltip(menuItem, item.Title);

		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<CodeAction>>([
				CodeActionPresenterTestHost.CreateItem("Fix it", isPreferred: true),
				CodeActionPresenterTestHost.CreateItem("Extract expression"),
			]);

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(presenter.TryOpenMenu());

		ContextMenu menu = host.Menus[^1];

		Assert.IsTrue(host.ConfigureMenuItemInvoked);
		Assert.AreEqual(2, menu.Items.Count);
		Assert.AreEqual("Fix it", CodeActionTestHelpers.GetItemTooltip((MenuItem)menu.Items[0]!));
		Assert.AreEqual("Extract expression", CodeActionTestHelpers.GetItemTooltip((MenuItem)menu.Items[1]!));
	}

	[TestMethod]
	public void TryOpenMenu_CustomAnchorOptions_MoveTheMenuByTheConfiguredDelta()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();

		Point baselineAnchor;

		using (CodeActionPresenter baseline = host.CreatePresenter(controller))
		{
			CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
			Assert.IsTrue(baseline.TryOpenMenu());

			ContextMenu baselineMenu = host.Menus[^1];
			baselineAnchor = new Point(baselineMenu.HorizontalOffset, baselineMenu.VerticalOffset);
		}

		CodeActionMenuOptions menuOptions = CodeActionMenuOptions.Default with { AnchorXOffset = 12.0, CaretAnchorYOffset = 7.0 };

		using CodeActionPresenter presenter = host.CreatePresenter(controller, menuOptions);

		Assert.IsTrue(presenter.TryOpenMenu());

		ContextMenu menu = host.Menus[^1];

		// The configured offsets move the menu by the delta to the defaults, independent of the layout math
		// that produced the baseline position.
		Assert.AreEqual(baselineAnchor.X + 10.0, menu.HorizontalOffset, 1e-6);
		Assert.AreEqual(baselineAnchor.Y + 5.0, menu.VerticalOffset, 1e-6);
	}

	[TestMethod]
	public void ExternalMenuClose_UntracksTheMenuAndAllowsReopening()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(presenter.TryOpenMenu());
		Assert.IsTrue(presenter.IsMenuOpen);

		// The menu closes itself (Escape, focus loss, or a click elsewhere) without the presenter asking.
		CodeActionTestHelpers.CloseFromOutside(host.Menus[^1]);

		// The external close reaches the presenter through the menu's Closed event.
		DispatcherTestHelpers.PumpUntil(() => !presenter.IsMenuOpen);

		// The presenter untracked the closed menu, so the next open opens a fresh one.
		Assert.IsTrue(presenter.TryOpenMenu());
		Assert.IsTrue(presenter.IsMenuOpen);
	}

	[TestMethod]
	public void CloseMenu_RestoresTheFocusTheEditorHadBeforeTheMenuOpened()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		host.Editor.Focus();

		Assert.IsTrue(presenter.TryOpenMenu());
		Assert.IsTrue(presenter.IsMenuOpen);

		presenter.CloseMenu();

		Assert.IsFalse(presenter.IsMenuOpen);

		// The editor keeps working after the menu closes: keyboard focus returned to the element that had it
		// before the menu opened.
		Assert.IsTrue(host.Editor.IsKeyboardFocusWithin);
	}

	[TestMethod]
	public void ContextChange_ToDifferentLine_ClosesTheOpenMenu()
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

		// The caret moves to a line without actions; clearing the state must not leave an open menu whose
		// items belong to the previous line.
		host.Editor.CaretOffset = 4;

		Assert.IsFalse(controller.HasActions);
		Assert.IsFalse(presenter.IsMenuOpen);
		Assert.IsFalse(menu.IsOpen);
	}

	[TestMethod]
	public void RequestAsync_ReplacedActions_ClosesTheOpenMenu()
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
		CodeAction replacement = CodeActionPresenterTestHost.CreateItem("Extract expression");

		host.RequestCodeActionsAsync = (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([replacement]);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		// The published snapshot was replaced, so the menu that presented the previous snapshot closes and
		// only the new items stay available.
		Assert.IsFalse(presenter.IsMenuOpen);
		Assert.IsFalse(menu.IsOpen);
		Assert.AreSame(replacement, controller.Actions[0]);
	}

	[TestMethod]
	public void RequestAsync_EmptyResult_ClosesTheOpenMenu()
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

		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([]);
		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.IsFalse(presenter.IsMenuOpen);
		Assert.IsFalse(menu.IsOpen);
	}

	[TestMethod]
	public void Changed_HandlerThatReopenedTheMenuWithTheNewActions_KeepsTheReopenedMenu()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();

		CodeActionPresenter? presenter = null;

		// This handler subscribes before the presenter does, so it runs first: it reopens the menu with the set the controller published.
		controller.Changed += (_, _) => presenter?.TryOpenMenu();

		presenter = host.CreatePresenter(controller);

		using (presenter)
		{
			CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

			Assert.IsTrue(presenter.IsMenuOpen);

			ContextMenu firstMenu = host.Menus[^1];

			// A second publish replaces the set; the earlier handler reopens the menu for it, and the presenter must keep that menu open.
			host.RequestCodeActionsAsync = static (_, _) =>
				Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it again")]);

			CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

			Assert.IsTrue(presenter.IsMenuOpen);
			Assert.HasCount(2, host.Menus);
			Assert.IsFalse(firstMenu.IsOpen);
			Assert.IsTrue(host.Menus[^1].IsOpen);
		}
	}

	[TestMethod]
	public void ControllerDisposal_WhenMenuOpen_ClosesTheMenuAndOpensNoneAfterwards()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		CodeActionController controller = host.CreateController();

		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(presenter.TryOpenMenu());

		controller.Dispose();

		Assert.IsFalse(presenter.IsMenuOpen);
		Assert.IsFalse(presenter.TryOpenMenu());
	}

	[TestMethod]
	public void ControllerDisposalOnAnotherThread_WhenMenuOpen_PostsTheCloseToTheTextAreaThread()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		CodeActionController controller = host.CreateController();

		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(presenter.TryOpenMenu());

		// The disposal raises the controller's change notification on the worker thread, which must not touch the menu from there.
		var thread = new Thread(controller.Dispose);

		thread.Start();
		thread.Join();

		DispatcherTestHelpers.PumpUntil(() => !presenter.IsMenuOpen);

		Assert.IsFalse(presenter.IsMenuOpen);
	}

	[TestMethod]
	public void Dispose_WhenMenuOpen_ClosesTheMenuAndLeavesTheControllerRunning()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();

		CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(presenter.TryOpenMenu());

		presenter.Dispose();

		Assert.IsFalse(presenter.IsMenuOpen);
		Assert.IsFalse(presenter.TryOpenMenu());
		Assert.IsFalse(presenter.TryOpenMenu(1, new Point(0.0, 0.0)));

		// The presenter does not own the controller: the controller keeps its actions and keeps working.
		Assert.IsTrue(controller.HasActions);

		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([]);
		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void PublicOperations_AfterDisposal_ReturnSafeDefaultsAndDoNotThrow()
	{
		using var host = new CodeActionPresenterTestHost();

		using CodeActionController controller = host.CreateController();

		CodeActionPresenter presenter = host.CreatePresenter(controller);

		presenter.Dispose();

		presenter.CloseMenu();
		presenter.Dispose();

		Assert.IsFalse(presenter.IsMenuOpen);
		Assert.IsFalse(presenter.TryOpenMenu());
		Assert.IsFalse(presenter.TryOpenMenu(1, new Point(0.0, 0.0)));
	}

	[TestMethod]
	public void PublicOperations_FromAnotherThreadAfterDisposal_AreQuietNoOps()
	{
		using var host = new CodeActionPresenterTestHost();

		using CodeActionController controller = host.CreateController();

		CodeActionPresenter presenter = host.CreatePresenter(controller);

		presenter.Dispose();

		Exception? failure = null;

		bool opened = true;
		bool openedAtAnchor = true;

		var thread = new Thread(() =>
		{
			try
			{
				opened = presenter.TryOpenMenu();
				openedAtAnchor = presenter.TryOpenMenu(1, new Point(0.0, 0.0));
				presenter.CloseMenu();
				presenter.Dispose();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.Start();
		thread.Join();

		// After disposal every open member returns before the thread check, so the calls are safe from any thread.
		Assert.IsNull(failure);
		Assert.IsFalse(opened);
		Assert.IsFalse(openedAtAnchor);
	}

	[TestMethod]
	public void TryOpenMenu_MenuHookThatDisposesTheController_LeavesNoMenuOpen()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		CodeActionController controller = host.CreateController();

		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());

		// The hook runs while the menu opens, before the presenter tracks the menu, so the change notification of the disposal finds nothing to close.
		host.ConfigureMenu = _ => controller.Dispose();

		Assert.IsFalse(presenter.TryOpenMenu());
		Assert.IsFalse(presenter.IsMenuOpen);
		Assert.IsFalse(host.Menus[^1].IsOpen);
	}

	[TestMethod]
	public void Constructor_NullArguments_ThrowArgumentNullException()
	{
		using var host = new CodeActionPresenterTestHost();

		using CodeActionController controller = host.CreateController();

		var textArea = host.Editor.TextArea;

		CodeActionPresentationOptions presentation = CodeActionPresenterTestHost.CreatePresentation();

		ArgumentNullException textAreaFailure = Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CodeActionPresenter(textArea: null!, controller, presentation)
		);

		ArgumentNullException controllerFailure = Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CodeActionPresenter(textArea, controller: null!, presentation)
		);

		// Each guard names its own parameter, so a missing argument is not reported by a collaborator that happens to reject it later.
		Assert.AreEqual("textArea", textAreaFailure.ParamName);
		Assert.AreEqual("controller", controllerFailure.ParamName);
	}

	[TestMethod]
	public void Constructor_WithoutPresentation_UsesTheDefaultPresentation()
	{
		using var host = new CodeActionPresenterTestHost();

		using CodeActionController controller = host.CreateController();
		using var presenter = new CodeActionPresenter(host.Editor.TextArea, controller);

		Assert.AreEqual(CodeActionPresentationOptions.Default.MenuTheme, presenter.MenuTheme);
	}

	[TestMethod]
	public void Constructor_OffTheTextAreaThread_ThrowsInvalidOperationException()
	{
		using var host = new CodeActionPresenterTestHost();

		using CodeActionController controller = host.CreateController();

		var textArea = host.Editor.TextArea;

		CodeActionPresentationOptions presentation = CodeActionPresenterTestHost.CreatePresentation();

		Exception? failure = null;

		var thread = new Thread(() =>
		{
			try
			{
				new CodeActionPresenter(textArea, controller, presentation).Dispose();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.Start();
		thread.Join();

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
	}

	[TestMethod]
	public void InteractiveMembers_OffTheTextAreaThread_ThrowInvalidOperationException()
	{
		using var host = new CodeActionPresenterTestHost();

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		Exception? openFailure = null;
		Exception? anchoredFailure = null;
		Exception? closeFailure = null;

		var thread = new Thread(() =>
		{
			try
			{
				_ = presenter.TryOpenMenu();
			}
			catch (Exception exception)
			{
				openFailure = exception;
			}

			try
			{
				_ = presenter.TryOpenMenu(1, new Point(0.0, 0.0));
			}
			catch (Exception exception)
			{
				anchoredFailure = exception;
			}

			try
			{
				presenter.CloseMenu();
			}
			catch (Exception exception)
			{
				closeFailure = exception;
			}
		});

		thread.Start();
		thread.Join();

		Assert.IsInstanceOfType<InvalidOperationException>(openFailure);
		Assert.IsInstanceOfType<InvalidOperationException>(anchoredFailure);
		Assert.IsInstanceOfType<InvalidOperationException>(closeFailure);
	}
}
