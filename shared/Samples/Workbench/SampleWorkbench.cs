using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Formatting;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;
using Nickelony.IDEKit.Processes;
using Nickelony.IDEKit.Workspace;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views.Coordination;
using Nickelony.LanguageServer.Provider;
using Nickelony.LanguageServer.Provider.Abstractions;
using Nickelony.LanguageServer.Roslyn;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Navigation;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

namespace Nickelony.Samples;

/// <summary>Owns the open documents of the sample, the theme they share, the language servers and the commands that act on them.</summary>
/// <remarks>
/// A window shows <see cref="Explorer"/>, the tabs of <see cref="Panes"/> and <see cref="Previews"/>, the menus of <see cref="Commands"/>
/// and the lists of <see cref="Panels"/>, and opens the child window that <see cref="DialogRequested"/> names; it holds no feature code of its own.
/// </remarks>
internal sealed partial class SampleWorkbench : IAsyncDisposable
{
	// A view publishes its text once typing pauses this long, so the other views of its document follow while the user types.
	private static readonly TimeSpan s_publishDelay = TimeSpan.FromMilliseconds(300);

	private static readonly string[] s_contextMenuCommandIds =
	[
		SampleCommandIds.GoToDefinition,
		SampleCommandIds.FindReferences,
		SampleCommandIds.Rename,
		SampleCommandIds.CodeActions,
		SampleCommandIds.ToggleComment,
		SampleCommandIds.Format,
		SampleCommandIds.Split,
		SampleCommandIds.Preview,
	];

	private readonly ILogger _logger;
	private readonly SampleDocuments _documents;
	private readonly UiDispatcher _dispatcher = UiDispatcher.FromCurrent();

	private readonly SampleProcessRunner _commandRunner = new(
		new ProcessRunner(new ProcessRunnerOptions { MaxCapturedCharactersPerStream = 20_000 })
	);

	private readonly SampleProjectDiagnostics _projectDiagnostics;
	private readonly List<SamplePane> _panes = [];
	private readonly List<SampleMarkdownPreview> _previews = [];
	private readonly Dictionary<SampleDocumentKind, WorkspaceDocumentSynchronizer> _serverMirrors = [];

	private ILanguageServerFeatureProvider? _luaServer;
	private ILanguageServerFeatureProvider? _cSharpServer;
	private ILanguageServerFeatureProvider? _visualBasicServer;

	private SampleSettings _settings;
	private SamplePane? _activePane;
	private int _viewCount;

	private bool _isDisposed;
	private bool _isRunning;

	private SampleWorkbench(SampleFiles files, ILogger logger, SampleDocuments documents, SampleSettings settings)
	{
		Files = files;
		_logger = logger;
		_documents = documents;
		_settings = settings;
		Theme = SampleThemes.Catalog.GetTheme(settings.Theme);
		Explorer = new SampleExplorer(files.Root);
		Panels = new SamplePanels(files.Root, ReadText);
		_projectDiagnostics = new SampleProjectDiagnostics(root: files.Root, CaptureEditorTexts, dispatcher: _dispatcher, logger);
		Commands = CreateCommands();

		KeyBindings = CreateKeyBindings(files);

		QuickPick = new SampleQuickPick(this, _dispatcher, logger);

		_documents.DocumentChanged += (_, _) => FormatChanged?.Invoke(this, EventArgs.Empty);
		_documents.DiskTextApplied += (_, e) => ApplyDiskText(e.Path, e.Text);
		_documents.EditorTextWritten += (_, e) => MarkWritten(e.Path, e.Text);
		_documents.StatusChanged += (_, e) => Report(e.Message);

		_documents.FilesChanged += (_, _) =>
		{
			Explorer.Refresh();
			_projectDiagnostics.HandleProjectChanged();
		};
	}

	/// <summary>Raised with a one-line message after a command ran or failed.</summary>
	internal event EventHandler<StatusChangedEventArgs>? StatusChanged;

	/// <summary>Raised with the text a run of the settings command produced.</summary>
	internal event EventHandler<OutputChangedEventArgs>? OutputChanged;

	/// <summary>
	/// Raised after <see cref="ActivePane"/> changed, for example when a navigation opened another document, so the window shows that pane.
	/// </summary>
	internal event EventHandler? ActivePaneChanged;

	/// <summary>Raised after a tab opened, closed or took the place of another, so the window updates its tabs.</summary>
	internal event EventHandler? TabsChanged;

	/// <summary>Raised after <see cref="Theme"/> changed, so the window chrome can follow the editors.</summary>
	internal event EventHandler? ThemeChanged;

	/// <summary>Raised after a language server started or stopped, so the status line follows.</summary>
	internal event EventHandler? LanguageServersChanged;

	/// <summary>Raised after the indentation of the active pane changed, so the status bar follows.</summary>
	internal event EventHandler? IndentationChanged;

	/// <summary>Raised after the encoding or the line terminators of the active pane may have changed, so the status bar follows.</summary>
	internal event EventHandler? FormatChanged;

	/// <summary>Raised when a command needs a child window, which only the window can open.</summary>
	internal event EventHandler<DialogRequestedEventArgs>? DialogRequested;

	/// <summary>The editor tabs, in the order they opened.</summary>
	internal IReadOnlyList<SamplePane> Panes => _panes;

	/// <summary>The Markdown previews, each a view of a document beside its editor.</summary>
	internal IReadOnlyList<SampleMarkdownPreview> Previews => _previews;

	/// <summary>
	/// The tabs of the second editor group: each preview, and each view that a split opened while an earlier view of its document is open.
	/// </summary>
	/// <remarks>
	/// A view shows beside the view it follows, so an edit in one shows in the other at once. The group stays empty when every tab
	/// would be in it, so closing the first views does not leave an empty group.
	/// </remarks>
	internal IReadOnlyList<object> TabsBeside
	{
		get
		{
			List<object> beside =
			[
				.. _panes.Where(pane => _panes.Any(other => other.Instance < pane.Instance && ShowSameDocument(other, pane))),
				.. _previews,
			];

			return beside.Count < _panes.Count + _previews.Count ? beside : [];
		}
	}

	/// <summary>The pane of the sample script, which opens with the window.</summary>
	/// <exception cref="InvalidOperationException">The tab of the script is closed.</exception>
	internal SamplePane LuaPane => FindPane(Files.LuaPath) ?? throw new InvalidOperationException("The tab of the sample script is closed.");

	/// <summary>The pane of the settings file, which opens with the window.</summary>
	/// <exception cref="InvalidOperationException">The tab of the settings file is closed.</exception>
	internal SamplePane SettingsPane => FindPane(Files.SettingsPath) ?? throw new InvalidOperationException("The tab of the settings is closed.");

	internal SampleFiles Files { get; }

	/// <summary>The language servers that run now, which the workspace symbol mode searches.</summary>
	/// <remarks>
	/// The in-process providers of the sample language list no workspace symbol, so a language that none of them serves is not searched.
	/// </remarks>
	internal IReadOnlyList<IWorkspaceSymbolProvider> WorkspaceSymbolProviders =>
		[.. new[] { _luaServer, _cSharpServer, _visualBasicServer }.OfType<IWorkspaceSymbolProvider>()];

	internal SampleExplorer Explorer { get; }

	internal SampleTheme Theme { get; private set; }

