using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Pins the tooltip accessor's documented graceful-disable behavior on AvaloniaEdit: unlike WPF
/// AvalonEdit's reflected private field, AvaloniaEdit exposes no tooltip member, so the accessor always
/// reports the tooltip as unavailable and the tooltip-aware controller paths degrade instead of throwing.
/// </summary>
[AvaloniaTestClass]
public sealed class CompletionWindowTooltipAccessTests
{
	[TestMethod]
	public void TryGetTooltip_AlwaysReportsTheTooltipAsUnavailable()
	{
		var editor = new TextEditor();
		HostWindow hostWindow = AvaloniaTestHost.ShowInHostWindow(editor);

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);
			completionWindow.Show();

			bool resolved = CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip);

			// Avalonia divergence: WPF AvalonEdit exposes its tooltip in a reflected private field, so the
			// reference test asserted a resolved tooltip and a present field. AvaloniaEdit publishes no
			// tooltip member at all, so the accessor must report the failure that disables tooltip styling.
			Assert.IsFalse(resolved);
			Assert.IsNull(tooltip);
			Assert.IsFalse(CompletionWindowTooltipAccess.IsFieldAvailable);

			completionWindow.Close();
		}
	}
}
