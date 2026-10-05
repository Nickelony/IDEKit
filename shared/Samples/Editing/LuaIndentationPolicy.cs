using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.Indentation;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.Samples;

/// <summary>Indents a line like the line above it, one unit deeper after a block opener and one unit shallower on a block closer.</summary>
internal sealed partial class LuaIndentationPolicy : IIndentationPolicy
{
	public string GetDesiredIndentation(in IndentationRequest request)
	{
		string previous = request.PreviousLineText;
		string indentation = previous[..LineTextOperations.GetLeadingWhitespaceLength(previous)];

		if (!request.UseSmartIndent)
			return indentation;

		if (OpenerPattern().IsMatch(previous))
			indentation += request.IndentationUnit;

		if (CloserPattern().IsMatch(request.CurrentLineText))
			indentation = IndentationOperations.TruncateIndentationByUnitLength(indentation, request.IndentationUnit);

		return indentation;
	}

	// A block opens at a line that ends in then, do, else or repeat, or that declares a function and ends its parameter list.
	[GeneratedRegex(@"(\b(then|do|else|repeat)|\bfunction\b.*\))\s*$", RegexOptions.CultureInvariant)]
	private static partial Regex OpenerPattern();

	[GeneratedRegex(@"^\s*(end|else|elseif|until)\b", RegexOptions.CultureInvariant)]
	private static partial Regex CloserPattern();
}