	/// <summary>
	/// The pane that has the focus, which the editing commands act on and the lists follow, or <see langword="null"/> when no tab is open.
	/// </summary>
	internal SamplePane? ActivePane
	{
		get => _activePane;
		set
		{
			if (value == _activePane)
				return;

			_activePane = value;
			Panels.Follow(value);
			ActivePaneChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	/// <summary>The rows of the outline, problems, references and search results lists.</summary>
	internal SamplePanels Panels { get; }

	/// <summary>What the find commands look for, which the find and replace window edits.</summary>
	internal SampleSearchOptions Search { get; set; } = new(string.Empty);

	/// <summary>The text that replaces every match.</summary>
	internal string ReplaceText { get; set; } = string.Empty;

	/// <summary>
	/// Asks whether to save a tab with unsaved changes that closes; the window shows the question, and without one the changes are dropped.
	/// </summary>
	internal Func<SamplePane, Task<SampleSaveDecision>> DecideSaveAsync { get; set; } = _ => Task.FromResult(SampleSaveDecision.Discard);

	/// <summary>
	/// Asks whether to save the documents with unsaved changes when the window closes; the window shows the question, and without one the changes
	/// are dropped.
	/// </summary>
	/// <remarks>
	/// The question covers every such document at once, so a window with one document asks the question
	/// of a closing tab instead, through <see cref="DecideSaveAsync"/>.
	/// </remarks>
	internal Func<IReadOnlyList<SamplePane>, Task<SampleSaveDecision>> DecideSaveAllAsync { get; set; } =
		_ => Task.FromResult(SampleSaveDecision.Discard);

	/// <summary>Which features answer the Lua files, as the status line shows it.</summary>
	internal string LuaFeaturesDescription => _luaServer is null ? "Lua: in-process providers" : "Lua: language server";

	/// <summary>Which of the C# and Visual Basic files a Roslyn server answers, as the status line shows it.</summary>
	internal string RoslynDescription =>
		(_cSharpServer, _visualBasicServer) switch
		{
			(null, _) => "C# and VB: highlighting only",
			(_, null) => "C#: Roslyn, VB: highlighting only",
			_ => "C# and VB: Roslyn",
		};

	/// <summary>The numbers of columns per level that the status bar offers for the indentation.</summary>
	internal static IReadOnlyList<int> IndentationSizes { get; } = [1, 2, 3, 4, 5, 6, 7, 8];

	/// <summary>
	/// The indentation of the active pane as the status bar shows it, such as "Spaces: 4" or "Tab Size: 4", or an empty text when no tab is open.
	/// </summary>
	internal string IndentationDescription => ActivePane?.Session.Editing.DescribeIndentation() ?? string.Empty;

	/// <summary>
	/// Says whether the style and the size of the active pane were detected or are the defaults, for the tooltip of the status bar item.
	/// </summary>
	internal string IndentationToolTip => ActivePane?.Session.Editing.DescribeIndentationSource() ?? string.Empty;

	/// <summary>The commands that convert the indentation of the active pane, in menu order.</summary>
	internal IReadOnlyList<SampleCommand> IndentationCommands =>
		[
			.. Commands.Where(command =>
				command.Id
					is SampleCommandIds.ConvertIndentationToSpaces
						or SampleCommandIds.ConvertIndentationToTabs
						or SampleCommandIds.ReindentSpaces
			),
		];

	/// <summary>The formats the status bar offers to reopen or save the active pane with, which name an encoding and a byte-order mark.</summary>
	internal static IReadOnlyList<TextFileFormat> EncodingFormats => SampleEncodings.Formats;

	/// <summary>The line terminator styles the status bar offers to convert the active pane to.</summary>
	internal static IReadOnlyList<LineTerminatorStyle> LineTerminatorStyles { get; } = [LineTerminatorStyle.Lf, LineTerminatorStyle.CrLf];

	/// <summary>
	/// The encoding of the active pane's document as the status bar shows it, such as "UTF-8 with BOM", or an empty text when no tab is open.
	/// </summary>
	internal string EncodingDescription => TryGetActiveFormat(out TextFileFormat format) ? format.EncodingDisplayName : string.Empty;

	/// <summary>Explains the encoding item of the status bar.</summary>
	internal string EncodingToolTip =>
		TryGetActiveFormat(out TextFileFormat format)
			? $"{format.EncodingDisplayName}: click to reopen the file or save it with another encoding"
			: string.Empty;

	/// <summary>
	/// The line terminators of the active pane's document as the status bar shows them: "LF", "CRLF", "CR" or "Mixed", or an empty text when no tab
	/// is open.
	/// </summary>
	internal string LineTerminatorDescription =>
		TryGetActiveFormat(out TextFileFormat format) ? format.LineTerminatorStyle.GetDisplayName() : string.Empty;

	/// <summary>
	/// Explains the line terminator item of the status bar, and says what a text without a line terminator or with mixed ones gets.
	/// </summary>
	internal string LineTerminatorToolTip =>
		!TryGetActiveFormat(out TextFileFormat format)
			? string.Empty
			: format.LineTerminatorStyle switch
			{
				LineTerminatorStyle.None =>
					$"The text has no line terminator yet, so the editor inserts {format.LineTerminatorStyle.GetDisplayName()} for the first new line",

				LineTerminatorStyle.Mixed => "The line terminators differ: click to convert them to one style",
				_ => $"{format.LineTerminatorStyle.GetDisplayName()}: click to convert the line terminators",
			};

	/// <summary>The commands of the sample, in menu order.</summary>
	internal IReadOnlyList<SampleCommand> Commands { get; }

	/// <summary>The commands of the editor's context menu, in menu order, after its clipboard commands.</summary>
	internal IReadOnlyList<SampleCommand> ContextMenuCommands =>
		[.. s_contextMenuCommandIds.Select(id => Commands.First(command => command.Id == id))];

	/// <summary>The key bindings that run the commands.</summary>
	internal SampleKeyBindings KeyBindings { get; }

	/// <summary>The command palette, which lists the commands with their shortcuts and runs the one the user picks.</summary>
	internal SampleQuickPick QuickPick { get; }

	/// <summary>Opens the folder in the document store, starts the language servers the options name and opens the script and the settings.</summary>
	/// <param name="files">The files the editors show.</param>
	/// <param name="options">The language servers to start, or <see langword="null"/> for none.</param>
	/// <param name="logger">Receives the contained failures of the editor controllers, or <see langword="null"/> to log nothing.</param>
	/// <remarks>
	/// Call it on the UI thread.
	/// </remarks>
	internal static async Task<SampleWorkbench> CreateAsync(SampleFiles files, SampleServerOptions? options = null, ILogger? logger = null)
	{
		options ??= SampleServerOptions.Default;
		logger ??= NullLogger.Instance;

		// The code pages of the encoding menus need the provider, which the libraries never register.
		SampleEncodings.RegisterCodePages();

		// A folder of the user's own may have no settings file, and then the defaults apply.
		SampleSettings settings =
			(File.Exists(files.SettingsPath) ? SampleSettings.FromJson(await File.ReadAllTextAsync(files.SettingsPath)) : null)
			?? SampleSettings.Default;

		var dispatcher = UiDispatcher.FromCurrent();
		var documents = new SampleDocuments(TrashRootOf(files), dispatcher, action => dispatcher.Post(action));
		SampleWorkbench workbench;

		try
		{
			workbench = new SampleWorkbench(files, logger, documents, settings);
		}
		catch
		{
			// The documents are already built, and no workbench exists to dispose them.
			await documents.DisposeAsync();
			throw;
		}

		try
		{
			if (options.LuaServerPath is { } luaServerPath)
			{
				workbench._luaServer = await StartLuaServerAsync(
					files,
					serverPath: luaServerPath,
					logger,
					hooks: workbench.CreateServerHooks("Lua"),
					report: workbench.ReportFromAnyThread
				);
			}

			if (options.RoslynServerPath is { } roslynServerPath)
				workbench.StartRoslyn(roslynServerPath);

			workbench.MirrorServer(SampleDocumentKind.Lua);
			workbench.MirrorServer(SampleDocumentKind.CSharp);
			workbench.MirrorServer(SampleDocumentKind.VisualBasic);

			await documents.Reloader.WatchFolderAsync(files.Root);

			// The script and the settings open in tabs when the folder has them, and the script is the active one.
			if (File.Exists(files.LuaPath))
				await workbench.OpenAsync(files.LuaPath);

			if (File.Exists(files.SettingsPath))
				await workbench.OpenAsync(files.SettingsPath, activate: workbench.ActivePane is null);

			// The project check has no analysis until the folder is reported once.
			workbench._projectDiagnostics.HandleProjectChanged();

			return workbench;
		}
		catch
		{
			// What the failed construction already started would otherwise outlive the exception.
			await workbench.DisposeAsync();
			throw;
		}
	}

	/// <summary>Shows a file in a tab: the tab that shows it already, or a new one.</summary>
	/// <param name="path">The file to show.</param>
	/// <param name="activate">Whether the tab becomes the active one.</param>
	/// <param name="encoding">
	/// The one encoding to read a file that no tab shows with, or <see langword="null"/> to try the candidates of the sample.
	/// </param>
	/// <remarks>
	/// A file that does not exist opens as a new, empty document, which a save creates.
	/// </remarks>
	internal async Task<SamplePane> OpenAsync(string path, bool activate = true, TextEncoding? encoding = null)
	{
		path = Path.GetFullPath(path);

		if (FindPane(path) is not { } pane)
		{
			string text = await _documents.OpenAsync(path, encoding);
			pane = CreatePane(path, text, instance: 1);
			await _documents.AttachAsync(path, pane.View);

			_panes.Add(pane);
			TabsChanged?.Invoke(this, EventArgs.Empty);
		}

		if (activate)
			ActivePane = pane;

		return pane;
	}

	/// <summary>Shows a file in a tab as the explorer does, and reports a failure in the status line with the failure code of the store.</summary>
	/// <param name="path">The file to show.</param>
	internal Task OpenFromExplorerAsync(string path) => RunSafelyAsync(async () => await OpenAsync(path));

	/// <summary>Shows a file in a tab read with one encoding, or reads the file again with it when a tab shows the file already.</summary>
	/// <param name="path">The file to show.</param>
	/// <param name="encoding">The encoding to read the file with.</param>
	/// <remarks>
	/// A file that no candidate encoding decodes opens this way, because the one encoding replaces the candidates for that file.
	/// </remarks>
	internal Task OpenWithEncodingAsync(string path, TextEncoding encoding) => RunSafelyAsync(() => OpenWithEncodingCoreAsync(path, encoding));

	/// <summary>Opens the document of a pane in a second editor, which the store keeps in step with the first as either one is typed in.</summary>
	internal async Task<SamplePane?> SplitAsync(SamplePane? pane)
	{
		if (pane is null)
			return null;

		int instance = _panes.Where(other => SampleDocuments.PathComparer.Equals(other.FilePath, pane.FilePath)).Max(other => other.Instance) + 1;

		SamplePane split = CreatePane(pane.FilePath, pane.Editor.Text, instance);
		await _documents.AttachAsync(pane.FilePath, split.View);

		_panes.Insert(_panes.IndexOf(pane) + 1, split);
		TabsChanged?.Invoke(this, EventArgs.Empty);
		ActivePane = split;
		Report($"{split.Title} shows the same document beside the first view; type in either one");

		return split;
	}

	/// <summary>Opens the rendered Markdown of a pane as a view of its document, which follows each edit.</summary>
	internal async Task<SampleMarkdownPreview?> OpenPreviewAsync(SamplePane? pane)
	{
		if (pane?.Session.Kind != SampleDocumentKind.Markdown)
		{
			Report("Only a Markdown file has a preview");
			return null;
		}

		// The editor text becomes the document first, so the preview shows what the user sees.
		await _documents.PublishAsync(pane.FilePath);

		var preview = new SampleMarkdownPreview($"preview {++_viewCount}", Theme);
		preview.DocumentRenamed += (_, _) => TabsChanged?.Invoke(this, EventArgs.Empty);
		await _documents.AttachAsync(pane.FilePath, preview);

		_previews.Add(preview);
		TabsChanged?.Invoke(this, EventArgs.Empty);

		return preview;
	}

	/// <summary>Closes a tab, first asking what to do with unsaved changes when no other tab shows the document.</summary>
	/// <returns>
	/// <see langword="true"/> when the tab closed; otherwise, <see langword="false"/> because the answer or a failed save kept it open.
	/// </returns>
	internal async Task<bool> CloseAsync(SamplePane pane)
	{
		bool isLastView = !_panes.Any(other => other != pane && SampleDocuments.PathComparer.Equals(other.FilePath, pane.FilePath));

		if (isLastView && pane.View.IsDirty)
		{
			switch (await DecideSaveAsync(pane))
			{
				case SampleSaveDecision.Cancel:
					return false;

				case SampleSaveDecision.Save:
					if (!await SaveAsync(pane))
						return false;

					break;

				default:
					await _documents.RevertAsync(pane.FilePath);
					break;
			}
		}

		RemovePane(pane);
		return true;
	}

	/// <summary>Asks once what to do with the unsaved changes of every document when the window closes.</summary>
	/// <returns>
	/// <see langword="true"/> when the window may close; otherwise, <see langword="false"/> because the answer or a failed save kept it open.
	/// </returns>
	/// <remarks>
	/// A document with several views counts once. Saving writes each document through the path of a closing tab, and the first save that fails keeps
	/// the window open, so no text is lost.
	/// </remarks>
	internal async Task<bool> ConfirmCloseAsync()
	{
		List<SamplePane> dirty = [.. _panes.Where(pane => pane.View.IsDirty).DistinctBy(pane => pane.FilePath, SampleDocuments.PathComparer)];

		if (dirty.Count == 0)
			return true;

		switch (dirty.Count == 1 ? await DecideSaveAsync(dirty[0]) : await DecideSaveAllAsync(dirty))
		{
			case SampleSaveDecision.Cancel:
				return false;

			case SampleSaveDecision.Save:
				foreach (SamplePane pane in dirty)
				{
					if (!await SaveAsync(pane))
						return false;
				}

				return true;

			default:
				return true;
		}
	}

	/// <summary>Closes a Markdown preview.</summary>
	internal void ClosePreview(SampleMarkdownPreview preview)
	{
		_documents.Detach(preview);
		_previews.Remove(preview);
		TabsChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Switches every editor and preview to the named theme of <see cref="SampleThemes.Catalog"/>.</summary>
	internal void SetTheme(string name)
	{
		Theme = SampleThemes.Catalog.GetTheme(name);

		foreach (SamplePane pane in _panes)
			pane.Session.ApplyTheme(Theme);

		foreach (SampleMarkdownPreview preview in _previews)
			preview.ApplyTheme(Theme);

		ThemeChanged?.Invoke(this, EventArgs.Empty);
		Report($"Theme: {Theme.Name}");
	}

	/// <summary>Switches to the other theme of the catalog.</summary>
	internal void ToggleTheme()
	{
		SampleTheme next = SampleThemes.Catalog.Themes.First(theme => theme.Name != Theme.Name);
		SetTheme(next.Name);
	}

	/// <summary>Writes the pane's text to its file and makes it the baseline of the change markers.</summary>
	/// <param name="pane">The pane whose document is saved.</param>
	/// <param name="format">The encoding and the byte-order mark to save with, or <see langword="null"/> to keep those of the document.</param>
	/// <returns><see langword="true"/> when the file was written; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Saving the settings file applies its values. A save that fails reports the outcome and the failure code of the store.
	/// </remarks>
	internal async Task<bool> SaveAsync(SamplePane? pane, TextFileFormat? format = null)
	{
		if (pane is null)
			return false;

		// A Markdown line that ends in two spaces breaks, so the trimming leaves Markdown alone.
		if (_settings.TrimTrailingWhitespace && pane.Session.Kind != SampleDocumentKind.Markdown)
			pane.Session.Editing.TrimTrailingWhitespace();

		WorkspaceDocumentCommitResult? committed = await _documents.CommitAsync(pane.FilePath, format);

		if (committed is not { Outcome: WorkspaceDocumentCommitOutcome.Committed, Snapshot: { } written })
		{
			string reason = Describe(committed?.Outcome, committed?.Failure);

			Report(
				format is { } failedFormat
					? $"Could not save {pane.Title} as {failedFormat.EncodingDisplayName}: {reason}"
					: $"Could not save {pane.Title}: {reason}"
			);

			FormatChanged?.Invoke(this, EventArgs.Empty);

			return false;
		}

		string text = written.Content;

		foreach (SamplePane view in PanesOf(pane.FilePath))
			view.Session.LineStatus.MarkSaved(text);

		if (SampleDocuments.PathComparer.Equals(pane.FilePath, Files.SettingsPath) && SampleSettings.FromJson(text) is { } settings)
		{
			ApplySettings(settings);
			_settings = settings;
			SetTheme(settings.Theme);
		}

		Report(format is { } savedFormat ? $"Saved {pane.Title} as {savedFormat.EncodingDisplayName}" : $"Saved {pane.Title}");
		FormatChanged?.Invoke(this, EventArgs.Empty);

		return true;
	}

	/// <summary>Converts the line terminators of the active pane to one style as one undo step, and has the document record the style.</summary>
	/// <param name="style">The style every line terminator takes.</param>
	/// <remarks>
	/// The view publishes the converted text with the style it shows, so the document needs no call beyond the publication
	/// that this member asks for. A text without a line terminator has nothing to convert, and the editor inserts its platform's terminator
	/// for the first new line.
	/// </remarks>
	internal Task UseLineTerminatorAsync(LineTerminatorStyle style) => RunSafelyAsync(() => UseLineTerminatorCoreAsync(style));

	/// <summary>Reads the active pane's file again with another encoding, asking what to do with unsaved changes first.</summary>
	/// <param name="format">
	/// The format that names the encoding; its byte-order mark is ignored, because a mark in the file decides the encoding.
	/// </param>
	internal Task ReopenWithEncodingAsync(TextFileFormat format) => RunSafelyAsync(() => ReopenCoreAsync(format.Encoding));

	/// <summary>Saves the active pane's document in another encoding.</summary>
	/// <param name="format">The encoding and the byte-order mark to write with.</param>
	internal Task SaveWithEncodingAsync(TextFileFormat format) => RunSafelyAsync(async () => await SaveAsync(ActivePane, format));

	/// <summary>Whether the document of the active pane records the line terminator style, which the status bar marks in its list.</summary>
	internal bool UsesLineTerminator(LineTerminatorStyle style) =>
		TryGetActiveFormat(out TextFileFormat format) && format.LineTerminatorStyle == style;

	/// <summary>Whether the document of the active pane is read with the encoding, which the status bar marks in its list for a reopen.</summary>
	internal bool UsesEncoding(TextEncoding encoding) => TryGetActiveFormat(out TextFileFormat active) && active.Encoding == encoding;

	/// <summary>
	/// Whether the document of the active pane is saved with the encoding and the byte-order mark, which the status bar marks in its list for a save.
	/// </summary>
	internal bool UsesFormat(TextFileFormat format) =>
		TryGetActiveFormat(out TextFileFormat active) && active.Encoding == format.Encoding && active.HasBom == format.HasBom;

	/// <summary>Saves every document with unsaved changes.</summary>
	internal async Task SaveAllAsync()
	{
		foreach (SamplePane pane in _panes.Where(pane => pane.View.IsDirty).DistinctBy(pane => pane.FilePath, SampleDocuments.PathComparer).ToList())
			await SaveAsync(pane);
	}

	/// <summary>Drops the unsaved changes of a pane's document, in every tab that shows it.</summary>
	internal async Task RevertAsync(SamplePane? pane)
	{
		if (pane is null)
			return;

		Report(await _documents.RevertAsync(pane.FilePath) ? $"Reverted {pane.Title}" : $"Could not revert {pane.Title}");
	}

	/// <summary>Runs the command line of the settings file in the folder of the files and publishes its output.</summary>
	internal async Task RunAsync()
	{
		if (_isRunning)
		{
			Report("A run is already in progress");
			return;
		}

		_isRunning = true;

		try
		{
			Report($"Running: {_settings.RunCommand}");

			string output = await _commandRunner.RunAsync(_settings.RunCommand, Files.Root);
			OutputChanged?.Invoke(this, new OutputChangedEventArgs(output));

			Report("Run finished");
		}
		finally
		{
			_isRunning = false;
		}
	}

	/// <summary>Shows a row of a list: it opens the file of the row when no tab shows it, activates its pane and selects the row's range.</summary>
	/// <returns><see langword="true"/> when the file could be shown; otherwise, <see langword="false"/>.</returns>
	internal async Task<bool> NavigateToAsync(SampleLocation location)
	{
		if (!File.Exists(location.DocumentId) && FindPane(location.DocumentId) is null)
		{
			Report($"{Path.GetFileName(location.DocumentId)} does not exist");
			return false;
		}

		SamplePane pane = await OpenAsync(location.DocumentId);

		// The pane may have become visible only now, so its layout runs before the location scrolls it.
		pane.Editor.UpdateLayout();

		int length = pane.Editor.Document.TextLength;
		int start = Math.Clamp(location.Range.Offset, 0, length);

		var selection = new TextRange(start, Math.Clamp(location.Range.Length, 0, length - start));

		pane.Editor.TextArea.ApplyLocation(
			new NavigationLocation(DocumentId: pane.FilePath, CaretOffset: start, selection, PreferredDocumentLine: null)
		);

		return true;
	}

	/// <summary>Moves the caret of the pane to the definition of the symbol at its caret and reports whether it moved.</summary>
	internal async Task GoToDefinitionAsync(SamplePane? pane)
	{
		if (pane is null)
			return;

		bool moved = await pane.Session.Language.Definitions.GoToDefinitionAsync();
		Report(moved ? "Moved to the definition" : "No definition was found at the caret");
	}

	/// <summary>Lists the references of the symbol at the caret of the pane in <see cref="SamplePanels.References"/>.</summary>
	internal async Task FindReferencesAsync(SamplePane? pane)
	{
		if (pane is null)
			return;

		IReadOnlyList<ReferenceLocation> references = await pane.Session.Language.References.FindAsync();
		int shown = Panels.ShowReferences(references);

		Report(references.Count == 0 ? "No references were found at the caret" : $"{shown} reference(s)");
	}

	/// <summary>Selects the first problem of the active pane after the caret, wrapping to the first one.</summary>
	internal bool GoToNextProblem()
	{
		IReadOnlyList<SampleLocation> problems = Panels.Problems;

		if (ActivePane is not { } pane)
			return false;

		if (problems.Count == 0)
		{
			Report($"{pane.Title} has no problems");
			return false;
		}

		int caret = pane.Editor.CaretOffset;
		SampleLocation next = problems.FirstOrDefault(problem => problem.Range.Offset > caret) ?? problems[0];

		pane.Editor.TextArea.ApplyLocation(
			new NavigationLocation(DocumentId: pane.FilePath, CaretOffset: next.Range.Offset, Selection: next.Range, PreferredDocumentLine: null)
		);

		return true;
	}

	/// <summary>Returns the word at the caret of the pane, which the rename window offers as the new name to edit.</summary>
	internal static string GetSymbolAtCaret(SamplePane pane) => pane.Editor.TextArea.GetWordFromOffset(pane.Editor.CaretOffset) ?? string.Empty;

	/// <summary>Renames the symbol at the caret of the pane in every document the language provider names, open or not.</summary>
	internal async Task RenameAsync(SamplePane pane, string newName)
	{
		if (string.IsNullOrWhiteSpace(newName))
		{
			Report("The new name is empty");
			return;
		}

		if (await pane.Session.Language.Rename.RequestEditsAsync(newName.Trim()) is not { } edit)
		{
			Report("There is no symbol to rename at the caret");
			return;
		}

		int changed = await _documents.ApplyEditAsync(edit, path => FindPane(path) is not null);
		Report($"Renamed in {changed} document(s)");
	}

	/// <summary>Selects the next match of <see cref="Search"/> in the active pane and reports whether one exists.</summary>
	internal bool FindNext() => ActivePane is { } pane && ReportFind(pane.Session.Search.FindNext(Search));

	/// <summary>Selects the previous match of <see cref="Search"/> in the active pane and reports whether one exists.</summary>
	internal bool FindPrevious() => ActivePane is { } pane && ReportFind(pane.Session.Search.FindPrevious(Search));

	/// <summary>Lists every match of <see cref="Search"/> in the active pane in <see cref="SamplePanels.SearchResults"/>.</summary>
	internal void FindAll()
	{
		if (ActivePane is not { } pane)
			return;

		IReadOnlyList<TextRange> matches = pane.Session.Search.FindAll(Search);
		Panels.ShowSearchResults([(pane.FilePath, pane.Editor.Text, matches)]);
		Report($"{matches.Count} match(es) for '{Search.FindText}'");
	}

	/// <summary>Lists every match of <see cref="Search"/> in every file of the folder, reading an open document from its editor.</summary>
	internal void FindInFiles()
	{
		if (Search.FindText.Length == 0)
		{
			Report("Type what to find in the find and replace window first");
			return;
		}

		List<(string Path, string Text, IReadOnlyList<TextRange> Matches)> files = [];

		foreach (
			string path in Directory.EnumerateFiles(Files.Root, "*", SearchOption.AllDirectories).Where(IsSearchable).Order(StringComparer.Ordinal)
		)
		{
			if (ReadText(path) is not { } text)
				continue;

			IReadOnlyList<TextRange> matches = SampleSearch.FindAll(text, Search);

			if (matches.Count > 0)
				files.Add((path, text, matches));
		}

		Panels.ShowSearchResults(files);
		Report($"{files.Sum(file => file.Matches.Count)} match(es) for '{Search.FindText}' in {files.Count} file(s)");
	}

	/// <summary>Replaces every match of <see cref="Search"/> in the active pane with <see cref="ReplaceText"/> as one undo step.</summary>
	internal void ReplaceAll()
	{
		if (ActivePane is not { } pane)
			return;

		int count = pane.Session.Search.ReplaceAll(Search, ReplaceText);
		Report($"Replaced {count} match(es)");
	}

	/// <summary>Answers the Lua files from a language server, or from the in-process providers again.</summary>
	/// <param name="serverPath">
	/// The path of a <c>lua-language-server</c> executable or of a LuaLS archive, or <see langword="null"/> for the in-process providers.
	/// </param>
	internal async Task UseLuaServerAsync(string? serverPath)
	{
		ILanguageServerFeatureProvider? server = serverPath is null
			? null
			: await StartLuaServerAsync(Files, serverPath, logger: _logger, hooks: CreateServerHooks("Lua"), report: ReportFromAnyThread);

		ILanguageServerFeatureProvider? previous = _luaServer;

		_luaServer = server;
		await ReplaceFeaturesAsync(SampleDocumentKind.Lua, previous);

		Report(
			server is null
				? "The in-process providers answer the Lua files"
				: "The Lua language server answers the Lua files; it starts on the first request"
		);
	}

	/// <summary>Answers the C# and Visual Basic files from the Roslyn language server, or leaves them with highlighting only again.</summary>
	/// <param name="serverPath">The path of the Roslyn language server executable, or <see langword="null"/> to stop it.</param>
	internal async Task UseRoslynServerAsync(string? serverPath)
	{
		ILanguageServerFeatureProvider? previousCSharp = _cSharpServer;
		ILanguageServerFeatureProvider? previousVisualBasic = _visualBasicServer;

		_cSharpServer = null;
		_visualBasicServer = null;

		if (serverPath is not null)
			StartRoslyn(serverPath);

		await ReplaceFeaturesAsync(SampleDocumentKind.CSharp, previousCSharp);
		await ReplaceFeaturesAsync(SampleDocumentKind.VisualBasic, previousVisualBasic);

		Report(
			serverPath is null ? "The C# and Visual Basic files have highlighting only"
			: _visualBasicServer is null
				? "Roslyn answers the C# files; this build of the server carries no Visual Basic, so the VB files keep highlighting only"
			: "Roslyn answers the C# and Visual Basic files; a server starts on the first request"
		);
	}

	/// <summary>Creates an empty file in a folder and opens it.</summary>
	internal async Task CreateFileAsync(string folder, string name)
	{
		string path = Path.Combine(folder, name.Trim());

		if (File.Exists(path) || Directory.Exists(path))
		{
			Report($"{name} already exists");
			return;
		}

		// The store opens a missing file as a new document, and the save writes it.
		SamplePane pane = await OpenAsync(path);
		await SaveAsync(pane);
		Explorer.Refresh();
	}

	/// <summary>Creates a folder.</summary>
	internal void CreateFolder(string folder, string name)
	{
		Directory.CreateDirectory(Path.Combine(folder, name.Trim()));
		Explorer.Refresh();
	}

	/// <summary>Renames a file or a folder through the store, and the tabs of every document it renames follow it.</summary>
	internal async Task RenameEntryAsync(string path, string newName)
	{
		string newPath = Path.Combine(Path.GetDirectoryName(path) ?? Files.Root, newName.Trim());
		string outcome = Directory.Exists(path) ? await _documents.RenameFolderAsync(path, newPath) : await _documents.RenameFileAsync(path, newPath);

		Explorer.Refresh();
		Report($"Rename of {Path.GetFileName(path)}: {outcome}");
	}

	/// <summary>Moves a file or a folder into the trash through the store, and closes the tabs of every document it removes.</summary>
	internal async Task DeleteEntryAsync(string path)
	{
		string outcome = Directory.Exists(path) ? await _documents.DeleteFolderAsync(path) : await _documents.DeleteFileAsync(path);

		if (outcome == "Deleted")
		{
			foreach (SamplePane pane in _panes.Where(pane => IsAtOrUnder(pane.FilePath, path)).ToList())
				RemovePane(pane);

			foreach (SampleMarkdownPreview preview in _previews.Where(preview => preview.DocumentId is { } id && IsAtOrUnder(id, path)).ToList())
				ClosePreview(preview);
		}

		Explorer.Refresh();
		Report($"Delete of {Path.GetFileName(path)}: {outcome}");
	}

	// A keybindings.json that the service rejects, such as one of another schema version or with two commands on one chord, leaves the
	// catalog defaults in force. The file stays as the user wrote it until a rebinding saves over it.
	private SampleKeyBindings CreateKeyBindings(SampleFiles files)
	{
		try
		{
			return CreateKeyBindings(new JsonKeyBindingOverrideStore(files.KeyBindingsPath));
		}
		catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or ArgumentException)
		{
			LogKeyBindingsRejected(_logger, exception, files.KeyBindingsPath);
			return CreateKeyBindings(new JsonKeyBindingOverrideStore(files.KeyBindingsPath, loadsExistingFile: false));
		}
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "The key bindings in {Path} were rejected, so the default key bindings apply.")]
	private static partial void LogKeyBindingsRejected(ILogger logger, Exception exception, string path);

	private SampleKeyBindings CreateKeyBindings(JsonKeyBindingOverrideStore overridesStore) =>
		new(
			Commands,
			overridesStore,
			isEditorFocused: () => ActivePane?.Editor.TextArea.IsKeyboardFocusWithin == true,
			canExecuteInEditor: commandId => ActivePane is { } pane && CanExecute(pane, commandId),
			runsAnywhere: commandId => Commands.Any(command => command.Id == commandId && command.RunsAnywhere),
			execute: ExecuteCommand
		);

	public async ValueTask DisposeAsync()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;

		// The project check and the palette stop on the UI thread, which this call runs on until its first await.
		_projectDiagnostics.Dispose();
		QuickPick.Dispose();

		// The document store stops first, so no reload can touch an editor that is already disposed.
		await _documents.DisposeAsync();

		foreach (SamplePane pane in _panes)
		{
			pane.Session.Dispose();
			pane.View.Dispose();
		}

		foreach (WorkspaceDocumentSynchronizer mirror in _serverMirrors.Values)
			mirror.Dispose();

		foreach (ILanguageServerFeatureProvider? server in new[] { _luaServer, _cSharpServer, _visualBasicServer })
		{
			if (server is not null)
				await server.DisposeAsync();
		}
	}

