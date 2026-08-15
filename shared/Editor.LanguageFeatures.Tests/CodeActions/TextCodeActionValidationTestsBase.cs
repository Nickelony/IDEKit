#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using System.Windows.Media;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// The <see cref="TextCodeActionController"/> and <see cref="TextCodeActionMenuSkin"/> validation scenarios
/// whose shape is the same across the editor bindings: the null-argument and missing-hook constructor
/// guards, the off-owner-thread guard, and the skin's null-brush guards.
/// </summary>
/// <remarks>
/// A binding supplies the worker-thread configuration through the hook below: WPF needs the COM
/// single-threaded apartment for its dispatcher, while Avalonia does not. The worker itself never touches
/// the editor fixture - the text area is captured on the owning thread first - so the only per-binding
/// difference is that setup.
/// </remarks>
public abstract class TextCodeActionValidationTestsBase
{
	/// <summary>
	/// Configures the worker thread used by the off-owner-thread guard scenario.
	/// </summary>
	/// <param name="thread">The worker thread, before it is started.</param>
	protected abstract void ConfigureWorkerThread(Thread thread);

	[TestMethod]
	public void Constructor_NullArguments_Throw()
	{
		var editor = TestHost.CreateEditor("sample");
		TextCodeActionPresentation presentation = CodeActionTestHost.CreatePresentation();

		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(null!, presentation, CreateHooks()));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(editor.TextArea, null!, CreateHooks()));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(editor.TextArea, presentation, hooks: null!));
	}

	[TestMethod]
	public void Constructor_MissingRequiredHooks_Throw()
	{
		var editor = TestHost.CreateEditor("sample");
		TextCodeActionPresentation presentation = CodeActionTestHost.CreatePresentation();

		var missingStateBuilder = new TextCodeActionControllerHooks
		{
			BuildRequest = null!,
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]),
			ExecuteActionAsync = static _ => Task.CompletedTask
		};

		var missingRequest = new TextCodeActionControllerHooks
		{
			BuildRequest = static _ => null,
			RequestCodeActionsAsync = null!,
			ExecuteActionAsync = static _ => Task.CompletedTask
		};

		var missingExecute = new TextCodeActionControllerHooks
		{
			BuildRequest = static _ => null,
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]),
			ExecuteActionAsync = null!
		};

		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(editor.TextArea, presentation, hooks: missingStateBuilder));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(editor.TextArea, presentation, hooks: missingRequest));
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionController(editor.TextArea, presentation, hooks: missingExecute));
	}

	[TestMethod]
	public void Constructor_CreatedOffOwnerThread_Throws()
	{
		var editor = TestHost.CreateEditor("sample");
		TextCodeActionPresentation presentation = CodeActionTestHost.CreatePresentation();

		// The text area is captured on the owning (UI) thread before the worker is started, so the worker
		// only exercises the controller's own thread-affinity check instead of touching the editor fixture.
		TextArea textArea = editor.TextArea;
		Exception? caught = null;

		var thread = new Thread(() =>
		{
			try
			{
				using var controller = new TextCodeActionController(
					textArea,
					presentation,
					hooks: new TextCodeActionControllerHooks
					{
						BuildRequest = static context => new TextCodeActionRequest(context.DocumentText, 0, 0),
						RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]),
						ExecuteActionAsync = static _ => Task.CompletedTask
					});
			}
			catch (Exception exception)
			{
				caught = exception;
			}
		});

		ConfigureWorkerThread(thread);
		thread.Start();
		thread.Join();

		Assert.IsInstanceOfType<InvalidOperationException>(caught);
	}

	[TestMethod]
	public void Skin_NullBrushes_ThrowAtAssignment()
	{
		// The skin self-validates, so the failure site is the offending object initializer instead of the
		// controller constructor (matching the options records).
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionMenuSkin
		{
			BorderBrush = null!,
			Background = Brushes.Black,
			Foreground = Brushes.White
		});
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionMenuSkin
		{
			BorderBrush = Brushes.Gray,
			Background = null!,
			Foreground = Brushes.White
		});
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionMenuSkin
		{
			BorderBrush = Brushes.Gray,
			Background = Brushes.Black,
			Foreground = null!
		});
	}

	private static TextCodeActionControllerHooks CreateHooks() => new()
	{
		BuildRequest = static _ => null,
		RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]),
		ExecuteActionAsync = static _ => Task.CompletedTask
	};
}
