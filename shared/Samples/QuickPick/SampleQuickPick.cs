using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Media;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Controllers.WorkspaceSymbols;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;
using Nickelony.IDEKit.Workspace.QuickPick;
using Nickelony.QuickPick;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;
using Nickelony.QuickPick.Avalonia;
using IconImage = Avalonia.Media.IImage;
using PlacementTarget = Avalonia.Controls.Control;
#else
using System.Windows;
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.Media;
using Nickelony.IDEKit.AvalonEdit.Navigation;
using Nickelony.QuickPick.Wpf;
using IconImage = System.Windows.Media.ImageSource;
using PlacementTarget = System.Windows.UIElement;
#endif

namespace Nickelony.Samples;

/// <summary>
/// Drives the quick pick of the sample: the files, the commands, the open editors, the go to line and the symbol modes, in a window near the top
/// of the main window.
/// </summary>
/// <remarks>
/// <para>
/// The files mode has no prefix, so it opens with Ctrl+P; the other modes are picked by typing their prefix,
/// and <c>?</c> lists them. The icon of a command is a dot in the color of its category, and an unsaved document shows an orange dot,
/// which shows how the window asks the host for the image of an icon key. A file name followed by a colon and a line, as in <c>sample.lua:12</c>,
/// opens the file at that line.
/// </para>
/// <para>
/// Ctrl+Shift+O opens the symbols of the active document (<c>@</c>, and <c>@:</c> grouped by kind), and Ctrl+T searches the symbols
/// of the workspace (<c>#</c>) in the language servers that run. The icon of a symbol is a dot in the color of the group its kind belongs to.
/// </para>
/// </remarks>
internal sealed partial class SampleQuickPick : IDisposable
{
	private static readonly ArgbColor[] s_categoryColors =
	[
		ArgbColor.FromRgb(0x2E, 0x86, 0xDE),
		ArgbColor.FromRgb(0x27, 0xAE, 0x60),
		ArgbColor.FromRgb(0xE6, 0x7E, 0x22),
		ArgbColor.FromRgb(0x8E, 0x44, 0xAD),
		ArgbColor.FromRgb(0xC0, 0x39, 0x2B),
		ArgbColor.FromRgb(0x16, 0xA0, 0x85),
	];

	private static readonly Dictionary<string, ArgbColor> s_symbolColors = CreateSymbolColors();

	private readonly Dictionary<string, IconImage> _icons = [];

	private readonly WorkspaceFileQuickPickProvider _files;

	private QuickPickPresenter? _presenter;

	/// <param name="workbench">Owns the tabs, the folder and the commands that the modes list and act on.</param>
	/// <param name="dispatcher">The dispatcher of the UI thread, which marshals the refresh of the file list.</param>
	/// <param name="logger">Receives the contained failures of the controller and of the file list.</param>
	/// <remarks>
	/// The controller belongs to the creating thread, which is the UI thread, and takes the synchronization context that the thread has.
	/// </remarks>
	internal SampleQuickPick(SampleWorkbench workbench, IUiDispatcher dispatcher, ILogger logger)
	{
		string root = LocalPath.Normalize(workbench.Files.Root);

		_files = new WorkspaceFileQuickPickProvider(
			rootPaths: [root],
			pathComparison: SampleDocuments.Options.PathComparison,
			openFileAsync: (request, _) => OpenFileAsync(workbench, request),
			listOpenEditors: () => ListOpenEditors(workbench),
			options: new WorkspaceFileQuickPickOptions { ExcludeGlobs = ["bin", "obj", ".git"] },
			reportFault: exception => LogContainedFailure(logger, exception)
		);

		// Every provider answers to its own prefix, so the sample offers each one without an override.
		Controller = new QuickPickController(
			modes:
			[
				new QuickPickMode(_files),
				new QuickPickMode(new CommandQuickPickProvider(workbench.KeyBindings, workbench.CanExecuteNow, workbench.ExecuteCommand)),

				new QuickPickMode(
					new OpenEditorsQuickPickProvider(
						() => ListOpenEditors(workbench),
						editor => ActivateEditor(workbench, editor),
						new OpenEditorsQuickPickOptions { RootPaths = [root] }
					)
				),

				new QuickPickMode(
					new GoToLineQuickPickProvider(() => workbench.ActivePane is { } pane ? new TextEditorNavigationTarget(pane.Editor) : null)
				),

				new QuickPickMode(
					new DocumentSymbolQuickPickProvider(
						() => DescribeActiveDocument(workbench),
						reportFault: exception => LogContainedFailure(logger, exception)
					)
				),

				new QuickPickMode(
					new DocumentSymbolQuickPickProvider(
						() => DescribeActiveDocument(workbench),

						DocumentSymbolQuickPickOptions.Default with
						{
							GroupByKind = true,
						},

						exception => LogContainedFailure(logger, exception)
					)
				),

				new QuickPickMode(
					new WorkspaceSymbolQuickPickProvider(
						getProviders: () => workbench.WorkspaceSymbolProviders,
						openAsync: (symbol, _) => OpenSymbolAsync(workbench, symbol),
						options: new WorkspaceSymbolQuickPickOptions { RootPaths = [root] },
						reportFault: exception => LogContainedFailure(logger, exception)
					)
				),
			],

			recentStore: new JsonQuickPickRecentStore(workbench.Files.QuickPickRecentsPath),
			reportFault: exception => LogContainedFailure(logger, exception),
			options: QuickPickControllerOptions.Default
		);

		// The list follows the folder on a background thread, so an open window asks for a new list on the UI thread.
		_files.FilesChanged += (_, _) => dispatcher.Post(Controller.Refresh);
	}