	internal void Report(string message) => StatusChanged?.Invoke(this, new StatusChangedEventArgs(message));

	private static async Task<ILanguageServerFeatureProvider> StartLuaServerAsync(
		SampleFiles files,
		string serverPath,
		ILogger logger,
		LanguageServerFeatureProviderHooks hooks,
		Action<string> report
	)
	{
		// Extracting an archive reads megabytes, so it runs off the UI thread.
		string executable = await Task.Run(() => LuaServerArchive.Resolve(serverPath, LuaServerArchive.DefaultExtractionRoot));
		return SampleLanguageServers.CreateLua(files, executablePath: executable, logger, hooks, reportFailure: report);
	}

	private static string TrashRootOf(SampleFiles files) =>
		Path.Combine(Path.GetDirectoryName(files.Root) ?? files.Root, $"{Path.GetFileName(files.Root)}.Trash");

	private static bool IsAtOrUnder(string path, string root)
	{
		return SampleDocuments.PathComparer.Equals(path, root)
			|| path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsSearchable(string path) =>
		SampleFiles.KindOf(path) != SampleDocumentKind.PlainText || Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase);

	private void StartRoslyn(string serverPath)
	{
		_cSharpServer = SampleLanguageServers.CreateRoslyn(
			Files,
			executablePath: serverPath,
			language: RoslynLanguage.CSharp,
			logger: _logger,
			hooks: CreateServerHooks("C#"),
			reportFailure: ReportFromAnyThread
		);

		// The build of the server that Visual Studio Code installs carries C# only, and a Visual Basic server over it would answer nothing.
		if (SampleServerOptions.ServesVisualBasic(serverPath))
		{
			_visualBasicServer = SampleLanguageServers.CreateRoslyn(
				Files,
				executablePath: serverPath,
				language: RoslynLanguage.VisualBasic,
				logger: _logger,
				hooks: CreateServerHooks("Visual Basic"),
				reportFailure: ReportFromAnyThread
			);
		}
	}

