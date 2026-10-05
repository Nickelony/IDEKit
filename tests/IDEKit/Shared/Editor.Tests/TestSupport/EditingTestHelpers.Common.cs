using Nickelony.IDEKit.Core.Formatting;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>Provides the input-event builders and the formatter double shared by the editor editing test classes.</summary>
internal static partial class EditingTestHelpers
{
	/// <summary>Supplies a formatter that returns its input unchanged, for tests that only need a reachable formatter.</summary>
	public sealed class IdentityFormatter : IDocumentFormatter
	{
		/// <inheritdoc/>
		public string FormatDocument(string content) => content;
	}
}
