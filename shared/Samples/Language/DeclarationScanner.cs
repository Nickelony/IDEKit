using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.Samples;

/// <summary>
/// Finds the functions, parameters and local variables a text declares, with regular expressions, which is all the sample language needs.
/// </summary>
internal static partial class DeclarationScanner
{
	/// <summary>Finds the declared functions with their parameters, in text order.</summary>
	internal static IReadOnlyList<FunctionDeclaration> FindFunctions(string text)
	{
		List<FunctionDeclaration> functions = [];

		foreach (Match match in FunctionPattern().Matches(StripCommentsAndStrings(text)))
		{
			Group name = match.Groups["name"];
			Group parameters = match.Groups["parameters"];

			List<VariableDeclaration> declared =
			[
				.. NamePattern()
					.Matches(parameters.Value)
					.Select(parameter => new VariableDeclaration(
						parameter.Value,
						new TextRange(parameters.Index + parameter.Index, parameter.Length)
					)),
			];

			functions.Add(
				new FunctionDeclaration(
					Name: name.Value,
					HeaderRange: new TextRange(match.Index, match.Length),
					NameRange: new TextRange(name.Index, name.Length),
					Parameters: declared
				)
			);
		}

		return functions;
	}

	/// <summary>Finds the variables that a <c>local</c> statement or a numeric <c>for</c> loop declares, in text order.</summary>
	internal static IReadOnlyList<VariableDeclaration> FindVariables(string text)
	{
		return
		[
			.. VariablePattern()
				.Matches(StripCommentsAndStrings(text))
				// An underscore names a value the code discards, so it declares nothing worth listing.
				.Where(match => match.Groups["name"].Value != "_")
				.Select(match => new VariableDeclaration(
					match.Groups["name"].Value,
					new TextRange(match.Groups["name"].Index, match.Groups["name"].Length)
				)),
		];
	}

	/// <summary>Finds the first declaration of a name: a function, a parameter or a variable.</summary>
	/// <returns>The range of the declared name, or <see langword="null"/> when the text does not declare it.</returns>
	internal static TextRange? FindDeclaration(string text, string name)
	{
		IEnumerable<VariableDeclaration> declarations = FindFunctions(text)
			.SelectMany(function => function.Parameters.Prepend(new VariableDeclaration(function.Name, function.NameRange)))
			.Concat(FindVariables(text));

		return declarations
			.Where(declaration => string.Equals(declaration.Name, name, StringComparison.Ordinal))
			.OrderBy(declaration => declaration.NameRange.Offset)
			.Select(declaration => (TextRange?)declaration.NameRange)
			.FirstOrDefault();
	}

	/// <summary>Blanks the comments and string literals of a text, so a pattern that runs over it finds code only.</summary>
	/// <remarks>
	/// The blanks keep every offset, so a match in the stripped text addresses the same range of the original.
	/// </remarks>
	internal static string StripCommentsAndStrings(string text) => CommentOrStringPattern().Replace(text, match => new string(' ', match.Length));

	[GeneratedRegex(@"--[^\n]*|""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*'", RegexOptions.CultureInvariant)]
	private static partial Regex CommentOrStringPattern();

	[GeneratedRegex(@"\bfunction\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*\((?<parameters>[^)]*)\)", RegexOptions.CultureInvariant)]
	private static partial Regex FunctionPattern();

	[GeneratedRegex(@"\b(?:local(?!\s+function\b)|for)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
	private static partial Regex VariablePattern();

	[GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant)]
	private static partial Regex NamePattern();
}