	private async Task ReplaceFeaturesAsync(SampleDocumentKind kind, ILanguageServerFeatureProvider? previous)
	{
		foreach (SamplePane pane in _panes.Where(pane => pane.Session.Kind == kind))
			pane.Session.ReplaceLanguage(FeaturesFor(pane.FilePath));

		// The mirror of the old server closes its documents, so the old server has nothing left to answer.
		MirrorServer(kind);

		if (previous is not null)
			await previous.DisposeAsync();

		LanguageServersChanged?.Invoke(this, EventArgs.Empty);
	}

	// Mirrors the documents of a kind to the server that answers them now; the mirror of the server that answered them before closes them there.
	private void MirrorServer(SampleDocumentKind kind)
	{
		if (_serverMirrors.Remove(kind, out WorkspaceDocumentSynchronizer? previous))
			previous.Dispose();

		ILanguageServerFeatureProvider? server = kind switch
		{
			SampleDocumentKind.Lua => _luaServer,
			SampleDocumentKind.CSharp => _cSharpServer,
			SampleDocumentKind.VisualBasic => _visualBasicServer,
			_ => null,
		};

		if (server is not null)
			_serverMirrors.Add(kind, _documents.SynchronizeServer(server, path => SampleFiles.KindOf(path) == kind));
	}