	/// <summary>The controller of the palette, which a window shows through <see cref="Attach"/>.</summary>
	internal QuickPickController Controller { get; }

	/// <summary>Whether the palette window is open, which a test reads.</summary>
	internal bool IsWindowOpen => _presenter?.IsOpen == true;

	/// <summary>Opens the quick pick in the files mode, which has no prefix.</summary>
	internal void OpenFiles() => Controller.Open();

	/// <summary>Opens the quick pick in the command mode.</summary>
	internal void OpenCommands() => Controller.Open(CommandQuickPickProvider.ModePrefix);

	/// <summary>Opens the quick pick in the go to line mode.</summary>
	internal void OpenGoToLine() => Controller.Open(":");

	/// <summary>Opens the quick pick in the mode that lists the symbols of the active document.</summary>
	internal void OpenSymbols() => Controller.Open("@");

	/// <summary>Opens the quick pick in the mode that searches the symbols of the workspace.</summary>
	internal void OpenWorkspaceSymbols() => Controller.Open("#");

	/// <summary>Shows the palette in a window placed against an element of the main window.</summary>
	/// <param name="host">The element the palette hangs from.</param>
	/// <param name="theme">The look of the window.</param>
	internal void Attach(PlacementTarget host, SampleTheme theme)
	{
		_presenter?.Dispose();
		_presenter = new QuickPickPresenter(host, Controller, theme: ThemeOf(theme), hooks: new QuickPickPresenterHooks { ResolveIcon = IconOf });
	}

	/// <summary>Switches the look of the palette window.</summary>
	internal void ApplyTheme(SampleTheme theme)
	{
		if (_presenter is not null)
			_presenter.Theme = ThemeOf(theme);
	}

	public void Dispose()
	{
		// The file list stops first, so its background thread raises no more refreshes of a controller that is going away.
		_files.Dispose();
		_presenter?.Dispose();
		Controller.Dispose();
	}

	// The file opens in a tab like one chosen in the explorer, and a line and column typed after its name place the caret, clamped to the file.
	private static async Task OpenFileAsync(SampleWorkbench workbench, WorkspaceFileOpenRequest request)
	{
		await workbench.OpenFromExplorerAsync(request.Path);

		if (
			request.Line is { } line
			&& workbench.Panes.FirstOrDefault(pane => SampleDocuments.PathComparer.Equals(pane.FilePath, request.Path)) is { } opened
		)
		{
			workbench.ActivePane = opened;
			new TextEditorNavigationTarget(opened.Editor).MoveCaret(line, request.Column ?? 1);
		}
	}

	// A tab whose language answers no outline, such as a settings file, has no symbols to list.
	private static SymbolDocumentDescriptor? DescribeActiveDocument(SampleWorkbench workbench)
	{
		return workbench.ActivePane is { Session.Features.DocumentSymbols: { } provider } pane
			? new SymbolDocumentDescriptor(
				provider,
				documentId: pane.FilePath,
				documentText: pane.Editor.Text,
				target: new TextEditorNavigationTarget(pane.Editor)
			)
			: null;
	}

