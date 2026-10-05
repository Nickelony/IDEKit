using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.Win32;
using Nickelony.IDEKit.AvalonEdit.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.Samples.Wpf;

/// <summary>
/// The main window of the WPF sample: a menu, the explorer and the outline, the editor tabs with a second group for split views and previews,
/// the lists of the workbench and a status bar.
/// </summary>
/// <remarks>
/// The window lays out WPF controls over the workbench, which owns every feature, so the Avalonia head shows the same menus and rows.
/// The window chrome takes the Fluent light or dark theme of the editor theme.
/// </remarks>
internal sealed class SampleWindow : Window
{
	private readonly TabControl _tabs = new();
	private readonly TabControl _tabsBeside = new() { Visibility = Visibility.Collapsed };

	private readonly GridSplitter _groupSplitter = new()
	{
		HorizontalAlignment = HorizontalAlignment.Stretch,
		Width = 5,
		Visibility = Visibility.Collapsed,
	};

	private readonly Grid _groups = new();
	private readonly TabControl _lists = new();
	private readonly TreeView _explorer = new() { BorderThickness = new Thickness(0) };

	private readonly ListBox _outline = CreateList();
	private readonly ListBox _problems = CreateList();
	private readonly ListBox _references = CreateList();
	private readonly ListBox _searchResults = CreateList();

	private readonly TextBox _output = new()
	{
		IsReadOnly = true,
		FontFamily = new("Consolas"),
		VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
	};

	private readonly TextBlock _caret = new() { Margin = new Thickness(left: 16, top: 0, right: 16, bottom: 0) };
	private readonly TextBlock _indentation = new() { Margin = new Thickness(left: 0, top: 0, right: 16, bottom: 0), Cursor = Cursors.Hand };

	private readonly ContextMenu _indentationMenu = new();
	private readonly List<(bool InsertSpaces, int Size, MenuItem Item)> _indentationSizes = [];
	private readonly TextBlock _encoding = new() { Margin = new Thickness(left: 0, top: 0, right: 16, bottom: 0), Cursor = Cursors.Hand };
	private readonly ContextMenu _encodingMenu = new();

	private readonly List<(TextFileFormat Format, MenuItem Item)> _reopenEncodings = [];
	private readonly List<(TextFileFormat Format, MenuItem Item)> _saveEncodings = [];