	private SampleLanguageFeatures FeaturesFor(string path)
	{
		if (SampleDocuments.PathComparer.Equals(path, Files.SettingsPath))
			return SampleLanguageFeatures.CreateSettings(SampleSettingsSchema.Index);

		if (SampleProjectDiagnostics.Covers(path))
			return SampleLanguageFeatures.None with { DiagnosticsSource = _projectDiagnostics.Source };

		return SampleFiles.KindOf(path) switch
		{
			SampleDocumentKind.Lua => _luaServer is { } lua ? SampleLanguageFeatures.FromServer(lua) : SampleLanguageFeatures.CreateLua(),
			SampleDocumentKind.CSharp when _cSharpServer is { } cSharp => SampleLanguageFeatures.FromServer(cSharp),
			SampleDocumentKind.VisualBasic when _visualBasicServer is { } visualBasic => SampleLanguageFeatures.FromServer(visualBasic),
			_ => SampleLanguageFeatures.None,
		};
	}

	// An editor allows its text only on the UI thread, which the project check's hook runs on, so the check reads the text of each open
	// editor of a covered file from here and the files on disk by itself.
	private IReadOnlyDictionary<string, string> CaptureEditorTexts()
	{
		var texts = new Dictionary<string, string>(SampleDocuments.PathComparer);

		foreach (SamplePane pane in _panes.Where(pane => SampleProjectDiagnostics.Covers(pane.FilePath)))
			texts.TryAdd(pane.FilePath, pane.Editor.Text);

		return texts;
	}