	// The symbol opens in a tab like a file chosen in the explorer, with the caret at the start of its name as VS Code places it.
	internal static async Task OpenSymbolAsync(SampleWorkbench workbench, WorkspaceSymbol symbol)
	{
		string path = symbol.Location.DocumentId;

		await workbench.OpenFromExplorerAsync(path);

		if (
			symbol.Location.Range is { } range
			&& workbench.Panes.FirstOrDefault(pane => SampleDocuments.PathComparer.Equals(pane.FilePath, path)) is { } opened
		)
		{
			workbench.ActivePane = opened;
			new TextEditorNavigationTarget(opened.Editor).MoveCaret(range.Start.Line + 1, range.Start.Character + 1);
		}
	}

	private static IReadOnlyList<OpenEditorDescriptor> ListOpenEditors(SampleWorkbench workbench) =>
		[.. workbench.Panes.Select(pane => new OpenEditorDescriptor(pane.Title, pane.FilePath, pane.View.IsDirty) { Tag = pane })];

	private static void ActivateEditor(SampleWorkbench workbench, OpenEditorDescriptor editor)
	{
		if (editor.Tag is SamplePane pane && workbench.Panes.Contains(pane))
			workbench.ActivePane = pane;
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "The quick pick contained a failure.")]
	private static partial void LogContainedFailure(ILogger logger, Exception exception);

	private static QuickPickTheme ThemeOf(SampleTheme theme)
	{
		return new()
		{
			Background = theme.Surface.ToBrush(),
			Foreground = theme.Foreground.ToBrush(),
			BorderBrush = theme.Border.ToBrush(),
			ActiveBackground = theme.Selection.ToBrush(),
			ActiveForeground = theme.Foreground.ToBrush(),
		};
	}

	// The kinds of one group share a color, so the symbols of a type, of a callable, of a value and of a container read apart at a glance.
	private static Dictionary<string, ArgbColor> CreateSymbolColors()
	{
		var colors = new Dictionary<string, ArgbColor>();

		foreach (DocumentSymbolKind kind in Enum.GetValues<DocumentSymbolKind>())
		{
			colors[kind.ToIconKey()] = kind switch
			{
				DocumentSymbolKind.Class
				or DocumentSymbolKind.Struct
				or DocumentSymbolKind.Interface
				or DocumentSymbolKind.Enum
				or DocumentSymbolKind.TypeParameter => ArgbColor.FromRgb(0xE6, 0x7E, 0x22),

				DocumentSymbolKind.Method
				or DocumentSymbolKind.Function
				or DocumentSymbolKind.Constructor
				or DocumentSymbolKind.Event
				or DocumentSymbolKind.Operator => ArgbColor.FromRgb(0x8E, 0x44, 0xAD),

				DocumentSymbolKind.Field
				or DocumentSymbolKind.Property
				or DocumentSymbolKind.Variable
				or DocumentSymbolKind.Constant
				or DocumentSymbolKind.EnumMember => ArgbColor.FromRgb(0x2E, 0x86, 0xDE),

				DocumentSymbolKind.File or DocumentSymbolKind.Module or DocumentSymbolKind.Namespace or DocumentSymbolKind.Package =>
					ArgbColor.FromRgb(0x27, 0xAE, 0x60),

				_ => ArgbColor.FromRgb(0x16, 0xA0, 0x85),
			};
		}

		return colors;
	}

	// A category keeps one color for the session, so the dots of one menu match, and the dot of an unsaved document is the same orange everywhere.
	private IconImage IconOf(string category)
	{
		if (!_icons.TryGetValue(category, out IconImage? icon))
		{
			ArgbColor color =
				category == OpenEditorDescriptor.UnsavedIconKey ? ArgbColor.FromRgb(0xE6, 0x7E, 0x22)
				: s_symbolColors.TryGetValue(category, out ArgbColor symbolColor) ? symbolColor
				: s_categoryColors[category.Sum(character => character) % s_categoryColors.Length];

#if AVALONIAEDIT
			icon = new DrawingImage(
				new GeometryDrawing { Brush = color.ToBrush(), Geometry = new EllipseGeometry(new Rect(x: 2.0, y: 2.0, width: 12.0, height: 12.0)) }
			);
#else
			var dot = new DrawingImage(new GeometryDrawing(color.ToBrush(), null, new EllipseGeometry(new Point(8.0, 8.0), 6.0, 6.0)));

			dot.Freeze();
			icon = dot;
#endif

			_icons[category] = icon;
		}

		return icon;
	}
}
