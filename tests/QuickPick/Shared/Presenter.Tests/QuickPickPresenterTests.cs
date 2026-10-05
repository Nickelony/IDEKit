#if AVALONIAQUICKPICK
using Avalonia.Input;
using Avalonia.Media;

namespace Nickelony.QuickPick.Avalonia.Tests;

#else
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.QuickPick.Wpf.Tests;

#endif

/// <summary>
/// Covers the quick-pick presenter in its toolkit package: the window that opens and closes with the session, the keys, the rows it draws
/// with their highlights, groups and hooks, and the events that end a session. The ranking and the session rules are covered by the controller tests,
/// which run without a toolkit.
/// </summary>
[STATestClass]
#if !AVALONIAQUICKPICK
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class QuickPickPresenterTests
{
	private static StubQuickPickProvider ProviderOf(params string[] keys)
	{
		var provider = new StubQuickPickProvider();

		provider.Items.AddRange(keys.Select(key => StubQuickPickProvider.Item(key)));

		return provider;
	}

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf());

		Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickPresenter(null!, host.Controller));
		Assert.ThrowsExactly<ArgumentNullException>(() => new QuickPickPresenter(host.Host, null!));
	}

	[TestMethod]
	public void Theme_Null_Throws()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf());
		Assert.ThrowsExactly<ArgumentNullException>(() => host.Presenter.Theme = null!);
	}

	[TestMethod]
	public void Open_ShowsTheWindowWithTheFirstRowActive()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha", "beta", "gamma"));

		Assert.IsFalse(host.Presenter.IsOpen);

		host.Open();

		Assert.IsTrue(host.Presenter.IsOpen);
		Assert.IsTrue(host.Presenter.Popup.IsOpen);
		Assert.HasCount(3, host.Presenter.List.Items);
		Assert.AreEqual(0, host.Presenter.List.SelectedIndex);
	}

	[TestMethod]
	public void Open_GivesTheInputTheKeyboardFocus()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Open();

		Assert.IsTrue(TestQuickPickInput.IsFocused(host.Presenter.Input));
	}

	[TestMethod]
	public void Open_WithADisabledFirstItem_ActivatesTheFirstEnabledRow()
	{
		var provider = new StubQuickPickProvider();

		provider.Items.Add(StubQuickPickProvider.Item("off", isEnabled: false));
		provider.Items.Add(StubQuickPickProvider.Item("on"));

		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();

		Assert.AreEqual(1, host.Presenter.List.SelectedIndex);
	}

	[TestMethod]
	public void Open_ShowsThePlaceholderOfTheModeUntilTheUserTypes()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha", "beta"));

		host.Open();

		Assert.AreEqual("Type to filter", host.Presenter.Placeholder.Text);
		Assert.IsTrue(TestQuickPickInput.IsShown(host.Presenter.Placeholder));

		host.Type("a");

		Assert.IsFalse(TestQuickPickInput.IsShown(host.Presenter.Placeholder));
	}

	[TestMethod]
	public void Open_WithoutRows_HidesTheList()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf());

		host.Open();

		Assert.IsFalse(TestQuickPickInput.IsShown(host.Presenter.List));
	}

	[TestMethod]
	public void Open_AfterADismiss_ShowsTheWindowAgain()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Open();
		host.Controller.Dismiss();
		host.Open();

		Assert.IsTrue(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void Dismiss_ClosesTheWindowAndReturnsTheFocusToTheControlThatHadIt()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Other.Focus();
		DispatcherTestHelpers.PumpUntilIdle();

		host.Open();
		host.Controller.Dismiss();
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.IsFalse(host.Presenter.Popup.IsOpen);
		Assert.IsTrue(TestQuickPickInput.IsFocused(host.Other));
	}

	[TestMethod]
	public void Dismiss_ClearsTheInputForTheNextSession()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha", "beta"));

		host.Open();
		host.Type("be");
		host.Controller.Dismiss();
		host.Open();

		Assert.AreEqual(string.Empty, host.Presenter.Input.Text);
		Assert.HasCount(2, host.Presenter.List.Items);
	}

	[TestMethod]
	public void Rows_DrawTheMatchedCharactersOfTheLabelBold()
	{
		var provider = new StubQuickPickProvider();

		provider.Items.Add(new QuickPickItem("fd", "Format Document"));
		provider.Items.Add(new QuickPickItem("cf", "Close File"));

		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();
		host.Type("fmtdoc");

		Assert.HasCount(1, host.Presenter.List.Items);
		Assert.AreSequenceEqual(["F", "m", "t", "Doc"], TestQuickPickInput.BoldTextsOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
	}

	[TestMethod]
	public void Rows_WithoutAQuery_DrawNoBoldText()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Open();

		Assert.IsEmpty(TestQuickPickInput.BoldTextsOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
	}

	[TestMethod]
	public void Rows_DrawTheDescriptionTheDetailAndTheTrailingText()
	{
		var provider = new StubQuickPickProvider();

		provider.Items.Add(
			new QuickPickItem("save", "Save File")
			{
				Description = "File",
				Detail = "Writes the document",
				TrailingText = "Ctrl+S",
			}
		);

		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();

		IReadOnlyList<string> texts = TestQuickPickInput.TextsOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0));

		Assert.Contains("Save File", texts);
		Assert.Contains("File", texts);
		Assert.Contains("Writes the document", texts);
		Assert.Contains("Ctrl+S", texts);
	}

	[TestMethod]
	public void Rows_DrawTheSeparatorOfAGroupWithItsCount()
	{
		var group = new QuickPickGroup("function", "Functions", showsCount: true);
		var provider = new StubQuickPickProvider();

		provider.Items.Add(StubQuickPickProvider.Item("f1", group));
		provider.Items.Add(StubQuickPickProvider.Item("f2", group));

		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();

		Assert.HasCount(3, host.Presenter.List.Items);
		Assert.AreSequenceEqual(["Functions (2)"], TestQuickPickInput.TextsOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
		Assert.AreEqual(1, host.Presenter.List.SelectedIndex);
	}

	[TestMethod]
	public void Rows_DrawTheImageTheIconHookReturns()
	{
		QuickPickPresenterTestHost? host = null;

		var image = TestQuickPickInput.CreateImage();

		var requestedKeys = new List<string>();
		var provider = new StubQuickPickProvider();

		provider.Items.Add(new QuickPickItem("a", "With icon") { IconKey = "file" });
		provider.Items.Add(new QuickPickItem("b", "Without icon"));

		try
		{
			host = new QuickPickPresenterTestHost(
				provider,

				hooks: new QuickPickPresenterHooks
				{
					ResolveIcon = key =>
					{
						requestedKeys.Add(key);
						return image;
					},
				}
			);

			host.Open();

			Assert.AreSequenceEqual(["file"], requestedKeys);
			Assert.AreSame(image, Assert.ContainsSingle(TestQuickPickInput.ImagesOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0))));
			Assert.IsEmpty(TestQuickPickInput.ImagesOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 1)));
		}
		finally
		{
			host?.Dispose();
		}
	}

	[TestMethod]
	public void Rows_DrawNoImageWhenTheIconHookReturnsNull()
	{
		var provider = new StubQuickPickProvider();
		provider.Items.Add(new QuickPickItem("a", "With icon") { IconKey = "file" });
		using var host = new QuickPickPresenterTestHost(provider, hooks: new QuickPickPresenterHooks { ResolveIcon = _ => null });

		host.Open();

		Assert.IsEmpty(TestQuickPickInput.ImagesOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
	}

	[TestMethod]
	public void Rows_UseTheItemTemplateOfTheTheme()
	{
		using var host = new QuickPickPresenterTestHost(
			ProviderOf("alpha", "beta"),
			new QuickPickTheme { ItemTemplate = TestQuickPickInput.CreateMarkerTemplate() }
		);

		host.Open();

		Assert.IsTrue(TestQuickPickInput.IsMarkerRow(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
		Assert.IsTrue(TestQuickPickInput.IsMarkerRow(TestQuickPickInput.ContainerOf(host.Presenter.List, 1)));
	}

	[TestMethod]
	public void Theme_AssignedWhileOpen_SwitchesBetweenTheItemTemplateAndTheDefaultRows()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Open();

		Assert.IsFalse(TestQuickPickInput.IsMarkerRow(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));

		host.Presenter.Theme = new QuickPickTheme { ItemTemplate = TestQuickPickInput.CreateMarkerTemplate() };
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsTrue(TestQuickPickInput.IsMarkerRow(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));

		host.Presenter.Theme = QuickPickTheme.Default;
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsFalse(TestQuickPickInput.IsMarkerRow(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
		Assert.Contains("alpha", TestQuickPickInput.TextsOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
	}

	[TestMethod]
	public void Rows_AreNamedByTheirLabelDescriptionAndTrailingTextForAScreenReader()
	{
		var provider = new StubQuickPickProvider();

		provider.Items.Add(new QuickPickItem("a", "Format Document") { Description = "Edit", TrailingText = "Ctrl+K" });
		provider.Items.Add(new QuickPickItem("b", "Close"));

		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();

		Assert.AreEqual("Format Document, Edit, Ctrl+K", TestQuickPickInput.AutomationNameOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
		Assert.AreEqual("Close", TestQuickPickInput.AutomationNameOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 1)));
	}

	[TestMethod]
	public void Input_IsNamedByThePlaceholderOfTheMode()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Open();

		Assert.AreEqual("Type to filter", TestQuickPickInput.AutomationNameOf(host.Presenter.Input));
	}

	[TestMethod]
	public void Rows_WithoutAnItemTemplate_DrawTheDefaultRows()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Open();

		Assert.IsFalse(TestQuickPickInput.IsMarkerRow(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
		Assert.Contains("alpha", TestQuickPickInput.TextsOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
	}

	[TestMethod]
	public void Typing_FiltersTheRowsAndActivatesTheFirstOne()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha", "beta", "alps"));

		host.Open();
		host.Type("alp");

		Assert.AreSequenceEqual(["alps", "alpha"], host.RowTexts);
		Assert.AreEqual(0, host.Presenter.List.SelectedIndex);
	}

	[TestMethod]
	public void Typing_AQueryThatLeavesNoItem_ShowsTheEmptyRowThatNeverActivatesOrAccepts()
	{
		StubQuickPickProvider provider = ProviderOf("alpha", "beta");
		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();
		host.Type("zzz");

		Assert.IsTrue(TestQuickPickInput.IsShown(host.Presenter.List));
		Assert.HasCount(1, host.Presenter.List.Items);
		Assert.Contains("No matching results", TestQuickPickInput.TextsOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
		Assert.AreEqual("No matching results", TestQuickPickInput.AutomationNameOf(TestQuickPickInput.ContainerOf(host.Presenter.List, 0)));
		Assert.AreEqual(-1, host.Presenter.List.SelectedIndex);

		TestQuickPickInput.PressKey(host.Presenter.Input, Key.Enter);
		DispatcherTestHelpers.PumpUntilIdle();
		TestQuickPickInput.ClickRow(host.Presenter.List, 0);
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsTrue(host.Presenter.IsOpen);
		Assert.DoesNotContain(entry => entry.StartsWith("accept:", StringComparison.Ordinal), provider.Log);
	}

	[TestMethod]
	public void Keys_DownAndUp_MoveTheActiveRowAndWrap()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("a", "b", "c"));

		host.Open();

		Assert.IsTrue(TestQuickPickInput.PressKey(host.Presenter.Input, Key.Down));
		Assert.AreEqual(1, host.Presenter.List.SelectedIndex);

		Assert.IsTrue(TestQuickPickInput.PressKey(host.Presenter.Input, Key.Up));
		Assert.AreEqual(0, host.Presenter.List.SelectedIndex);

		Assert.IsTrue(TestQuickPickInput.PressKey(host.Presenter.Input, Key.Up));
		Assert.AreEqual(2, host.Presenter.List.SelectedIndex);
	}

	[TestMethod]
	public void Keys_PageDownAndPageUp_MoveByTheVisibleRowCountWithoutWrapping()
	{
		string[] keys = [.. Enumerable.Range(0, 30).Select(index => $"item{index:00}")];

		var theme = new QuickPickTheme { MaxVisibleRows = 4 };
		using var host = new QuickPickPresenterTestHost(ProviderOf(keys), theme);

		host.Open();

		Assert.IsTrue(TestQuickPickInput.PressKey(host.Presenter.Input, Key.PageDown));
		Assert.AreEqual(4, host.Presenter.List.SelectedIndex);

		TestQuickPickInput.PressKey(host.Presenter.Input, Key.PageDown);
		Assert.AreEqual(8, host.Presenter.List.SelectedIndex);

		for (int press = 0; press < 20; press++)
			TestQuickPickInput.PressKey(host.Presenter.Input, Key.PageDown);

		Assert.AreEqual(29, host.Presenter.List.SelectedIndex);

		for (int press = 0; press < 20; press++)
			TestQuickPickInput.PressKey(host.Presenter.Input, Key.PageUp);

		Assert.AreEqual(0, host.Presenter.List.SelectedIndex);
	}

	[TestMethod]
	public void Keys_Enter_AcceptsTheActiveItemOnceAndClosesTheWindow()
	{
		StubQuickPickProvider provider = ProviderOf("alpha", "beta");
		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();
		TestQuickPickInput.PressKey(host.Presenter.Input, Key.Down);

		Assert.IsTrue(TestQuickPickInput.PressKey(host.Presenter.Input, Key.Enter));
		DispatcherTestHelpers.PumpUntilIdle();

		// The session is over, so a second accept finds nothing to accept.
		Assert.IsFalse(host.Controller.AcceptAsync().Result);

		Assert.AreSequenceEqual(["preview:alpha", "preview:beta", "accept:beta"], provider.Log);
		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.IsFalse(host.Presenter.Popup.IsOpen);
	}

	[TestMethod]
	public void Keys_Escape_EndsTheSessionAndRestoresThePreview()
	{
		StubQuickPickProvider provider = ProviderOf("alpha", "beta");
		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();
		TestQuickPickInput.PressKey(host.Presenter.Input, Key.Down);

		Assert.IsTrue(TestQuickPickInput.PressKey(host.Presenter.Input, Key.Escape));

		Assert.AreSequenceEqual(["preview:alpha", "preview:beta", "cancel"], provider.Log);
		Assert.IsFalse(host.Controller.State.IsOpen);
		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void Keys_OtherKeys_AreLeftToTheInput()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Open();

		Assert.IsFalse(TestQuickPickInput.PressKey(host.Presenter.Input, Key.A));
		Assert.IsFalse(TestQuickPickInput.PressKey(host.Presenter.Input, Key.Left));
	}

	[TestMethod]
	public void Click_OnARow_ActivatesAndAcceptsItsItem()
	{
		StubQuickPickProvider provider = ProviderOf("alpha", "beta", "gamma");
		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();
		TestQuickPickInput.ClickRow(host.Presenter.List, 2);
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.Contains("accept:gamma", provider.Log);
		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void Click_OnTheSeparatorOfAGroup_DoesNothing()
	{
		var group = new QuickPickGroup("g", "Group");
		var provider = new StubQuickPickProvider();

		provider.Items.Add(StubQuickPickProvider.Item("a", group));
		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();
		TestQuickPickInput.ClickRow(host.Presenter.List, 0);
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsTrue(host.Presenter.IsOpen);
		Assert.DoesNotContain(entry => entry.StartsWith("accept:", StringComparison.Ordinal), provider.Log);
	}

	[TestMethod]
	public void Click_OnADisabledItem_DoesNothing()
	{
		var provider = new StubQuickPickProvider();

		provider.Items.Add(StubQuickPickProvider.Item("on"));
		provider.Items.Add(StubQuickPickProvider.Item("off", isEnabled: false));

		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();
		TestQuickPickInput.ClickRow(host.Presenter.List, 1);
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsTrue(host.Presenter.IsOpen);
		Assert.AreEqual(0, host.Presenter.List.SelectedIndex);
	}

	[TestMethod]
	public void FocusMovingToAnotherControl_EndsTheSessionWithoutAnAccept()
	{
		StubQuickPickProvider provider = ProviderOf("alpha");
		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();
		host.Other.Focus();
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsFalse(host.Controller.State.IsOpen);
		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.AreSequenceEqual(["preview:alpha", "cancel"], provider.Log);
	}

	[TestMethod]
	public void DeactivationOfTheHostWindow_EndsTheSession()
	{
		StubQuickPickProvider provider = ProviderOf("alpha");
		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();
		TestWindowActivation.Deactivate(host.Window);
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsFalse(host.Controller.State.IsOpen);
		Assert.IsFalse(host.Presenter.IsOpen);
	}

	[TestMethod]
	public void ThePopupClosingByItself_EndsTheSession()
	{
		StubQuickPickProvider provider = ProviderOf("alpha");
		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();
		host.Presenter.Popup.IsOpen = false;
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsFalse(host.Controller.State.IsOpen);
		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.AreSequenceEqual(["preview:alpha", "cancel"], provider.Log);
	}

	[TestMethod]
	public void Busy_ASlowQuery_ShowsTheBusyBarUntilTheAnswerArrives()
	{
		StubQuickPickProvider provider = ProviderOf("alpha");
		var gate = new TaskCompletionSource<IReadOnlyList<QuickPickItem>>();
		provider.Answer = (_, _) => gate.Task;
		using var host = new QuickPickPresenterTestHost(provider);

		host.Open();

		// The controller tests pin the moment of the busy state on a virtual clock; here the bar follows it.
		DispatcherTestHelpers.PumpUntil(() => TestQuickPickInput.IsShown(host.Presenter.BusyBar));

		gate.SetResult([StubQuickPickProvider.Item("alpha")]);
		DispatcherTestHelpers.PumpUntil(() => !TestQuickPickInput.IsShown(host.Presenter.BusyBar));

		Assert.HasCount(1, host.Presenter.List.Items);
	}

	[TestMethod]
	public void ModeFromTheListOfModes_SetsTheTextOfTheInput()
	{
		var provider = new StubQuickPickProvider(">");
		provider.Items.Add(StubQuickPickProvider.Item("save"));
		using var host = new QuickPickPresenterTestHost(provider);

		host.Open("?");
		TestQuickPickInput.PressKey(host.Presenter.Input, Key.Enter);
		DispatcherTestHelpers.PumpUntil(() => provider.Queries.Count > 0);

		Assert.AreEqual(">", host.Presenter.Input.Text);
		Assert.IsTrue(host.Presenter.IsOpen);
		Assert.HasCount(1, host.Presenter.List.Items);
	}

	[TestMethod]
	public void Theme_ColorsTheFrameAndTheInput()
	{
		var theme = new QuickPickTheme
		{
			Background = Brushes.Navy,
			Foreground = Brushes.Yellow,
			BorderBrush = Brushes.Red,
			BorderThickness = 3.0,
			CornerRadius = 9.0,
			Width = 400.0,
		};

		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"), theme);

		host.Open();

		Assert.AreSame(Brushes.Navy, host.Presenter.Frame.Background);
		Assert.AreSame(Brushes.Red, host.Presenter.Frame.BorderBrush);
		Assert.AreSame(Brushes.Yellow, host.Presenter.Input.Foreground);
		Assert.AreEqual(3.0, host.Presenter.Frame.BorderThickness.Left);
		Assert.AreEqual(9.0, host.Presenter.Frame.CornerRadius.TopLeft);
		Assert.AreEqual(400.0, host.Presenter.Frame.Width);
		Assert.AreSame(theme, host.Presenter.Theme);
	}

	[TestMethod]
	public void Theme_AssignedWhileOpen_ColorsTheOpenWindow()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Open();

		var theme = new QuickPickTheme { Background = Brushes.Black, Foreground = Brushes.White };

		host.Presenter.Theme = theme;

		Assert.AreSame(Brushes.Black, host.Presenter.Frame.Background);
		Assert.AreSame(Brushes.White, host.Presenter.Input.Foreground);
		Assert.AreEqual(0, host.Presenter.List.SelectedIndex);
	}

	[TestMethod]
	public void Theme_MaxVisibleRows_BoundsTheHeightOfTheList()
	{
		var theme = new QuickPickTheme { MaxVisibleRows = 3, RowHeight = 20.0 };
		using var host = new QuickPickPresenterTestHost(ProviderOf("a", "b", "c", "d", "e"), theme);

		host.Open();

		Assert.AreEqual(60.0, host.Presenter.List.MaxHeight);
	}

	[TestMethod]
	public void Theme_Invalid_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = new QuickPickTheme { Background = null! });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new QuickPickTheme { Width = 0.0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new QuickPickTheme { BorderThickness = -1.0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new QuickPickTheme { MaxVisibleRows = 0 });
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new QuickPickTheme { RowHeight = double.NaN });
	}

	[TestMethod]
	public void Placement_TheWindowHangsFromTheTopOfTheHostAtTheOffsetOfTheTheme()
	{
		var theme = new QuickPickTheme { TopOffset = 20.0, Width = 400.0 };
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"), theme);

		host.Open();

		(double top, double left, double right) = TestQuickPickInput.MeasureGaps(host.Presenter.Frame, host.Host);

		Assert.AreEqual(20.0, top, 1.0);
		Assert.IsGreaterThan(0.0, left);
		Assert.AreEqual(left, right, 1.0);
	}

	[TestMethod]
	public void Dispose_ClosesTheWindowAndStopsFollowingTheController()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Open();
		host.Presenter.Dispose();

		Assert.IsFalse(host.Presenter.IsOpen);
		Assert.IsFalse(host.Presenter.Popup.IsOpen);

		// The session of the controller goes on, and the disposed presenter shows nothing of it.
		Assert.IsTrue(host.Controller.State.IsOpen);

		host.Controller.Open();

		Assert.IsFalse(host.Presenter.Popup.IsOpen);
	}

	[TestMethod]
	public void Constructor_ForAControllerThatIsAlreadyOpen_ShowsTheWindow()
	{
		using var host = new QuickPickPresenterTestHost(ProviderOf("alpha"));

		host.Presenter.Dispose();
		host.Controller.Open();

		using var late = new QuickPickPresenter(host.Host, host.Controller);

		Assert.IsTrue(late.IsOpen);
		Assert.IsTrue(late.Popup.IsOpen);
	}
}