	private void ReportFromAnyThread(string message) => _dispatcher.Post(() => Report(message));

	private LanguageServerFeatureProviderHooks CreateServerHooks(string serverName) =>
		SampleLanguageServers.CreateHooks(serverName, ReportFromAnyThread, ApplyServerEditAsync);

	/// <summary>Applies an edit that a language server asked for, on the UI thread, and says whether the documents took it.</summary>
	/// <remarks>
	/// A server asks on a connection thread, for example while it runs the command of a quick fix, and the documents belong to the UI thread.
	/// An edit can name any file the user can write, so the sample refuses one that names a file outside its folder.
	/// </remarks>
	internal Task<bool> ApplyServerEditAsync(WorkspaceEdit edit)
	{
		var applied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		_dispatcher.Post(() => _ = ApplyServerEditOnUiThreadAsync(edit, applied));

		return applied.Task;
	}

	private async Task ApplyServerEditOnUiThreadAsync(WorkspaceEdit edit, TaskCompletionSource<bool> applied)
	{
		try
		{
			if (edit.DocumentEdits.Any(documentEdit => !IsAtOrUnder(documentEdit.DocumentId, Files.Root)))
				throw new InvalidOperationException("it names a file outside the sample folder");

			await ApplyEditAsync(edit);
			applied.SetResult(true);
		}
		catch (InvalidOperationException exception)
		{
			// The edit named a file outside the folder or a document the store could not change as asked, so the server hears that nothing was applied.
			Report($"The edit of the language server was not applied: {exception.Message}");
			applied.SetResult(false);
		}
		catch (Exception exception)
		{
			applied.SetException(exception);
		}
	}

	private bool ReportFind(bool found)
	{
		Report(found ? $"Found '{Search.FindText}'" : $"No match for '{Search.FindText}'");
		return found;
	}

	// The views of one document share its key through a rename, while their panes follow the new path one after the other, so the key tells
	// whether two panes show one document and the path only decides for a view that has no document yet. A tab that left its group during the
	// rename would come back blank.
	private static bool ShowSameDocument(SamplePane first, SamplePane second)
	{
		return first.View.DocumentKey is { } key && second.View.DocumentKey is { } otherKey
			? key == otherKey
			: SampleDocuments.PathComparer.Equals(first.FilePath, second.FilePath);
	}

	private SamplePane? FindPane(string documentId) => _panes.FirstOrDefault(pane => SampleDocuments.PathComparer.Equals(pane.FilePath, documentId));

	private IEnumerable<SamplePane> PanesOf(string path) => _panes.Where(pane => SampleDocuments.PathComparer.Equals(pane.FilePath, path));

	// A tab shows its editor text, which can be ahead of the file; a file without a tab is read from disk.
	private string? ReadText(string path)
	{
		if (FindPane(path) is { } pane)
			return pane.Editor.Text;

		try
		{
			return File.Exists(path) ? File.ReadAllText(path) : null;
		}
		catch (IOException)
		{
			return null;
		}
	}

	private SamplePane CreatePane(string path, string text, int instance)
	{
		var editor = new TextEditor
		{
			FontFamily = new("Consolas, Menlo, monospace"),
			ShowLineNumbers = true,
			Text = text,
		};

		editor.FontSize = _settings.FontSize;

		var session = new SampleEditorSession(
			editor,
			documentId: path,
			Theme,
			features: FeaturesFor(path),
			hoverDelay: _settings.HoverDelay,
			ApplyEditAsync,
			navigateToDocumentAsync: NavigateToDefinitionAsync,
			logger: _logger
		);

		session.LineStatus.Bind(path, editor.Text);

		// The text shows how it indents, and the tab size of the settings serves where it shows nothing.
		session.Editing.DetectIndentation(_settings.TabSize);

		var view = new SampleDocumentView($"{path} #{++_viewCount}", editor, s_publishDelay);
		var pane = new SamplePane(editor, session, view, instance);

		// The coordinator moves the view while a rename runs, so the pane follows once the rename is done.
		view.DocumentRenamed += (_, _) => _dispatcher.Post(() => FollowRename(pane));
		view.ConflictChanged += (_, _) => _dispatcher.Post(() => KeepTypedText(pane));
		Panels.Watch(pane);

		// An edit of a covered file changes what the project check finds, so the check runs again once the typing settles.
		editor.TextChanged += (_, _) =>
		{
			if (SampleProjectDiagnostics.Covers(pane.FilePath))
				_projectDiagnostics.HandleProjectChanged();
		};

		return pane;
	}

	// Two views of one document changed the same lines before either published; as a reload conflict keeps the editor text, the view whose
	// publication came second keeps its side of those lines, every other change of both views stays, and the status line says so.
	private void KeepTypedText(SamplePane pane)
	{
		if (_isDisposed || !pane.View.HasConflict)
			return;

		pane.View.KeepViewText();
		Report($"{Path.GetFileName(pane.FilePath)}: two views changed the same lines, and the later change replaced the side of the other view");
	}

	// The coordinator moved the view with the document, so the pane keeps its editor and view and rebuilds only the language session, whose
	// controllers serve one document id.
	private void FollowRename(SamplePane pane)
	{
		if (
			_isDisposed
			|| !_panes.Contains(pane)
			|| pane.View.DocumentId is not { } newPath
			|| SampleDocuments.PathComparer.Equals(newPath, pane.FilePath)
		)
		{
			return;
		}

		pane.Session.FollowRename(newPath, FeaturesFor(newPath));
		_projectDiagnostics.HandleProjectChanged();
		TabsChanged?.Invoke(this, EventArgs.Empty);
	}

	private async Task ApplyEditAsync(WorkspaceEdit edit)
	{
		int changed = await _documents.ApplyEditAsync(edit, path => FindPane(path) is not null);
		Report($"The action changed {changed} document(s)");
	}

	private void RemovePane(SamplePane pane)
	{
		int index = _panes.IndexOf(pane);

		if (index < 0)
			return;

		DisposePane(pane);
		_panes.RemoveAt(index);
		TabsChanged?.Invoke(this, EventArgs.Empty);

		if (pane == _activePane)
			ActivePane = _panes.Count == 0 ? null : _panes[Math.Min(index, _panes.Count - 1)];
	}

	private void DisposePane(SamplePane pane)
	{
		_documents.Detach(pane.View);

		pane.View.Dispose();
		pane.Session.Dispose();
	}

	private async Task<bool> NavigateToDefinitionAsync(DefinitionLocation location, CancellationToken cancellationToken)
	{
		// A location in the requesting document moves its caret in the binding, so this callback serves the other documents only.
		if (location.DocumentId is not { } documentId)
			return false;

		SamplePane pane = await OpenAsync(documentId);
		int offset = TextLineMap.Build(pane.Editor.Text).GetOffset(location.NavigationStart);

		return await NavigateToAsync(new SampleLocation(string.Empty, pane.FilePath, new TextRange(offset, 0)));
	}