	private readonly TextBlock _lineTerminator = new() { Margin = new Thickness(left: 0, top: 0, right: 16, bottom: 0), Cursor = Cursors.Hand };
	private readonly ContextMenu _lineTerminatorMenu = new();
	private readonly List<(LineTerminatorStyle Style, MenuItem Item)> _lineTerminators = [];
	private readonly TextBlock _servers = new();
	private readonly List<(SampleCommand Command, MenuItem Item)> _menuItems = [];
	private readonly Dictionary<object, TabItem> _tabItems = [];
	private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);

	private SampleFindWindow? _findWindow;
	private bool _isClosing;

	/// <param name="workbench">The workbench that owns the editors; the window disposes it when it closes.</param>
	internal SampleWindow(SampleWorkbench workbench)
	{
		Workbench = workbench;

		Title = "Nickelony sample (WPF)";
		Width = 1280;
		Height = 840;

		_lists.Items.Add(new TabItem { Header = "Problems", Content = _problems });
		_lists.Items.Add(new TabItem { Header = "References", Content = _references });
		_lists.Items.Add(new TabItem { Header = "Search results", Content = _searchResults });
		_lists.Items.Add(new TabItem { Header = "Output", Content = _output });

		Workbench.DecideSaveAsync = DecideSaveAsync;
		Workbench.DecideSaveAllAsync = DecideSaveAllAsync;
		Subscribe();

		var layout = new DockPanel();

		Menu menu = CreateMenu();
		Border statusBar = CreateStatusBar();

		DockPanel.SetDock(menu, Dock.Top);
		DockPanel.SetDock(statusBar, Dock.Bottom);

		layout.Children.Add(menu);
		layout.Children.Add(statusBar);
		layout.Children.Add(CreateBody());

		Content = layout;
		Workbench.QuickPick.Attach(layout, Workbench.Theme);

		_expandedFolders.Add(Path.Combine(workbench.Files.Root, "lib"));
		ShowExplorer();
		SyncTabs();
		SelectActiveTab();
		ApplyThemeMode();
		ShowServers();
		CreateIndentationMenu();
		ShowIndentation();
		CreateFormatMenus();
		ShowFormat();
	}

	/// <summary>The workbench that owns the editors.</summary>
	internal SampleWorkbench Workbench { get; }

	/// <summary>The line terminators as the status bar shows them, which a test reads.</summary>
	internal string LineTerminatorText => _lineTerminator.Text;

	protected override async void OnClosing(CancelEventArgs e)
	{
		base.OnClosing(e);

		if (_isClosing || e.Cancel)
			return;

		// The workbench disposes asynchronously, and the dispatcher stops once the main window is gone, so the window stays until it is done.
		// A document with unsaved changes is asked about first, and the window stays open when the answer is Cancel or a save fails.
		e.Cancel = true;
		_isClosing = true;
		bool closes = false;

		// WPF refuses a Close inside the closing event, which a workbench that confirms and disposes without waiting, as a disposed one does,
		// would make below, so the event ends first.
		await Task.Yield();

		try
		{
			closes = await Workbench.ConfirmCloseAsync();

			if (closes)
			{
				_findWindow?.Close();
				await Workbench.DisposeAsync();
			}
		}
		finally
		{
			if (closes)
				Close();
			else
				_isClosing = false;
		}
	}

	private static ListBox CreateList() => new() { FontFamily = new("Consolas"), BorderThickness = new Thickness(0) };

	private void Subscribe()
	{
		SamplePanels panels = Workbench.Panels;

		Workbench.StatusChanged += (_, e) =>
		{
			// A long message is cut with an ellipsis, and the tooltip holds the whole of it.
			StatusLine.Text = e.Message;
			StatusLine.ToolTip = e.Message;
		};

		Workbench.OutputChanged += (_, e) => Show(_output, () => _output.Text = e.Output);
		Workbench.KeyBindings.BindingsChanged += (_, _) => ShowShortcuts();

		Workbench.ThemeChanged += (_, _) =>
		{
			ApplyThemeMode();
			Workbench.QuickPick.ApplyTheme(Workbench.Theme);
		};

		Workbench.LanguageServersChanged += (_, _) => ShowServers();
		Workbench.IndentationChanged += (_, _) => ShowIndentation();
		Workbench.FormatChanged += (_, _) => ShowFormat();
		Workbench.DialogRequested += (_, e) => _ = OpenAsync(e.Dialog, e.Path);
		Workbench.TabsChanged += (_, _) => SyncTabs();
		Workbench.ActivePaneChanged += (_, _) => SelectActiveTab();
		Workbench.Explorer.Changed += (_, _) => ShowExplorer();

		panels.OutlineChanged += (_, _) => _outline.ItemsSource = panels.Outline;
		panels.ProblemsChanged += (_, _) => _problems.ItemsSource = panels.Problems;
		panels.ReferencesChanged += (_, _) => Show(_references, () => _references.ItemsSource = panels.References);
		panels.SearchResultsChanged += (_, _) => Show(_searchResults, () => _searchResults.ItemsSource = panels.SearchResults);
		_outline.ItemsSource = panels.Outline;
		_problems.ItemsSource = panels.Problems;

		foreach (ListBox list in new[] { _outline, _problems, _references, _searchResults })
		{
			// A click on a row navigates even when the row is already selected, which a selection change would miss.
			list.MouseLeftButtonUp += (_, _) => NavigateToSelection(list);

			list.KeyDown += (_, e) =>
			{
				if (e.Key == Key.Enter)
					NavigateToSelection(list);
			};
		}

		foreach (TabControl group in new[] { _tabs, _tabsBeside })
		{
			group.SelectionChanged += (_, e) =>
			{
				// A selection change of a control inside a tab bubbles to the tabs, and only the tabs pick the pane.
				if (e.OriginalSource == group && group.SelectedItem is TabItem { Tag: SamplePane pane })
					Workbench.ActivePane = pane;
			};
		}

		_explorer.MouseDoubleClick += (_, _) => OpenSelectedFile();

		_explorer.KeyDown += (_, e) =>
		{
			if (e.Key == Key.Enter)
				OpenSelectedFile();
			else if (e.Key == Key.Delete && SelectedEntry() is { } node)
				_ = OpenAsync(SampleDialogKind.DeleteEntry, node.Path);
			else if (e.Key == Key.F2 && SelectedEntry() is { } renamed)
				_ = OpenAsync(SampleDialogKind.RenameEntry, renamed.Path);
		};

		// The key bindings see every stroke of the window first, so a shortcut works in the explorer and the lists too.
		PreviewKeyDown += (_, e) => e.Handled |= Workbench.KeyBindings.HandleKeyDown(e);
		Deactivated += (_, _) => Workbench.KeyBindings.CancelPendingChords();
	}

	private void Show(UIElement list, Action update)
	{
		update();
		_lists.SelectedIndex = _lists.Items.Cast<TabItem>().ToList().FindIndex(tab => tab.Content == list);
	}

	private void NavigateToSelection(ListBox list)
	{
		if (list.SelectedItem is SampleLocation location)
			_ = Workbench.NavigateToAsync(location);
	}

	private void ApplyThemeMode()
	{
		// The editors take their colors from the sample theme, and the chrome around them takes the matching Fluent theme of WPF.
		if (Application.Current is not { } application)
			return;

		application.ThemeMode = Workbench.Theme.IsDark ? ThemeMode.Dark : ThemeMode.Light;

		// The Fluent rows are sized for touch, so the lists take a compact style based on the row style of the theme in force.
		var rowStyle = new Style(typeof(ListBoxItem), application.TryFindResource(typeof(ListBoxItem)) as Style);

		rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(left: 8, top: 2, right: 8, bottom: 2)));
		rowStyle.Setters.Add(new Setter(MinHeightProperty, 0.0));

		foreach (ListBox list in new[] { _outline, _problems, _references, _searchResults })
			list.ItemContainerStyle = rowStyle;
	}

	private void ShowServers() => _servers.Text = $"{Workbench.LuaFeaturesDescription}    {Workbench.RoslynDescription}";

	private void ShowIndentation()
	{
		_indentation.Text = Workbench.IndentationDescription;
		_indentation.ToolTip = Workbench.IndentationToolTip;

		foreach ((bool insertSpaces, int size, MenuItem item) in _indentationSizes)
			item.IsChecked = Workbench.UsesIndentation(insertSpaces, size);
	}

	// A click on the indentation in the status bar lists the sizes for spaces and for tabs, then the commands that convert the text.
	private void CreateIndentationMenu()
	{
		_indentationMenu.Items.Add(CreateIndentationSizeMenu("Indent using _spaces", insertSpaces: true));
		_indentationMenu.Items.Add(CreateIndentationSizeMenu("Indent using _tabs", insertSpaces: false));
		_indentationMenu.Items.Add(new Separator());

		foreach (SampleCommand command in Workbench.IndentationCommands)
			_indentationMenu.Items.Add(CreateMenuItem(command));

		_indentation.MouseLeftButtonUp += (_, _) =>
		{
			_indentationMenu.PlacementTarget = _indentation;
			_indentationMenu.IsOpen = true;
		};
	}

	private MenuItem CreateIndentationSizeMenu(string caption, bool insertSpaces)
	{
		var menu = new MenuItem { Header = caption };

		foreach (int size in SampleWorkbench.IndentationSizes)
		{
			MenuItem item = CreateCheckItem(
				size.ToString(System.Globalization.CultureInfo.InvariantCulture),
				() => Workbench.UseIndentation(insertSpaces, size)
			);

			_indentationSizes.Add((insertSpaces, size, item));
			menu.Items.Add(item);
		}

		return menu;
	}

	// The other view of a split follows the publication of the active one, and the document already holds the new style when that view's text changes,
	// so the status shows it then and does not wait for the next change event of the document.
	private void ShowFormatOfOtherView(SamplePane pane)
	{
		if (Workbench.ActivePane is { } active && active != pane && SampleDocuments.PathComparer.Equals(active.FilePath, pane.FilePath))
			ShowFormat();
	}

	private void ShowFormat()
	{
		_encoding.Text = Workbench.EncodingDescription;
		_encoding.ToolTip = Workbench.EncodingToolTip;
		_lineTerminator.Text = Workbench.LineTerminatorDescription;
		_lineTerminator.ToolTip = Workbench.LineTerminatorToolTip;

		foreach ((TextFileFormat format, MenuItem item) in _reopenEncodings)
			item.IsChecked = Workbench.UsesEncoding(format.Encoding);

		foreach ((TextFileFormat format, MenuItem item) in _saveEncodings)
			item.IsChecked = Workbench.UsesFormat(format);

		foreach ((LineTerminatorStyle style, MenuItem item) in _lineTerminators)
			item.IsChecked = Workbench.UsesLineTerminator(style);
	}

	// A click on the encoding in the status bar lists the encodings to reopen the file with and to save it with; a click on the line terminators
	// lists the styles the text converts to.
	private void CreateFormatMenus()
	{
		var reopen = new MenuItem { Header = "_Reopen with encoding" };

		foreach (TextFileFormat format in SampleWorkbench.EncodingFormats.DistinctBy(format => format.Encoding))
		{
			MenuItem item = CreateCheckItem(format.Encoding.DisplayName, () => _ = Workbench.ReopenWithEncodingAsync(format));

			_reopenEncodings.Add((format, item));
			reopen.Items.Add(item);
		}

		var save = new MenuItem { Header = "_Save with encoding" };

		foreach (TextFileFormat format in SampleWorkbench.EncodingFormats)
		{
			MenuItem item = CreateCheckItem(format.EncodingDisplayName, () => _ = Workbench.SaveWithEncodingAsync(format));

			_saveEncodings.Add((format, item));
			save.Items.Add(item);
		}

		_encodingMenu.Items.Add(reopen);
		_encodingMenu.Items.Add(save);

		_encoding.MouseLeftButtonUp += (_, _) =>
		{
			_encodingMenu.PlacementTarget = _encoding;
			_encodingMenu.IsOpen = true;
		};

		foreach (LineTerminatorStyle style in SampleWorkbench.LineTerminatorStyles)
		{
			MenuItem item = CreateCheckItem(style.GetDisplayName(), () => _ = Workbench.UseLineTerminatorAsync(style));

			_lineTerminators.Add((style, item));
			_lineTerminatorMenu.Items.Add(item);
		}

		_lineTerminator.MouseLeftButtonUp += (_, _) =>
		{
			_lineTerminatorMenu.PlacementTarget = _lineTerminator;
			_lineTerminatorMenu.IsOpen = true;
		};
	}

	private void ShowCaret()
	{
		if (Workbench.ActivePane?.Editor.TextArea.Caret is { } caret)
			_caret.Text = $"Ln {caret.Line}, Col {caret.Column}";
		else
			_caret.Text = string.Empty;
	}

	// The tabs follow the panes and previews of the workbench: a new one gets a tab at its place in its group, and a closed or replaced one loses its tab.
	private void SyncTabs()
	{
		List<object> wanted = [.. Workbench.Panes, .. Workbench.Previews];
		IReadOnlyList<object> beside = Workbench.TabsBeside;

		foreach (object gone in _tabItems.Keys.Where(item => !wanted.Contains(item)).ToList())
		{
			_tabs.Items.Remove(_tabItems[gone]);
			_tabsBeside.Items.Remove(_tabItems[gone]);
			_tabItems.Remove(gone);
		}

		SyncGroup(_tabs, _tabsBeside, [.. wanted.Except(beside)]);
		SyncGroup(_tabsBeside, _tabs, beside);

		ShowGroupBeside(beside.Count > 0);

		foreach (TabItem tab in _tabItems.Values)
			UpdateHeader(tab);

		SelectActiveTab();
	}

	private void SyncGroup(TabControl group, TabControl other, IReadOnlyList<object> wanted)
	{
		foreach (TabItem moved in group.Items.Cast<TabItem>().Where(tab => !wanted.Contains(tab.Tag)).ToList())
			group.Items.Remove(moved);

		for (int index = 0; index < wanted.Count; index++)
		{
			if (!_tabItems.TryGetValue(wanted[index], out TabItem? tab))
			{
				tab = CreateTab(wanted[index]);
				_tabItems[wanted[index]] = tab;
			}

			int current = group.Items.IndexOf(tab);

			if (current == index)
				continue;

			if (current >= 0)
				group.Items.RemoveAt(current);

			other.Items.Remove(tab);
			group.Items.Insert(index, tab);
		}
	}

	// The second group takes half of the editor area while it has a tab and no room otherwise. The widths change only when it appears or goes, so a
	// drag of the splitter between the groups holds.
	private void ShowGroupBeside(bool isShown)
	{
		Visibility visibility = isShown ? Visibility.Visible : Visibility.Collapsed;

		if (_tabsBeside.Visibility == visibility)
			return;

		_tabsBeside.Visibility = visibility;
		_groupSplitter.Visibility = visibility;
		_groups.ColumnDefinitions[1].Width = new GridLength(isShown ? 5 : 0);
		_groups.ColumnDefinitions[2].Width = isShown ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
	}

	private TabItem CreateTab(object item)
	{
		var tab = new TabItem { Tag = item };

		if (item is SamplePane pane)
		{
			tab.Content = pane.Editor;
			pane.Editor.ContextMenu = CreateEditorMenu(pane.Editor);

			// A right press puts the caret under the pointer before the menu opens, unless it lands inside the selection, so Cut and Copy keep
			// their text.
			pane.Editor.PreviewMouseRightButtonDown += (_, e) =>
			{
				TextArea textArea = pane.Editor.TextArea;

				if (!(textArea.TryGetTextOffsetFromPoint(e.GetPosition(textArea), out int offset) && textArea.Selection.Contains(offset)))
					_ = pane.Editor.TryMoveCaretToMousePosition();
			};

			pane.Editor.TextArea.Caret.PositionChanged += (_, _) => ShowCaret();
			pane.Editor.TextChanged += (_, _) => ShowFormatOfOtherView(pane);
			pane.View.DirtyChanged += (_, _) => UpdateHeader(tab);

			// Both groups show a selected tab, so a click into the editor of the other group makes its pane the active one.
			pane.Editor.TextArea.GotKeyboardFocus += (_, _) => Workbench.ActivePane = pane;
		}
		else if (item is SampleMarkdownPreview preview)
		{
			tab.Content = preview.Content;
		}

		return tab;
	}

	private void UpdateHeader(TabItem tab)
	{
		(string title, bool isDirty) = tab.Tag switch
		{
			SamplePane pane => (pane.Title, pane.View.IsDirty),
			SampleMarkdownPreview { DocumentId: { } id } => ($"Preview {Path.GetFileName(id)}", false),
			_ => ("Preview", false),
		};

		var close = new Button
		{
			Content = "×",
			Padding = new Thickness(left: 4, top: 0, right: 4, bottom: 0),
			Margin = new Thickness(left: 6, top: 0, right: 0, bottom: 0),
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			VerticalAlignment = VerticalAlignment.Center,
			Focusable = false,
		};

		close.Click += (_, e) =>
		{
			e.Handled = true;
			CloseTab(tab);
		};

		var header = new StackPanel { Orientation = Orientation.Horizontal };

		header.Children.Add(new TextBlock { Text = isDirty ? $"{title} ●" : title, VerticalAlignment = VerticalAlignment.Center });
		header.Children.Add(close);

		tab.Header = header;
	}

	private void CloseTab(TabItem tab)
	{
		if (tab.Tag is SamplePane pane)
			_ = Workbench.CloseAsync(pane);
		else if (tab.Tag is SampleMarkdownPreview preview)
			Workbench.ClosePreview(preview);
	}

	private void SelectActiveTab()
	{
		if (Workbench.ActivePane is { } pane)
		{
			if (_tabItems.TryGetValue(pane, out TabItem? tab))
				(_tabsBeside.Items.Contains(tab) ? _tabsBeside : _tabs).SelectedItem = tab;

			RevealInExplorer(pane.FilePath);
		}

		ShowCaret();
		ShowIndentation();
		ShowFormat();
	}

	// The explorer selects the file of the active tab and opens the folders above it, as the explorer of an editor reveals the active file.
	private void RevealInExplorer(string path)
	{
		if (SelectedEntry() is { } entry && SampleDocuments.PathComparer.Equals(entry.Path, path))
			return;

		foreach (string folder in Workbench.Explorer.FoldersAbove(path))
			_expandedFolders.Add(folder);

		ShowExplorer(path);
	}

	private ContextMenu CreateEditorMenu(TextEditor editor)
	{
		var menu = new ContextMenu();

		menu.Items.Add(CreateMenuItem("Cu_t", editor.Cut));
		menu.Items.Add(CreateMenuItem("_Copy", editor.Copy));
		menu.Items.Add(CreateMenuItem("_Paste", editor.Paste));

		// The context menu runs the same commands as the main menu, so the clipboard and the language commands share one list.
		foreach (SampleCommand command in Workbench.ContextMenuCommands)
		{
			if (command.Id is SampleCommandIds.GoToDefinition or SampleCommandIds.ToggleComment or SampleCommandIds.Split)
				menu.Items.Add(new Separator());

			menu.Items.Add(CreateMenuItem(command));
		}

		return menu;
	}

	// The Fluent template draws the mark of a menu item only when the item is checkable, and a checkable item toggles itself on a click, so the
	// click shows the status again, which sets each mark from the state of the document.
	private MenuItem CreateCheckItem(string caption, Action execute)
	{
		MenuItem item = CreateMenuItem(caption, execute);
		item.IsCheckable = true;

		item.Click += (_, _) =>
		{
			ShowIndentation();
			ShowFormat();
		};

		return item;
	}

	/// <summary>The status line, which a test reads.</summary>
	internal TextBlock StatusLine { get; } = new() { TextTrimming = TextTrimming.CharacterEllipsis };

	/// <summary>The items of the status bar menus that carry a check mark, which a test reads.</summary>
	internal IReadOnlyList<MenuItem> CheckableStatusItems =>
		[
			.. _indentationSizes.Select(entry => entry.Item),
			.. _reopenEncodings.Select(entry => entry.Item),
			.. _saveEncodings.Select(entry => entry.Item),
			.. _lineTerminators.Select(entry => entry.Item),
		];

	private static MenuItem CreateMenuItem(string caption, Action execute)
	{
		var item = new MenuItem { Header = caption };
		item.Click += (_, _) => execute();

		return item;
	}

	// The explorer is rebuilt from the tree of the workbench after each change, keeping the folders the user opened and the selected entry.
	private void ShowExplorer(string? selected = null)
	{
		selected ??= SelectedEntry()?.Path;

		_explorer.Items.Clear();

		foreach (SampleExplorerNode node in Workbench.Explorer.Root.Children)
			_explorer.Items.Add(CreateExplorerItem(node));

		// The tree takes no selection from an item that is selected before it joins the tree, so the item is selected after.
		if (selected is not null && FindExplorerItem(_explorer.Items, selected) is { } item)
			item.IsSelected = true;

		_explorer.ContextMenu = CreateExplorerMenu();
	}

	private static TreeViewItem? FindExplorerItem(ItemCollection items, string path)
	{
		foreach (TreeViewItem item in items.OfType<TreeViewItem>())
		{
			if (item.Tag is SampleExplorerNode node && SampleDocuments.PathComparer.Equals(node.Path, path))
				return item;

			if (FindExplorerItem(item.Items, path) is { } found)
				return found;
		}

		return null;
	}

	private TreeViewItem CreateExplorerItem(SampleExplorerNode node)
	{
		var item = new TreeViewItem
		{
			Header = node.Name,
			Tag = node,
			FontWeight = node.IsFolder ? FontWeights.SemiBold : FontWeights.Normal,
			IsExpanded = node.IsFolder && _expandedFolders.Contains(node.Path),
		};

		if (node.IsFolder)
		{
			item.Expanded += (_, e) =>
			{
				if (e.OriginalSource == item)
					_expandedFolders.Add(node.Path);
			};

			item.Collapsed += (_, e) =>
			{
				if (e.OriginalSource == item)
					_expandedFolders.Remove(node.Path);
			};

			foreach (SampleExplorerNode child in node.Children)
				item.Items.Add(CreateExplorerItem(child));
		}

		return item;
	}

	private ContextMenu CreateExplorerMenu()
	{
		var menu = new ContextMenu();
		menu.Items.Add(CreateMenuItem("_Open", OpenSelectedFile));

		// A file that no candidate encoding decodes opens here, because the chosen encoding replaces the candidates for that file.
		var openWith = new MenuItem { Header = "Open with _encoding" };

		foreach (TextFileFormat format in SampleWorkbench.EncodingFormats.DistinctBy(format => format.Encoding))
			openWith.Items.Add(CreateMenuItem(format.Encoding.DisplayName, () => OpenSelectedFileWith(format.Encoding)));

		menu.Items.Add(openWith);
		menu.Items.Add(new Separator());
		menu.Items.Add(CreateMenuItem("_New file...", () => _ = OpenAsync(SampleDialogKind.NewFile, SelectedFolder())));
		menu.Items.Add(CreateMenuItem("New f_older...", () => _ = OpenAsync(SampleDialogKind.NewFolder, SelectedFolder())));
		menu.Items.Add(new Separator());
		menu.Items.Add(CreateMenuItem("_Rename...", () => _ = OpenSelectedAsync(SampleDialogKind.RenameEntry)));
		menu.Items.Add(CreateMenuItem("_Delete", () => _ = OpenSelectedAsync(SampleDialogKind.DeleteEntry)));

		return menu;
	}

	private SampleExplorerNode? SelectedEntry() => (_explorer.SelectedItem as TreeViewItem)?.Tag as SampleExplorerNode;

	private string SelectedFolder() =>
		SelectedEntry() is { } node ? (node.IsFolder ? node.Path : Path.GetDirectoryName(node.Path) ?? Workbench.Files.Root) : Workbench.Files.Root;

	private void OpenSelectedFile()
	{
		if (SelectedEntry() is { IsFolder: false } node)
			_ = Workbench.OpenFromExplorerAsync(node.Path);
	}

	private void OpenSelectedFileWith(TextEncoding encoding)
	{
		if (SelectedEntry() is { IsFolder: false } node)
			_ = Workbench.OpenWithEncodingAsync(node.Path, encoding);
	}

	private Task OpenSelectedAsync(SampleDialogKind dialog) => SelectedEntry() is { } node ? OpenAsync(dialog, node.Path) : Task.CompletedTask;

	private Menu CreateMenu()
	{
		var menu = new Menu();

		foreach (IGrouping<string, SampleCommand> group in Workbench.Commands.GroupBy(command => command.Menu))
		{
			var item = new MenuItem { Header = group.Key };

			foreach (SampleCommand command in group)
			{
				if (command.BeginsGroup)
					item.Items.Add(new Separator());

				item.Items.Add(CreateMenuItem(command));
			}

			if (group.Key == "_File")
			{
				var exit = new MenuItem { Header = "E_xit" };
				exit.Click += (_, _) => Close();

				item.Items.Add(new Separator());
				item.Items.Add(exit);
			}

			menu.Items.Add(item);
		}

		ShowShortcuts();

		return menu;
	}

	private MenuItem CreateMenuItem(SampleCommand command)
	{
		var item = new MenuItem { Header = command.Caption, InputGestureText = Workbench.KeyBindings.GetDisplayText(command.Id) };
		item.Click += (_, _) => command.Execute();
		_menuItems.Add((command, item));

		return item;
	}

	private void ShowShortcuts()
	{
		// The shortcut is the display text of the key bindings, so a rebinding shows in the menu at once.
		foreach ((SampleCommand command, MenuItem item) in _menuItems)
			item.InputGestureText = Workbench.KeyBindings.GetDisplayText(command.Id);
	}

	private Grid CreateBody()
	{
		var filter = new TextBox { Margin = new Thickness(left: 0, top: 0, right: 0, bottom: 4), ToolTip = "Filter the outline" };
		filter.TextChanged += (_, _) => Workbench.Panels.OutlineFilter = filter.Text;

		var outline = new DockPanel();
		DockPanel.SetDock(filter, Dock.Top);

		outline.Children.Add(filter);
		outline.Children.Add(_outline);

		// The explorer sits above the outline, and splitters let each part of the window take the room it needs.
		var side = new Grid { Margin = new Thickness(left: 6, top: 6, right: 0, bottom: 6) };

		AddRows(
			side,
			GridLength.Auto,
			new GridLength(1, GridUnitType.Star),
			new GridLength(5),
			GridLength.Auto,
			new GridLength(1, GridUnitType.Star)
		);

		AddToRow(side, CreateCaption("Explorer"), 0);
		AddToRow(side, _explorer, 1);
		AddToRow(side, new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 5 }, 2);
		AddToRow(side, CreateCaption("Outline"), 3);
		AddToRow(side, outline, 4);

		// The second group shows a split view or a preview beside the tab it follows.
		_groups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		_groups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
		_groups.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });

		Grid.SetColumn(_groupSplitter, 1);
		Grid.SetColumn(_tabsBeside, 2);

		_groups.Children.Add(_tabs);
		_groups.Children.Add(_groupSplitter);
		_groups.Children.Add(_tabsBeside);

		var editorArea = new Grid { Margin = new Thickness(left: 0, top: 6, right: 6, bottom: 6) };
		AddRows(editorArea, new GridLength(1, GridUnitType.Star), new GridLength(5), new GridLength(180));

		AddToRow(editorArea, _groups, 0);
		AddToRow(editorArea, new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 5 }, 1);
		AddToRow(editorArea, _lists, 2);

		var body = new Grid();

		body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
		body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
		body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		var columnSplitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Width = 5 };

		Grid.SetColumn(columnSplitter, 1);
		Grid.SetColumn(editorArea, 2);

		body.Children.Add(side);
		body.Children.Add(columnSplitter);
		body.Children.Add(editorArea);

		return body;
	}

	private static TextBlock CreateCaption(string text)
	{
		return new()
		{
			Text = text,
			FontWeight = FontWeights.SemiBold,
			Opacity = 0.7,
			Margin = new Thickness(left: 2, top: 2, right: 0, bottom: 4),
		};
	}

	private static void AddRows(Grid grid, params GridLength[] heights)
	{
		foreach (GridLength height in heights)
			grid.RowDefinitions.Add(new RowDefinition { Height = height });
	}

	private static void AddToRow(Grid grid, UIElement element, int row)
	{
		Grid.SetRow(element, row);
		grid.Children.Add(element);
	}

	private Border CreateStatusBar()
	{
		var bar = new DockPanel { Margin = new Thickness(left: 8, top: 3, right: 8, bottom: 3) };

		DockPanel.SetDock(_servers, Dock.Right);
		DockPanel.SetDock(_caret, Dock.Right);
		DockPanel.SetDock(_indentation, Dock.Right);
		DockPanel.SetDock(_encoding, Dock.Right);
		DockPanel.SetDock(_lineTerminator, Dock.Right);

		bar.Children.Add(_servers);
		bar.Children.Add(_caret);
		bar.Children.Add(_lineTerminator);
		bar.Children.Add(_encoding);
		bar.Children.Add(_indentation);
		bar.Children.Add(StatusLine);

		return new Border
		{
			Child = bar,
			BorderThickness = new Thickness(left: 0, top: 1, right: 0, bottom: 0),
			BorderBrush = Brushes.Gray,
		};
	}

	private Task<SampleSaveDecision> DecideSaveAllAsync(IReadOnlyList<SamplePane> panes)
	{
		MessageBoxResult answer = MessageBox.Show(
			owner: this,
			messageBoxText: $"Save the changes to {panes.Count} documents?",
			caption: "Unsaved changes",
			button: MessageBoxButton.YesNoCancel
		);

		return Task.FromResult(
			answer switch
			{
				MessageBoxResult.Yes => SampleSaveDecision.Save,
				MessageBoxResult.No => SampleSaveDecision.Discard,
				_ => SampleSaveDecision.Cancel,
			}
		);
	}

	private Task<SampleSaveDecision> DecideSaveAsync(SamplePane pane)
	{
		MessageBoxResult answer = MessageBox.Show(
			owner: this,
			messageBoxText: $"Save the changes to {pane.Title}?",
			caption: "Unsaved changes",
			button: MessageBoxButton.YesNoCancel
		);

		return Task.FromResult(
			answer switch
			{
				MessageBoxResult.Yes => SampleSaveDecision.Save,
				MessageBoxResult.No => SampleSaveDecision.Discard,
				_ => SampleSaveDecision.Cancel,
			}
		);
	}

	private async Task OpenAsync(SampleDialogKind dialog, string? path)
	{
		try
		{
			await OpenCoreAsync(dialog, path);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// A rename or a server start that fails is a message for the status line, not a crash of the window.
			Workbench.Report($"Failed: {exception.Message}");
		}
	}

	private async Task OpenCoreAsync(SampleDialogKind dialog, string? path)
	{
		switch (dialog)
		{
			case SampleDialogKind.FindAndReplace:
				if (_findWindow is null)
				{
					_findWindow = new SampleFindWindow(Workbench) { Owner = this };
					_findWindow.Closed += (_, _) => _findWindow = null;
					_findWindow.Show();
				}

				_findWindow.Prefill(Workbench.ActivePane?.Editor.SelectedText ?? string.Empty);
				_findWindow.Activate();
				break;

			case SampleDialogKind.Rename when Workbench.ActivePane is { } pane:
				if (
					new SampleInputWindow(
						title: "Rename symbol",
						prompt: "New name",
						accept: "_Rename",
						initialText: SampleWorkbench.GetSymbolAtCaret(pane)
					).Ask(this) is
					{ } newName
				)
				{
					await Workbench.RenameAsync(pane, newName);
				}

				break;

			case SampleDialogKind.KeyBindings:
				new SampleKeyBindingsWindow(Workbench) { Owner = this }.ShowDialog();
				break;

			case SampleDialogKind.LuaServer:
				if (
					PickFile(
						"Choose a LuaLS archive or a lua-language-server executable",
						"LuaLS archive or executable|*.zip;lua-language-server*",
						null
					) is
					{ } luaServer
				)
				{
					await Workbench.UseLuaServerAsync(luaServer);
				}

				break;

			case SampleDialogKind.RoslynServer:
				if (
					PickFile("Choose the Roslyn language server executable", "Roslyn language server|Microsoft.CodeAnalysis.LanguageServer*", path) is
					{ } roslynServer
				)
				{
					await Workbench.UseRoslynServerAsync(roslynServer);
				}

				break;

			case SampleDialogKind.NewFile when path is not null:
				if (
					new SampleInputWindow("New file", $"Name of the new file in {Path.GetFileName(path)}", "_Create").Ask(this) is
					{ Length: > 0 } fileName
				)
				{
					await Workbench.CreateFileAsync(path, fileName);
				}

				break;

			case SampleDialogKind.NewFolder when path is not null:
				if (
					new SampleInputWindow("New folder", $"Name of the new folder in {Path.GetFileName(path)}", "_Create").Ask(this) is
					{ Length: > 0 } folderName
				)
				{
					Workbench.CreateFolder(path, folderName);
				}

				break;

			case SampleDialogKind.RenameEntry when path is not null:
				if (
					new SampleInputWindow(
						title: "Rename",
						prompt: $"New name of {Path.GetFileName(path)}",
						accept: "_Rename",
						initialText: Path.GetFileName(path)
					).Ask(this) is
					{ Length: > 0 } entryName
				)
				{
					await Workbench.RenameEntryAsync(path, entryName);
				}

				break;

			case SampleDialogKind.DeleteEntry when path is not null:
				if (
					MessageBox.Show(
						owner: this,
						messageBoxText: $"Move {Path.GetFileName(path)} to the trash?",
						caption: "Delete",
						button: MessageBoxButton.OKCancel
					) == MessageBoxResult.OK
				)
				{
					await Workbench.DeleteEntryAsync(path);
				}

				break;
		}
	}

	private string? PickFile(string title, string filter, string? suggestedFile)
	{
		var picker = new OpenFileDialog
		{
			Title = title,
			Filter = filter,
			InitialDirectory = Path.GetDirectoryName(suggestedFile) ?? string.Empty,
		};

		return picker.ShowDialog(this) == true ? picker.FileName : null;
	}
}
