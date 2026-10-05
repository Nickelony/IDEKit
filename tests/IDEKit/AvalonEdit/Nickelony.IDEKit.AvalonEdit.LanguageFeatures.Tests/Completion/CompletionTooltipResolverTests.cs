using System.Windows.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class CompletionTooltipResolverTests
{
	[TestMethod]
	public void TryGetTooltip_ReturnsCompletionWindowTooltip()
	{
		var editor = new ICSharpCode.AvalonEdit.TextEditor();
		HostWindow hostWindow = WpfTestHost.ShowInHostWindow(editor);

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);
			completionWindow.Show();

			bool resolved = CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip);

			Assert.IsTrue(resolved);
			Assert.IsNotNull(tooltip);

			// The reflected AvalonEdit field is what the controller depends on; losing it must fail this
			// test when the AvalonEdit version is upgraded.
			Assert.IsTrue(CompletionTooltipResolver.IsTooltipAvailable);

			completionWindow.Close();
		}
	}
}
