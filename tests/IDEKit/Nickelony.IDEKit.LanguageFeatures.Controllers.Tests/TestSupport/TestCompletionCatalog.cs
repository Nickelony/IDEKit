using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Provides the three-item completion catalog shared by the kernel and filter tests.</summary>
internal static class TestCompletionCatalog
{
	/// <summary>
	/// Three items in a fixed order whose insert texts extend their labels, so filtering and replacement ranges stay distinguishable.
	/// </summary>
	internal static IReadOnlyList<CompletionItem> Items { get; } =
	[
		new CompletionItem("Alpha") { InsertText = "Alpha" },
		new CompletionItem("Beta") { InsertText = "Beta: " },
		new CompletionItem("Gamma") { InsertText = "Gamma" },
	];
}