	private static bool CanExecute(SamplePane pane, string commandId)
	{
		// A document without line comments refuses the command and lets the stroke through.
		return commandId != SampleCommandIds.ToggleComment
			|| pane.Session.Kind is SampleDocumentKind.Lua or SampleDocumentKind.CSharp or SampleDocumentKind.VisualBasic;
	}

	internal void ExecuteCommand(string commandId) => Commands.FirstOrDefault(command => command.Id == commandId)?.Execute();

	// A command that acts on the window applies at any time, and one that acts on the editor applies when a tab is open and accepts the command.
	internal bool CanExecuteNow(string commandId)
	{
		return Commands.FirstOrDefault(command => command.Id == commandId) is { } found
			&& (found.RunsAnywhere || (ActivePane is { } pane && CanExecute(pane, commandId)));
	}

	private List<SampleCommand> CreateCommands()
	{
		// Every command reads the active pane when it runs, so it acts on the editor that has the focus. The quick pick is built after
		// the commands, so its commands stay lambdas: a method group would read QuickPick now, while it is still null.
#pragma warning disable IDE0200
		return
		[
			new(
				Id: SampleCommandIds.NewFile,
				Caption: "_New file...",
				Menu: "_File",
				Execute: () => RequestDialog(SampleDialogKind.NewFile, ActiveFolder()),
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.NewFolder,
				Caption: "New f_older...",
				Menu: "_File",
				Execute: () => RequestDialog(SampleDialogKind.NewFolder, ActiveFolder()),
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.Save,
				Caption: "_Save",
				Menu: "_File",
				Execute: () => RunSafely(() => SaveAsync(ActivePane)),
				BeginsGroup: true,
				RunsAnywhere: true
			),

			new(Id: SampleCommandIds.SaveAll, Caption: "Save _all", Menu: "_File", Execute: () => RunSafely(SaveAllAsync), RunsAnywhere: true),

			new(
				Id: SampleCommandIds.Revert,
				Caption: "_Revert",
				Menu: "_File",
				Execute: () => RunSafely(() => RevertAsync(ActivePane)),
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.CloseTab,
				Caption: "_Close tab",
				Menu: "_File",
				Execute: () => RunSafely(CloseActiveAsync),
				BeginsGroup: true,
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.Complete,
				Caption: "_Complete word",
				Menu: "_Edit",
				Execute: () => RunSafely(() => ActivePane?.Session.Language.Completion?.RequestAsync() ?? Task.CompletedTask)
			),

			new(Id: SampleCommandIds.CodeActions, Caption: "_Quick fix", Menu: "_Edit", Execute: () => RunSafely(QuickFixAsync)),

			new(
				Id: SampleCommandIds.ToggleComment,
				Caption: "Toggle line _comment",
				Menu: "_Edit",
				Execute: () => ActivePane?.Session.Editing.ToggleLineComment(),
				BeginsGroup: true
			),

			new(
				Id: SampleCommandIds.TrimWhitespace,
				Caption: "_Trim trailing whitespace",
				Menu: "_Edit",
				Execute: () => ActivePane?.Session.Editing.TrimTrailingWhitespace()
			),

			new(Id: SampleCommandIds.Format, Caption: "_Format document", Menu: "_Edit", Execute: () => RunSafely(() => FormatAsync(ActivePane))),

			new(
				Id: SampleCommandIds.ConvertIndentationToSpaces,
				Caption: "Convert indentation to s_paces",
				Menu: "_Edit",

				Execute: () =>
					ChangeIndentation(
						editing => editing.ConvertIndentationToSpaces(),
						"Converted the indentation to spaces",
						"No line is indented with a tab; the editor now inserts spaces"
					),

				BeginsGroup: true
			),

			new(
				Id: SampleCommandIds.ConvertIndentationToTabs,
				Caption: "Convert indentation to t_abs",
				Menu: "_Edit",

				Execute: () =>
					ChangeIndentation(
						editing => editing.ConvertIndentationToTabs(),
						"Converted the indentation to tabs",
						"No indentation fills a tab stop; the editor now inserts tabs"
					)
			),

			new(
				Id: SampleCommandIds.ReindentSpaces,
				Caption: "Re_indent spaces to the indent size",
				Menu: "_Edit",

				Execute: () =>
					ChangeIndentation(
						editing => editing.ReindentSpaces(),
						"Resized the space indentation to the indent size",
						"The text shows no space indentation of another size"
					)
			),

			new(
				Id: SampleCommandIds.Rename,
				Caption: "_Rename symbol...",
				Menu: "_Edit",
				Execute: () => RequestDialog(SampleDialogKind.Rename),
				BeginsGroup: true
			),

			new(
				Id: SampleCommandIds.ToggleBookmark,
				Caption: "Toggle _bookmark",
				Menu: "_Edit",
				Execute: () => ActivePane?.Session.LineStatus.ToggleBookmark(ActivePane.Editor.CaretOffset)
			),

			new(
				Id: SampleCommandIds.FindAndReplace,
				Caption: "_Find and replace...",
				Menu: "_Search",
				Execute: () => RequestDialog(SampleDialogKind.FindAndReplace),
				RunsAnywhere: true
			),

			new(Id: SampleCommandIds.FindNext, Caption: "Find _next", Menu: "_Search", Execute: () => FindNext(), BeginsGroup: true),
			new(Id: SampleCommandIds.FindPrevious, Caption: "Find _previous", Menu: "_Search", Execute: () => FindPrevious()),
			new(Id: SampleCommandIds.FindAll, Caption: "Find _all", Menu: "_Search", Execute: FindAll),
			new(Id: SampleCommandIds.FindInFiles, Caption: "Find in f_iles", Menu: "_Search", Execute: FindInFiles, RunsAnywhere: true),
			new(Id: SampleCommandIds.ReplaceAll, Caption: "_Replace all", Menu: "_Search", Execute: ReplaceAll),

			new(
				Id: SampleCommandIds.QuickOpen,
				Caption: "Go to _file...",
				Menu: "_Navigate",
				Execute: () => QuickPick.OpenFiles(),
				RunsAnywhere: true
			),

			new(Id: SampleCommandIds.GoToLine, Caption: "Go to _line...", Menu: "_Navigate", Execute: () => QuickPick.OpenGoToLine()),
			new(Id: SampleCommandIds.GoToSymbol, Caption: "Go to _symbol in editor...", Menu: "_Navigate", Execute: () => QuickPick.OpenSymbols()),

			new(
				Id: SampleCommandIds.GoToWorkspaceSymbol,
				Caption: "Go to symbol in _workspace...",
				Menu: "_Navigate",
				Execute: () => QuickPick.OpenWorkspaceSymbols(),
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.GoToDefinition,
				Caption: "Go to _definition",
				Menu: "_Navigate",
				Execute: () => RunSafely(() => GoToDefinitionAsync(ActivePane)),
				BeginsGroup: true
			),

			new(
				Id: SampleCommandIds.FindReferences,
				Caption: "Find all _references",
				Menu: "_Navigate",
				Execute: () => RunSafely(() => FindReferencesAsync(ActivePane))
			),

			new(Id: SampleCommandIds.NextProblem, Caption: "Next _problem", Menu: "_Navigate", Execute: () => GoToNextProblem(), BeginsGroup: true),

			new(
				Id: SampleCommandIds.Split,
				Caption: "_Split editor",
				Menu: "_View",
				Execute: () => RunSafely(() => SplitAsync(ActivePane)),
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.Preview,
				Caption: "Open _preview",
				Menu: "_View",
				Execute: () => RunSafely(() => OpenPreviewAsync(ActivePane)),
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.ToggleTheme,
				Caption: "Toggle _theme",
				Menu: "_View",
				Execute: ToggleTheme,
				BeginsGroup: true,
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.CommandPalette,
				Caption: "Command _palette...",
				Menu: "_View",
				Execute: () => QuickPick.OpenCommands(),
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.KeyBindings,
				Caption: "_Key bindings...",
				Menu: "_View",
				Execute: () => RequestDialog(SampleDialogKind.KeyBindings),
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.StartLuaServer,
				Caption: "Start the _Lua language server...",
				Menu: "_Language",
				Execute: () => RequestDialog(SampleDialogKind.LuaServer),
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.UseInProcessLua,
				Caption: "Use the _in-process Lua providers",
				Menu: "_Language",
				Execute: () => RunSafely(() => UseLuaServerAsync(null)),
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.StartRoslynServer,
				Caption: "Start the _Roslyn language server...",
				Menu: "_Language",
				Execute: () => RequestDialog(SampleDialogKind.RoslynServer, SampleServerOptions.FindInstalledRoslynServer()),
				BeginsGroup: true,
				RunsAnywhere: true
			),

			new(
				Id: SampleCommandIds.StopRoslynServer,
				Caption: "St_op the Roslyn language server",
				Menu: "_Language",
				Execute: () => RunSafely(() => UseRoslynServerAsync(null)),
				RunsAnywhere: true
			),

			new(Id: SampleCommandIds.Run, Caption: "_Run command", Menu: "_Run", Execute: () => RunSafely(RunAsync), RunsAnywhere: true),
		];
#pragma warning restore IDE0200
	}

