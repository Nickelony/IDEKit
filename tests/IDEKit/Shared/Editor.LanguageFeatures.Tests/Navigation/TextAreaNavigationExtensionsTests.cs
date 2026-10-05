#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed partial class TextAreaNavigationExtensionsTests
{
	private const string DocumentText = "sample alpha = 1\nsample bravo = 2\nsample charlie = 3";
	private const string DocumentId = "navigation-test.txt";

	[TestMethod]
	public async Task DocumentlessTextArea_AllAsyncEntryPoints_ReturnFalseWithoutFailing()
	{
		var textArea = new TextArea();

		Assert.IsFalse(
			await textArea.GoToDefinitionByHoverAsync(
				DocumentId,
				definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(LocationAt(1)),
				hoverResolverAsync: (_, _) => Task.FromResult<HoverInformation?>(CreateHoverInfo()),
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.IsFalse(
			await textArea.GoToDefinitionAsync(
				DocumentId,
				provider: new StubDefinitionProvider(LocationAt(1)),
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.IsFalse(
			await textArea.GoToDefinitionBySymbolAsync(
				DocumentId,
				definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(LocationAt(1)),
				symbolName: "alpha",
				cancellationToken: TestContext.CancellationToken
			)
		);
	}

	private sealed class StubDefinitionProvider(DefinitionLocation? location) : IDefinitionProvider
	{
		public DefinitionRequest? LastRequest { get; private set; }

		public Task<DefinitionLocation?> GetDefinitionAsync(DefinitionRequest request, CancellationToken cancellationToken = default)
		{
			LastRequest = request;
			return Task.FromResult(location);
		}
	}

	private sealed class ScriptedDefinitionProvider(Func<DefinitionRequest, CancellationToken, Task<DefinitionLocation?>> answer)
		: IDefinitionProvider
	{
		public Task<DefinitionLocation?> GetDefinitionAsync(DefinitionRequest request, CancellationToken cancellationToken = default) =>
			answer(request, cancellationToken);
	}

	private sealed class StubHoverProvider(HoverInformation? hoverInfo) : IHoverProvider
	{
		public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(hoverInfo);
	}

	private sealed record TestDiscriminator(string Value) : DefinitionDiscriminator;

	private static DefinitionLocation LocationAt(int oneBasedLine, int oneBasedColumn = 1, string? documentId = null)
	{
		return new(
			new TextPositionRange(new TextPosition(oneBasedLine - 1, oneBasedColumn - 1), new TextPosition(oneBasedLine - 1, oneBasedColumn - 1)),
			documentId
		);
	}

	private static HoverInformation CreateHoverInfo() => new("Function sample(value);") { SymbolName = "charlie" };

	private static (TextEditor Editor, HostWindow HostWindow) CreateHostedEditor() => TestHost.ShowHostedEditor(DocumentText);

	private Task<bool> GoToDefinitionByHoverAsync(
		TextArea textArea,
		StubDefinitionProvider definitionProvider,
		StubHoverProvider hoverProvider,
		int offset
	)
	{
		return textArea.GoToDefinitionByHoverAsync(
			DocumentId,
			definitionResolverAsync: definitionProvider.GetDefinitionAsync,
			hoverResolverAsync: hoverProvider.GetHoverAsync,
			offset,
			cancellationToken: TestContext.CancellationToken
		);
	}

	private Task<bool> GoToDefinitionBySymbolAsync(
		TextArea textArea,
		StubDefinitionProvider definitionProvider,
		string? symbolName,
		DefinitionDiscriminator? discriminator = null
	)
	{
		return textArea.GoToDefinitionBySymbolAsync(
			DocumentId,
			definitionResolverAsync: definitionProvider.GetDefinitionAsync,
			symbolName,
			discriminator,
			cancellationToken: TestContext.CancellationToken
		);
	}

	public TestContext TestContext { get; set; } = null!;
}
