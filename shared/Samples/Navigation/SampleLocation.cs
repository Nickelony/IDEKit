using Nickelony.IDEKit.Core.Text;

namespace Nickelony.Samples;

/// <summary>One row of a list of the window - an outline entry, a problem or a reference - and the range of a document it navigates to.</summary>
/// <param name="Text">The text of the row.</param>
/// <param name="DocumentId">The identity of the document the range lies in: its local file path.</param>
/// <param name="Range">The range that navigating to the row selects.</param>
internal sealed record SampleLocation(string Text, string DocumentId, TextRange Range)
{
	/// <summary>The text of the row, which a list control shows for an item without a template.</summary>
	public override string ToString() => Text;
}
