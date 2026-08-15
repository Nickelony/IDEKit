#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed partial class TextAreaDefinitionNavigationTests
{
	private const string DocumentText = "sample alpha = 1\nsample bravo = 2\nsample charlie = 3";

	[TestMethod]
	public void DocumentlessTextArea_AllEntryPoints_ReturnFalseWithoutFailing()
	{
		var textArea = new TextArea();

		// A text area whose document was cleared (or never assigned) reports "not navigated" instead of
		// failing on the missing document.
		Assert.IsFalse(textArea.TryGoToDefinition(
			new TestDefinitionProvider(LocationAt(1)),
			new TestHoverProvider(CreateHoverInfo()),
			0));
		Assert.IsFalse(textArea.TryGoToDefinitionAtOffset((_, _) => LocationAt(1), 0));
		Assert.IsFalse(textArea.TryGoToDefinitionBySymbol(new TestDefinitionProvider(LocationAt(1)), "alpha"));
	}

	[TestMethod]
	public async Task DocumentlessTextArea_AllAsyncEntryPoints_ReturnFalseWithoutFailing()
	{
		var textArea = new TextArea();

		Assert.IsFalse(await textArea.TryGoToDefinitionAsync(
			(_, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(1)),
			(_, _) => Task.FromResult<TextHoverInfo?>(CreateHoverInfo()),
			0));
		Assert.IsFalse(await textArea.TryGoToDefinitionAtOffsetAsync(
			(_, _, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(1)),
			0));
		Assert.IsFalse(await textArea.TryGoToDefinitionBySymbolAsync(
			(_, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(1)),
			"alpha"));
	}

	private sealed class TestDefinitionProvider(TextDefinitionLocation? location) : ITextDefinitionProvider
	{
		public TextDefinitionRequest? LastRequest { get; private set; }

		public TextDefinitionLocation? GetDefinition(TextDefinitionRequest request)
		{
			LastRequest = request;
			return location;
		}
	}

	private sealed class TestHoverProvider(TextHoverInfo? hoverInfo) : ITextHoverProvider
	{
		public TextHoverInfo? GetHoverInfo(TextHoverRequest request) => hoverInfo;
	}

	private sealed record TestDiscriminator(string Value) : TextDefinitionDiscriminator;

	private static TextDefinitionLocation LocationAt(int oneBasedLine, int oneBasedColumn = 1, string? documentId = null)
		=> new(
			new TextPositionRange(
				new TextPosition(oneBasedLine - 1, oneBasedColumn - 1),
				new TextPosition(oneBasedLine - 1, oneBasedColumn - 1)),
			documentId);

	private static TextHoverInfo CreateHoverInfo()
		=> new("Function sample(value);") { SymbolName = "charlie" };

	private static (TextEditor Editor, HostWindow HostWindow) CreateHostedEditor()
		=> TestHost.ShowHostedEditor(DocumentText);
}
