using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.KeyBindings.Avalonia;

namespace Nickelony.Samples.Avalonia;

/// <summary>
/// The main window of the Avalonia sample: a menu, the explorer and the outline, the editor tabs with a second group for split views and previews,
/// the lists of the workbench and a status bar.
/// </summary>
/// <remarks>
/// The window lays out Avalonia controls over the workbench, which owns every feature, so the WPF head shows the same menus and rows.
/// The window chrome takes the light or dark variant of the editor theme.
/// </remarks>
internal sealed class SampleWindow : Window
{
	private readonly TabControl _tabs = new();
	private readonly TabControl _tabsBeside = new() { IsVisible = false };

	private readonly GridSplitter _groupSplitter = new() { ResizeDirection = GridResizeDirection.Columns, IsVisible = false };
	private readonly Grid _groups = new() { ColumnDefinitions = new ColumnDefinitions("*,0,0") };
	private readonly TabControl _lists = new();
	private readonly TreeView _explorer = new();

	private readonly ListBox _outline = CreateList();
	private readonly ListBox _problems = CreateList();
	private readonly ListBox _references = CreateList();
	private readonly ListBox _searchResults = CreateList();

	private readonly TextBox _output = new() { IsReadOnly = true, FontFamily = new("Consolas, Menlo, monospace") };
	private readonly TextBlock _caret = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0) };

	private readonly TextBlock _indentation = new()
	{
		VerticalAlignment = VerticalAlignment.Center,
		Margin = new Thickness(left: 0, top: 0, right: 16, bottom: 0),
		Cursor = new Cursor(StandardCursorType.Hand),
	};

	private readonly ContextMenu _indentationMenu = new();
	private readonly List<(bool InsertSpaces, int Size, MenuItem Item)> _indentationSizes = [];

	private readonly TextBlock _encoding = new()
	{
		VerticalAlignment = VerticalAlignment.Center,
		Margin = new Thickness(left: 0, top: 0, right: 16, bottom: 0),
		Cursor = new Cursor(StandardCursorType.Hand),
	};

	private readonly ContextMenu _encodingMenu = new();

	private readonly List<(TextFileFormat Format, MenuItem Item)> _reopenEncodings = [];
	private readonly List<(TextFileFormat Format, MenuItem Item)> _saveEncodings = [];

	private readonly TextBlock _lineTerminator = new()
	{
		VerticalAlignment = VerticalAlignment.Center,
		Margin = new Thickness(left: 0, top: 0, right: 16, bottom: 0),
		Cursor = new Cursor(StandardCursorType.Hand),
	};

	private readonly ContextMenu _lineTerminatorMenu = new();
	private readonly List<(LineTerminatorStyle Style, MenuItem Item)> _lineTerminators = [];
	private readonly TextBlock _servers = new() { VerticalAlignment = VerticalAlignment.Center };
	private readonly List<(SampleCommand Command, TextBlock Shortcut)> _shortcuts = [];
	private readonly Dictionary<object, TabItem> _tabItems = [];
	private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);

	private SampleFindWindow? _findWindow;
	private bool _isClosing;

	/// <param name="workbench">The workbench that owns the editors; the window disposes it when it closes.</param>
	internal SampleWindow(SampleWorkbench workbench)
	{
		Workbench = workbench;

		Title = "Nickelony sample (Avalonia)";
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
		ApplyThemeVariant();
		ShowServers();
		CreateIndentationMenu();
		ShowIndentation();
		CreateFormatMenus();
		ShowFormat();
	}

	/// <summary>The workbench that owns the editors.</summary>
	internal SampleWorkbench Workbench { get; }

	/// <summary>The line terminators as the status bar shows them, which a test reads.</summary>
	internal string? LineTerminatorText => _lineTerminator.Text;

	protected override void OnClosed(EventArgs e)
	{
		Workbench.KeyBindings.RepeatGate?.Dispose();
		base.OnClosed(e);
	}

	protected override async void OnClosing(WindowClosingEventArgs e)
	{
		base.OnClosing(e);

		if (_isClosing || e.Cancel)
			return;

		// The workbench disposes asynchronously, and the application exits once the main window is gone, so the window stays until it is done.
		// A document with unsaved changes is asked about first, and the window stays open when the answer is Cancel or a save fails.
		e.Cancel = true;
		_isClosing = true;
		bool closes = false;

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

	private static ListBox CreateList() => new() { FontFamily = new("Consolas, Menlo, monospace"), FontSize = 12 };

	private void Subscribe()
	{
		SamplePanels panels = Workbench.Panels;

		Workbench.StatusChanged += (_, e) =>
		{
			// A long message is cut with an ellipsis, and the tooltip holds the whole of it.
			StatusLine.Text = e.Message;
			ToolTip.SetTip(StatusLine, e.Message);
		};

		Workbench.OutputChanged += (_, e) => Show(_output, () => _output.Text = e.Output);
		Workbench.KeyBindings.BindingsChanged += (_, _) => ShowShortcuts();

		Workbench.ThemeChanged += (_, _) =>
		{
			ApplyThemeVariant();
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
			// A tap on a row navigates even when the row is already selected, which a selection change would miss.
			list.Tapped += (_, _) => NavigateToSelection(list);

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
				if (e.Source == group && group.SelectedItem is TabItem { Tag: SamplePane pane })
					Workbench.ActivePane = pane;
			};
		}

		_explorer.DoubleTapped += (_, _) => OpenSelectedFile();

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
		Workbench.KeyBindings.RepeatGate = new KeyRepeatGate(this);
		AddHandler(KeyDownEvent, (_, e) => e.Handled |= Workbench.KeyBindings.HandleKeyDown(e), RoutingStrategies.Tunnel);
		Deactivated += (_, _) => Workbench.KeyBindings.CancelPendingChords();
	}

	private void Show(Control list, Action update)
	{
		update();
		_lists.SelectedIndex = _lists.Items.Cast<TabItem>().ToList().FindIndex(tab => tab.Content == list);
	}

	private void NavigateToSelection(ListBox list)
	{
		if (list.SelectedItem is SampleLocation location)
			_ = Workbench.NavigateToAsync(location);
	}

	private void ApplyThemeVariant()
	{
		// The editors take their colors from the sample theme, and the chrome around them takes the matching Fluent variant.
		if (Application.Current is { } application)
			application.RequestedThemeVariant = Workbench.Theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
	}

	private void ShowServers() => _servers.Text = $"{Workbench.LuaFeaturesDescription}    {Workbench.RoslynDescription}";

	private void ShowIndentation()
	{
		_indentation.Text = Workbench.IndentationDescription;
		ToolTip.SetTip(_indentation, Workbench.IndentationToolTip);

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

		_indentation.PointerReleased += (_, _) => _indentationMenu.Open(_indentation);
	}

	private MenuItem CreateIndentationSizeMenu(string caption, bool insertSpaces)
	{
		var menu = new MenuItem { Header = new AccessText { Text = caption } };

		foreach (int size in SampleWorkbench.IndentationSizes)
		{
			MenuItem item = CreateMenuItem(
				size.ToString(System.Globalization.CultureInfo.InvariantCulture),
				() => Workbench.UseIndentation(insertSpaces, size)
			);

			item.ToggleType = MenuItemToggleType.CheckBox;

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
		ToolTip.SetTip(_encoding, Workbench.EncodingToolTip);
		_lineTerminator.Text = Workbench.LineTerminatorDescription;
		ToolTip.SetTip(_lineTerminator, Workbench.LineTerminatorToolTip);

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
		var reopen = new MenuItem { Header = new AccessText { Text = "_Reopen with encoding" } };

		foreach (TextFileFormat format in SampleWorkbench.EncodingFormats.DistinctBy(format => format.Encoding))
		{
			MenuItem item = CreateMenuItem(format.Encoding.DisplayName, () => _ = Workbench.ReopenWithEncodingAsync(format));
			item.ToggleType = MenuItemToggleType.CheckBox;

			_reopenEncodings.Add((format, item));
			reopen.Items.Add(item);
		}

		var save = new MenuItem { Header = new AccessText { Text = "_Save with encoding" } };

		foreach (TextFileFormat format in SampleWorkbench.EncodingFormats)
		{
			MenuItem item = CreateMenuItem(format.EncodingDisplayName, () => _ = Workbench.SaveWithEncodingAsync(format));
			item.ToggleType = MenuItemToggleType.CheckBox;

			_saveEncodings.Add((format, item));
			save.Items.Add(item);
		}

		_encodingMenu.Items.Add(reopen);
		_encodingMenu.Items.Add(save);

		_encoding.PointerReleased += (_, _) => _encodingMenu.Open(_encoding);

		foreach (LineTerminatorStyle style in SampleWorkbench.LineTerminatorStyles)
		{
			MenuItem item = CreateMenuItem(style.GetDisplayName(), () => _ = Workbench.UseLineTerminatorAsync(style));
			item.ToggleType = MenuItemToggleType.CheckBox;

			_lineTerminators.Add((style, item));
			_lineTerminatorMenu.Items.Add(item);
		}

		_lineTerminator.PointerReleased += (_, _) => _lineTerminatorMenu.Open(_lineTerminator);
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
		if (_tabsBeside.IsVisible == isShown)
			return;

		_tabsBeside.IsVisible = isShown;
		_groupSplitter.IsVisible = isShown;
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
			pane.Editor.AddHandler(
				PointerPressedEvent,

				(_, e) =>
				{
					TextArea textArea = pane.Editor.TextArea;

					if (
						e.GetCurrentPoint(pane.Editor).Properties.IsRightButtonPressed
						&& !(textArea.TryGetTextOffsetFromPoint(e.GetPosition(textArea), out int offset) && textArea.Selection.Contains(offset))
					)
					{
						_ = pane.Editor.TryMoveCaretToMousePosition(e);
					}
				},

				RoutingStrategies.Tunnel
			);

			pane.Editor.TextArea.Caret.PositionChanged += (_, _) => ShowCaret();
			pane.Editor.TextChanged += (_, _) => ShowFormatOfOtherView(pane);
			pane.View.DirtyChanged += (_, _) => UpdateHeader(tab);

			// Both groups show a selected tab, so a click into the editor of the other group makes its pane the active one.
			pane.Editor.TextArea.GotFocus += (_, _) => Workbench.ActivePane = pane;
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
			Padding = new Thickness(5, 0),
			Margin = new Thickness(left: 6, top: 0, right: 0, bottom: 0),
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			VerticalAlignment = VerticalAlignment.Center,
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

	private static MenuItem CreateMenuItem(string caption, Action execute)
	{
		var item = new MenuItem { Header = new AccessText { Text = caption } };
		item.Click += (_, _) => execute();

		return item;
	}

	// The explorer is rebuilt from the tree of the workbench after each change, keeping the folders the user opened and the selected entry.
	private void ShowExplorer(string? selected = null)
	{
		// Only a request to show a file moves the list to its row; a rebuild after a change of the folder leaves the scrolling where the user put it.
		bool revealsSelection = selected is not null;
		selected ??= SelectedEntry()?.Path;

		// Rebuilding the items resets the scroll position, so the offset is put back once the new items have a layout.
		ScrollViewer? scroller = _explorer.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
		Vector offset = scroller?.Offset ?? default;

		_explorer.Items.Clear();

		foreach (SampleExplorerNode node in Workbench.Explorer.Root.Children)
			_explorer.Items.Add(CreateExplorerItem(node));

		// The tree scrolls a row that it selects to the edge of the view, so the selection of the rebuild leaves the scrolling to this method: the
		// row of a file stays where it is while it is in view, and scrolls into view when it is not.
		TreeViewItem? item = selected is null ? null : FindExplorerItem(_explorer.Items, selected);
		_explorer.AutoScrollToSelectedItem = false;

		// The tree takes no selection from an item that is selected before it joins the tree, so the tree selects it.
		_explorer.SelectedItem = item;
		_explorer.ContextMenu = CreateExplorerMenu();

		Dispatcher.UIThread.Post(
			() =>
			{
				if (scroller is not null)
				{
					scroller.Offset = offset;

					if (revealsSelection && item is not null && !IsInView(item, scroller))
						item.BringIntoView();
				}

				_explorer.AutoScrollToSelectedItem = true;
			},

			DispatcherPriority.Loaded
		);
	}

	private static bool IsInView(Control row, ScrollViewer scroller) =>
		row.TranslatePoint(default, scroller) is { } top && top.Y >= -1.0 && top.Y + row.Bounds.Height <= scroller.Viewport.Height + 1.0;

	/// <summary>The status line, which a test reads.</summary>
	internal TextBlock StatusLine { get; } = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

	/// <summary>The offset by which the explorer is scrolled, which a test reads.</summary>
	internal Vector ExplorerScrollOffset => _explorer.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset ?? default;

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
			FontWeight = node.IsFolder ? FontWeight.SemiBold : FontWeight.Normal,
			IsExpanded = node.IsFolder && _expandedFolders.Contains(node.Path),
		};

		if (node.IsFolder)
		{
			item.PropertyChanged += (_, e) =>
			{
				if (e.Property != TreeViewItem.IsExpandedProperty)
					return;

				if (item.IsExpanded)
					_expandedFolders.Add(node.Path);
				else
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
		var openWith = new MenuItem { Header = new AccessText { Text = "Open with _encoding" } };

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
		// The shortcut is the display text of the key bindings, so a rebinding shows in the menu at once.
		var shortcut = new TextBlock { Margin = new Thickness(left: 32, top: 0, right: 0, bottom: 0), Opacity = 0.6 };
		var header = new DockPanel();

		DockPanel.SetDock(shortcut, Dock.Right);

		header.Children.Add(shortcut);
		header.Children.Add(new AccessText { Text = command.Caption });
		_shortcuts.Add((command, shortcut));

		shortcut.Text = Workbench.KeyBindings.GetDisplayText(command.Id);

		var item = new MenuItem { Header = header };
		item.Click += (_, _) => command.Execute();

		return item;
	}

	private void ShowShortcuts()
	{
		foreach ((SampleCommand command, TextBlock shortcut) in _shortcuts)
			shortcut.Text = Workbench.KeyBindings.GetDisplayText(command.Id);
	}

	private Grid CreateBody()
	{
		var filter = new TextBox { PlaceholderText = "Filter the outline", Margin = new Thickness(left: 0, top: 0, right: 0, bottom: 4) };
		filter.TextChanged += (_, _) => Workbench.Panels.OutlineFilter = filter.Text ?? string.Empty;

		var outline = new DockPanel();
		DockPanel.SetDock(filter, Dock.Top);

		outline.Children.Add(filter);
		outline.Children.Add(_outline);

		// The explorer sits above the outline, and splitters let each part of the window take the room it needs.
		var side = new Grid { RowDefinitions = new RowDefinitions("Auto,*,5,Auto,*"), Margin = new Thickness(left: 6, top: 6, right: 0, bottom: 6) };

		AddToRow(side, CreateCaption("Explorer"), 0);
		AddToRow(side, _explorer, 1);
		AddToRow(side, new GridSplitter { ResizeDirection = GridResizeDirection.Rows }, 2);
		AddToRow(side, CreateCaption("Outline"), 3);
		AddToRow(side, outline, 4);

		// The second group shows a split view or a preview beside the tab it follows.
		Grid.SetColumn(_groupSplitter, 1);
		Grid.SetColumn(_tabsBeside, 2);

		_groups.Children.Add(_tabs);
		_groups.Children.Add(_groupSplitter);
		_groups.Children.Add(_tabsBeside);

		var editorArea = new Grid { RowDefinitions = new RowDefinitions("*,5,180"), Margin = new Thickness(left: 0, top: 6, right: 6, bottom: 6) };

		AddToRow(editorArea, _groups, 0);
		AddToRow(editorArea, new GridSplitter { ResizeDirection = GridResizeDirection.Rows }, 1);
		AddToRow(editorArea, _lists, 2);

		var body = new Grid { ColumnDefinitions = new ColumnDefinitions("250,5,*") };
		Grid.SetColumn(editorArea, 2);

		body.Children.Add(side);
		body.Children.Add(new GridSplitter { [Grid.ColumnProperty] = 1, ResizeDirection = GridResizeDirection.Columns });
		body.Children.Add(editorArea);

		return body;
	}

	private static TextBlock CreateCaption(string text)
	{
		return new()
		{
			Text = text,
			FontWeight = FontWeight.SemiBold,
			Opacity = 0.7,
			Margin = new Thickness(left: 2, top: 2, right: 0, bottom: 4),
		};
	}

	private static void AddToRow(Grid grid, Control control, int row)
	{
		Grid.SetRow(control, row);
		grid.Children.Add(control);
	}

	private Border CreateStatusBar()
	{
		var bar = new DockPanel { Margin = new Thickness(8, 3) };

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

	private Task<SampleSaveDecision> DecideSaveAsync(SamplePane pane) => AskToSaveAsync($"Save the changes to {pane.Title}?", "_Save");

	private Task<SampleSaveDecision> DecideSaveAllAsync(IReadOnlyList<SamplePane> panes) =>
		AskToSaveAsync($"Save the changes to {panes.Count} documents?", "_Save all");

	private async Task<SampleSaveDecision> AskToSaveAsync(string question, string saveCaption)
	{
		int? answer = await new SampleChoiceWindow("Unsaved changes", question, saveCaption, "_Don't save", "Cancel").ShowDialog<int?>(this);

		return answer switch
		{
			0 => SampleSaveDecision.Save,
			1 => SampleSaveDecision.Discard,
			_ => SampleSaveDecision.Cancel,
		};
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
					_findWindow = new SampleFindWindow(Workbench);
					_findWindow.Closed += (_, _) => _findWindow = null;
					_findWindow.Show(this);
				}

				_findWindow.Prefill(Workbench.ActivePane?.Editor.SelectedText ?? string.Empty);
				_findWindow.Activate();
				break;

			case SampleDialogKind.Rename when Workbench.ActivePane is { } pane:
				string? newName = await new SampleInputWindow(
					title: "Rename symbol",
					prompt: "New name",
					accept: "_Rename",
					initialText: SampleWorkbench.GetSymbolAtCaret(pane)
				).ShowDialog<string?>(this);

				if (newName is not null)
					await Workbench.RenameAsync(pane, newName);

				break;

			case SampleDialogKind.KeyBindings:
				await new SampleKeyBindingsWindow(Workbench).ShowDialog(this);
				break;

			case SampleDialogKind.LuaServer:
				if (
					await PickFileAsync("Choose a LuaLS archive or a lua-language-server executable", ["*.zip", "lua-language-server*"], null) is
					{ } luaServer
				)
				{
					await Workbench.UseLuaServerAsync(luaServer);
				}

				break;

			case SampleDialogKind.RoslynServer:
				if (
					await PickFileAsync("Choose the Roslyn language server executable", ["Microsoft.CodeAnalysis.LanguageServer*"], path) is
					{ } roslynServer
				)
				{
					await Workbench.UseRoslynServerAsync(roslynServer);
				}

				break;

			case SampleDialogKind.NewFile when path is not null:
				if (
					await new SampleInputWindow("New file", $"Name of the new file in {Path.GetFileName(path)}", "_Create").ShowDialog<string?>(
						this
					) is
					{ Length: > 0 } fileName
				)
				{
					await Workbench.CreateFileAsync(path, fileName);
				}

				break;

			case SampleDialogKind.NewFolder when path is not null:
				if (
					await new SampleInputWindow("New folder", $"Name of the new folder in {Path.GetFileName(path)}", "_Create").ShowDialog<string?>(
						this
					) is
					{ Length: > 0 } folderName
				)
				{
					Workbench.CreateFolder(path, folderName);
				}

				break;

			case SampleDialogKind.RenameEntry when path is not null:
				if (
					await new SampleInputWindow(
						title: "Rename",
						prompt: $"New name of {Path.GetFileName(path)}",
						accept: "_Rename",
						initialText: Path.GetFileName(path)
					).ShowDialog<string?>(this) is
					{ Length: > 0 } entryName
				)
				{
					await Workbench.RenameEntryAsync(path, entryName);
				}

				break;

			case SampleDialogKind.DeleteEntry when path is not null:
				if (
					await new SampleChoiceWindow("Delete", $"Move {Path.GetFileName(path)} to the trash?", "_Delete", "Cancel").ShowDialog<int?>(this)
					== 0
				)
				{
					await Workbench.DeleteEntryAsync(path);
				}

				break;
		}
	}

	private async Task<string?> PickFileAsync(string title, IReadOnlyList<string> patterns, string? suggestedFile)
	{
		IStorageFolder? start = Path.GetDirectoryName(suggestedFile) is { } folder ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null;

		IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
			new FilePickerOpenOptions
			{
				Title = title,
				SuggestedStartLocation = start,
				FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns }],
			}
		);

		return files.Count > 0 ? files[0].TryGetLocalPath() : null;
	}
}
