using Nickelony.IDEKit.Core.Indentation;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.Samples;

/// <summary>Indents a line like the line above it.</summary>
internal sealed class PreviousLineIndentationPolicy : IIndentationPolicy
{
	public string GetDesiredIndentation(in IndentationRequest request)
	{
		string previous = request.PreviousLineText;
		return previous[..LineTextOperations.GetLeadingWhitespaceLength(previous)];
	}
}