	/// <summary>Whether the active pane indents as given, which the status bar marks in its list of sizes.</summary>
	internal bool UsesIndentation(bool insertSpaces, int size) => ActivePane?.Session.Editing.UsesIndentation(insertSpaces, size) == true;

	/// <summary>Makes the active pane indent with spaces or with a tab at the given number of columns per level, and leaves its text alone.</summary>
	internal void UseIndentation(bool insertSpaces, int size)
	{
		if (ActivePane is not { } pane)
			return;

		pane.Session.Editing.UseIndentation(insertSpaces, size);
		ShareIndentation(pane);

		Report(
			insertSpaces
				? $"Indent using spaces: {size} per level; the text is unchanged"
				: $"Indent using tabs: a tab is {size} wide; the text is unchanged"
		);

		IndentationChanged?.Invoke(this, EventArgs.Empty);
	}

	private void ChangeIndentation(Func<SampleEditingFeatures, bool> change, string changed, string unchanged)
	{
		if (ActivePane is not { } pane)
			return;

		Report(change(pane.Session.Editing) ? changed : unchanged);
		ShareIndentation(pane);
		IndentationChanged?.Invoke(this, EventArgs.Empty);
	}

	// The other views of the document receive the converted text from the store, but each editor has options of its own, so they follow
	// the indentation of the view that changed it.
	private void ShareIndentation(SamplePane pane)
	{
		foreach (SamplePane other in PanesOf(pane.FilePath).Where(other => other != pane))
			other.Session.Editing.FollowIndentation(pane.Session.Editing);
	}

	private bool TryGetActiveFormat(out TextFileFormat format)
	{
		if (ActivePane is { } pane)
			return _documents.TryGetFormat(pane.FilePath, out format);

		format = default;
		return false;
	}

	private async Task UseLineTerminatorCoreAsync(LineTerminatorStyle style)
	{
		if (ActivePane is not { } pane)
			return;

		string label = style.GetDisplayName();

		if (pane.Editor.ConvertLineTerminators(style))
		{
			// The view publishes once typing pauses, so the document records the style at once only when asked.
			await _documents.PublishAsync(pane.FilePath);
			Report($"Converted the line terminators to {label}; one undo step restores them");
		}
		else if (pane.Editor.Document.LineCount == 1)
		{
			Report($"The text has no line terminator yet; the editor inserts {LineTerminatorStyle.None.GetDisplayName()} for the first new line");
		}
		else
		{
			Report($"Every line terminator is {label} already");
		}

		FormatChanged?.Invoke(this, EventArgs.Empty);
	}

	private async Task OpenWithEncodingCoreAsync(string path, TextEncoding encoding)
	{
		path = Path.GetFullPath(path);

		if (FindPane(path) is { } shown)
		{
			ActivePane = shown;
			await ReopenCoreAsync(encoding);
			return;
		}

		SamplePane pane = await OpenAsync(path, encoding: encoding);
		Report($"Opened {pane.Title} with {encoding.DisplayName}");
		FormatChanged?.Invoke(this, EventArgs.Empty);
	}

	private async Task ReopenCoreAsync(TextEncoding encoding)
	{
		if (ActivePane is not { } pane)
			return;

		string name = encoding.DisplayName;
		WorkspaceDocumentReloadResult? reloaded = await _documents.ReopenAsync(pane.FilePath, encoding);

		// The store re-reads no document that has unsaved changes, because the text would replace them, so the user decides what happens to them.
		if (reloaded?.Outcome == WorkspaceDocumentReloadOutcome.UnsavedChanges)
		{
			Report($"{pane.Title} has unsaved changes ({reloaded.Outcome}): save or discard them to reopen it with {name}");

			switch (await DecideSaveAsync(pane))
			{
				case SampleSaveDecision.Cancel:
					Report($"Reopen with {name} canceled: {pane.Title} keeps its unsaved changes");
					return;

				case SampleSaveDecision.Save:
					if (!await SaveAsync(pane))
						return;

					break;

				default:
					if (!await _documents.RevertAsync(pane.FilePath))
					{
						Report($"Could not discard the changes of {pane.Title}");
						return;
					}

					break;
			}

			reloaded = await _documents.ReopenAsync(pane.FilePath, encoding);
		}

		switch (reloaded)
		{
			case { Outcome: WorkspaceDocumentReloadOutcome.Reloaded, Snapshot: { } snapshot }:
				MarkWritten(pane.FilePath, snapshot.Content);
				Report($"Reopened {pane.Title} with {name}");
				break;

			case { Outcome: WorkspaceDocumentReloadOutcome.Unchanged }:
				Report($"{pane.Title} reads the same with {name}");
				break;

			default:
				Report($"Reopen with {name} failed: {Describe(reloaded?.Outcome, reloaded?.Failure)}");
				break;
		}

		FormatChanged?.Invoke(this, EventArgs.Empty);
	}

	// The outcome and the failure code of a store result, or the reason a view gave no result: a view held back its text, or the document is not tracked.
	private static string Describe(object? outcome, WorkspaceOperationFailure? failure)
	{
		return outcome is null ? "a view holds back its text"
			: failure is null ? outcome.ToString()!
			: $"{outcome} ({failure.Code})";
	}

	private string ActiveFolder() => ActivePane is { } pane ? Path.GetDirectoryName(pane.FilePath) ?? Files.Root : Files.Root;

	private void RequestDialog(SampleDialogKind dialog, string? path = null) =>
		DialogRequested?.Invoke(this, new DialogRequestedEventArgs(dialog, path));

	private async Task CloseActiveAsync()
	{
		if (ActivePane is { } pane)
			await CloseAsync(pane);
	}

	private async Task QuickFixAsync()
	{
		if (ActivePane?.Session.Language.CodeActions is not { } codeActions || !await codeActions.OpenMenuAsync())
			Report("No quick fix here");
	}

	private async Task FormatAsync(SamplePane? pane)
	{
		if (pane is null)
			return;

		SampleFormatting formatting = pane.Session.Language.Formatting;

		if (!formatting.CanFormat)
		{
			// Only a language server answers formatting requests, so without one the command trims the trailing whitespace.
			pane.Session.Editing.TrimTrailingWhitespace();
			Report("No formatting provider is configured: trimmed the trailing whitespace");
			return;
		}

		FormattingOutcome outcome = await formatting.FormatAsync();
		Report($"Format: {outcome}");
	}

	private void RunSafely(Func<Task> work) => _ = RunSafelyAsync(work);

	private async Task RunSafelyAsync(Func<Task> work)
	{
		try
		{
			await work();
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			Report($"Failed: {exception.Message}");
		}
	}

	private void MarkWritten(string path, string text)
	{
		// The editor text is on disk now, so it is the new baseline of the change markers unless the user typed meanwhile.
		foreach (SamplePane pane in PanesOf(path).Where(pane => !_isDisposed && pane.Editor.Text == text))
			pane.Session.LineStatus.MarkSaved(text);
	}

	private void ApplyDiskText(string path, string text)
	{
		if (_isDisposed || FindPane(path) is not { } pane)
			return;

		// The view already put the text in the editor. Text typed after the reload read the editor differs from it and is not on disk yet.
		if (pane.Editor.Text != text)
		{
			Report($"{pane.Title} changed on disk; the editor kept the text typed meanwhile");
			return;
		}

		foreach (SamplePane view in PanesOf(path))
			view.Session.LineStatus.MarkSaved(text);

		Report($"{pane.Title} changed on disk and was reloaded");
		FormatChanged?.Invoke(this, EventArgs.Empty);
	}

	private void ApplySettings(SampleSettings settings)
	{
		foreach (SamplePane pane in _panes)
		{
			pane.Editor.FontSize = settings.FontSize;
			pane.Session.Editing.DetectIndentation(settings.TabSize);
			pane.Session.ApplyHoverDelay(settings.HoverDelay);
		}

		// The tab size is the default of every tab that shows no indentation, so each tab detects again.
		IndentationChanged?.Invoke(this, EventArgs.Empty);
	}
}
