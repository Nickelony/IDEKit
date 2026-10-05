using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.Samples;

/// <summary>Answers a hover over a keyword, a library function or a name the document declares with its Markdown documentation.</summary>
/// <remarks>
/// A declared function shows its header and the <c>---</c> comment lines above it, the way the Lua language server shows them.
/// </remarks>
internal sealed class KeywordHoverProvider : IHoverProvider
{
	public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default)
	{
		// A library function is a dotted name, which the identifier policy of Core does not span.
		TextRange? token = IdentifierScanner.FindName(request.DocumentText, request.HoveredOffset);

		if (token is null)
			return Task.FromResult<HoverInformation?>(null);

		string word = token.Value.GetText(request.DocumentText);

		string? documentation =
			SampleKeywords.Describe(word)
			?? DescribeDeclaredFunction(request.DocumentText, word)
			?? DescribeDeclaredVariable(request.DocumentText, word);

		HoverInformation? information = documentation is null
			? null
			: new HoverInformation(new MarkupContent(documentation, MarkupKind.Markdown)) { Range = token, SymbolName = word };

		return Task.FromResult(information);
	}

	private static string? DescribeDeclaredFunction(string text, string name)
	{
		if (DeclarationScanner.FindFunctions(text).FirstOrDefault(function => function.Name == name) is not { } function)
			return null;

		var lineMap = TextLineMap.Build(text);

		int headerLine = lineMap.GetPosition(function.HeaderRange.Offset).Line;
		string header = lineMap.GetLineText(headerLine).Trim();

		// The documentation comment is the run of --- lines right above the header, read upward and shown in text order.
		List<string> comment = [];

		for (int line = headerLine - 1; line >= 0; line--)
		{
			string current = lineMap.GetLineText(line).TrimStart();

			if (!current.StartsWith("---", StringComparison.Ordinal))
				break;

			comment.Insert(0, FormatCommentLine(current[3..].Trim()));
		}

		return comment.Count == 0 ? SampleMarkdown.Fence(header, "lua") : $"{SampleMarkdown.Fence(header, "lua")}\n\n{string.Join("\n\n", comment)}";
	}

	private static string? DescribeDeclaredVariable(string text, string name)
	{
		// A parameter names the function it belongs to, the way the Lua language server labels it.
		if (
			DeclarationScanner.FindFunctions(text).FirstOrDefault(function => function.Parameters.Any(parameter => parameter.Name == name)) is
			{ } owner
		)
		{
			return $"{SampleMarkdown.Fence($"(parameter) {name}", "lua")}\n\nA parameter of `{owner.Name}`.";
		}

		return DeclarationScanner.FindVariables(text).Any(variable => variable.Name == name) ? SampleMarkdown.Fence($"local {name}", "lua") : null;
	}

	private static string FormatCommentLine(string line)
	{
		// An annotation such as @param name string shows its tag in italics and its name as code.
		if (!line.StartsWith('@'))
			return line;

		string[] words = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);

		return words.Length switch
		{
			1 => $"_{words[0]}_",
			2 => $"_{words[0]}_ `{words[1]}`",
			_ => $"_{words[0]}_ `{words[1]}` {words[2]}",
		};
	}
}
