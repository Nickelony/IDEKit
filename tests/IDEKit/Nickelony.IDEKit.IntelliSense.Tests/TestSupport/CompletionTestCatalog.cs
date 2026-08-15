using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.TestSupport;

/// <summary>
/// Provides the three-item completion catalog shared by the kernel and filter tests.
/// </summary>
internal static class CompletionTestCatalog
{
	/// <summary>
	/// Gets three items in a fixed order whose insert texts extend their labels, so filtering and
	/// replacement ranges stay distinguishable.
	/// </summary>
	internal static IReadOnlyList<TextCompletionItem> Items { get; } =
	[
		new TextCompletionItem("Alpha") { InsertText = "Alpha" },
		new TextCompletionItem("Beta") { InsertText = "Beta: " },
		new TextCompletionItem("Gamma") { InsertText = "Gamma" }
	];
}
