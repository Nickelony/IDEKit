namespace Nickelony.Samples;

/// <summary>The keywords and library functions of the Lua-like language that the in-process sample providers understand.</summary>
internal static class SampleKeywords
{
	internal static IReadOnlyList<string> Keywords { get; } =
	[
		"and",
		"break",
		"do",
		"else",
		"elseif",
		"end",
		"false",
		"for",
		"function",
		"if",
		"in",
		"local",
		"nil",
		"not",
		"or",
		"repeat",
		"return",
		"then",
		"true",
		"until",
		"while",
	];

	/// <summary>The library functions whose signature help the sample offers, keyed by name.</summary>
	internal static IReadOnlyDictionary<string, SampleFunction> Functions { get; } =
		new Dictionary<string, SampleFunction>(StringComparer.Ordinal)
		{
			["print"] = new("print", "Prints its arguments to standard output.", ["...: the values to print"]),
			["tostring"] = new("tostring", "Converts a value to a string.", ["value: the value to convert"]),

			["string.format"] = new(
				"string.format",
				"Formats its arguments by a format string.",
				["format: the format string", "...: the values to insert"]
			),

			["math.max"] = new("math.max", "Returns the largest of its arguments.", ["x: the first number", "...: the other numbers"]),
			["string.upper"] = new("string.upper", "Returns a copy of a string in upper case.", ["text: the string to convert"]),

			["string.gmatch"] = new(
				"string.gmatch",
				"Returns an iterator over the matches of a pattern in a string.",
				["text: the string to search", "pattern: the pattern to match"]
			),

			["table.insert"] = new("table.insert", "Appends a value to a list.", ["list: the list to append to", "value: the value to append"]),
			["ipairs"] = new("ipairs", "Returns an iterator over the index and value of each item of a list.", ["list: the list to walk"]),

			["require"] = new(
				"require",
				"Loads a module by name, once, and returns what the module returns.",
				["name: the module name, with a dot between folders"]
			),
		};

	/// <summary>The Markdown documentation of a keyword or function, or <see langword="null"/> for any other word.</summary>
	internal static string? Describe(string word)
	{
		if (Functions.TryGetValue(word, out SampleFunction? function))
			return $"{SampleMarkdown.Fence($"function {function.Name}({string.Join(", ", function.Parameters.Select(parameter => parameter.Split(':')[0]))})", "lua")}\n\n{function.Summary}";

		return Keywords.Contains(word, StringComparer.Ordinal) ? $"`{word}` is a keyword of the sample language." : null;
	}
}
