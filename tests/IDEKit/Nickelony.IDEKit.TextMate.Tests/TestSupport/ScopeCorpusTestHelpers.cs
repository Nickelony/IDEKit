namespace Nickelony.IDEKit.TextMate.Tests;

/// <summary>Builds the themes and the scope stacks that compare the resolver's rule lookup with a full scan of the rules.</summary>
internal static class ScopeCorpusTestHelpers
{
	/// <summary>A theme shaped like VS Code's Dark+: comma-separated scope lists, bare rules at several depths, and parent-scoped rules.</summary>
	/// <returns>The theme.</returns>
	internal static TextMateTokenTheme CreateDarkPlusLikeTheme()
	{
		return new TextMateTokenTheme
		{
			Rules =
			[
				Rule(string.Empty, "#D4D4D4"),
				Rule("meta.embedded, source.groovy.embedded, string meta.image.inline.markdown, variable.legacy.builtin.python", "#D4D4D4"),
				Rule("emphasis", "#D4D4D4", "italic"),
				Rule("strong", "#D4D4D4", "bold"),
				Rule("header", "#000080"),
				Rule("comment", "#6A9955"),
				Rule("constant.language", "#569CD6"),
				Rule("constant.numeric, variable.other.enummember, keyword.operator.plus.exponent, keyword.operator.minus.exponent", "#B5CEA8"),
				Rule("constant.regexp", "#646695"),
				Rule("entity.name.tag", "#569CD6"),
				Rule("entity.name.tag.css, entity.name.tag.less", "#D7BA7D"),
				Rule("entity.other.attribute-name", "#9CDCFE"),

				Rule(
					"entity.other.attribute-name.class.css, source.css entity.other.attribute-name.class, entity.other.attribute-name.id.css",
					"#D7BA7D"
				),

				Rule("invalid", "#F44747"),
				Rule("markup.underline", string.Empty, "underline"),
				Rule("markup.bold", "#569CD6", "bold"),
				Rule("markup.heading", "#569CD6", "bold"),
				Rule("markup.italic", string.Empty, "italic"),
				Rule("markup.strikethrough", string.Empty, "strikethrough"),
				Rule("markup.inserted", "#B5CEA8"),
				Rule("markup.deleted", "#CE9178"),
				Rule("markup.changed", "#569CD6"),
				Rule("punctuation.definition.quote.begin.markdown", "#6A9955"),
				Rule("punctuation.definition.list.begin.markdown", "#6796E6"),
				Rule("markup.inline.raw", "#CE9178"),
				Rule("punctuation.definition.tag", "#808080"),
				Rule("meta.preprocessor, entity.name.function.preprocessor", "#569CD6"),
				Rule("meta.preprocessor.string", "#CE9178"),
				Rule("meta.preprocessor.numeric", "#B5CEA8"),
				Rule("meta.structure.dictionary.key.python", "#9CDCFE"),
				Rule("meta.diff.header", "#569CD6"),
				Rule("storage", "#569CD6"),
				Rule("storage.type", "#569CD6"),
				Rule("storage.modifier, keyword.operator.noexcept", "#569CD6"),
				Rule("string, meta.embedded.assembly", "#CE9178"),
				Rule("string.tag", "#CE9178"),
				Rule("string.value", "#CE9178"),
				Rule("string.regexp", "#D16969"),

				Rule(
					"punctuation.definition.template-expression.begin, punctuation.definition.template-expression.end, punctuation.section.embedded",
					"#569CD6"
				),

				Rule("meta.template.expression", "#D4D4D4"),

				Rule(
					"support.type.vendored.property-name, support.type.property-name, source.css variable, variable.other.property.js, meta.object-literal.key",
					"#9CDCFE"
				),

				Rule("keyword", "#569CD6"),
				Rule("keyword.control", "#C586C0"),
				Rule("keyword.operator", "#D4D4D4"),

				Rule(
					"keyword.operator.new, keyword.operator.expression, keyword.operator.cast, keyword.operator.sizeof, keyword.operator.instanceof",
					"#569CD6"
				),

				Rule("keyword.other.unit", "#B5CEA8"),
				Rule("variable.language", "#569CD6"),

				Rule(
					"entity.name.function, support.function, source.powershell variable.other.member, entity.name.operator.custom-literal",
					"#DCDCAA"
				),

				Rule(
					"support.class, support.type, entity.name.type, entity.name.namespace, entity.other.attribute, entity.name.class, storage.type.cs, storage.type.java",
					"#4EC9B0"
				),

				Rule("meta.type.cast.expr, meta.type.new.expr, support.constant.math, entity.other.inherited-class", "#4EC9B0"),

				Rule(
					"keyword.control, source.cpp keyword.operator.new, keyword.operator.delete, keyword.other.using, entity.name.operator",
					"#C586C0"
				),

				Rule("variable, meta.definition.variable.name, support.variable, entity.name.variable, constant.other.placeholder", "#9CDCFE"),
				Rule("variable.other.constant, variable.other.enummember", "#4FC1FF"),
				Rule("meta.object-literal.key", "#9CDCFE"),

				Rule(
					"support.constant.property-value, support.constant.font-name, constant.other.color.rgb-value, support.constant.color",
					"#CE9178"
				),

				Rule("punctuation.definition.group.regexp, punctuation.character.set.begin.regexp, punctuation.character.set.end.regexp", "#CE9178"),
				Rule("constant.character.character-class.regexp, constant.other.character-class.set.regexp", "#D16969"),
				Rule("keyword.operator.or.regexp, keyword.control.anchor.regexp", "#DCDCAA"),
				Rule("keyword.operator.quantifier.regexp", "#D7BA7D"),
				Rule("constant.character, constant.other.option", "#569CD6"),
				Rule("constant.character.escape", "#D7BA7D"),
				Rule("entity.name.label", "#C8C8C8"),
				Rule("source.json meta.structure.dictionary.json support.type.property-name.json", "#9CDCFE"),
				Rule("source.json meta.structure.dictionary.value.json string.quoted.double.json", "#CE9178"),
				Rule("source.lua entity.name.function.lua", "#DCDCAA"),
				Rule("source.cs meta.method-call entity.name.function", "#DCDCAA"),
				Rule("text.html.markdown meta.paragraph meta.link.inline", "#3794FF"),
			],
		};
	}

	/// <summary>
	/// A theme of selectors drawn at random from a small vocabulary, so many rules share a rightmost scope or differ only in their parents,
	/// tie on specificity, merge as duplicates, use the child combinator, or set only some properties, with a defaults rule among them.
	/// </summary>
	/// <param name="seed">The seed that fixes the theme.</param>
	/// <returns>The theme.</returns>
	internal static TextMateTokenTheme CreateCollidingSelectorsTheme(int seed)
	{
		var random = new Random(seed);
		var rules = new List<TextMateTokenThemeRule> { Rule(string.Empty, "#101010", "italic") };

		for (int index = 0; index < 90; index++)
		{
			int partCount = random.Next(1, 4);
			var selector = new List<string>();

			for (int part = 0; part < partCount; part++)
			{
				if (part > 0 && random.Next(5) == 0)
					selector.Add(">");

				selector.Add(CollidingVocabulary[random.Next(CollidingVocabulary.Length)]);
			}

			string scope = string.Join(' ', selector);

			// A leading or doubled child combinator is dropped by the rule set, so it is fine for the generator to produce one.
			string? foreground = random.Next(4) == 0 ? null : $"#{random.Next(0x1000000):X6}";

			string? fontStyle = random.Next(3) switch
			{
				0 => null,
				1 => "bold",
				_ => random.Next(2) == 0 ? "italic" : string.Empty,
			};

			rules.Add(
				Rule(
					index % 7 == 0 ? scope + ", " + CollidingVocabulary[random.Next(CollidingVocabulary.Length)] : scope,
					foreground ?? string.Empty,
					fontStyle
				)
			);
		}

		return new TextMateTokenTheme { Rules = rules };
	}

	/// <summary>
	/// Builds scope stacks that exercise a theme's selectors: the parts of each selector stacked in order and extended with a suffix,
	/// cut short inside a segment, buried among unrelated scopes, and drawn at random.
	/// </summary>
	/// <param name="theme">The theme whose selectors the stacks are built from.</param>
	/// <param name="seed">The seed that fixes the random stacks.</param>
	/// <param name="randomStackCount">The number of random stacks to add.</param>
	/// <returns>The stacks, each ordered from the root scope to the innermost scope.</returns>
	internal static IReadOnlyList<string[]> CreateScopeStacks(TextMateTokenTheme theme, int seed, int randomStackCount)
	{
		var random = new Random(seed);
		var stacks = new List<string[]>();
		var parts = new List<string>();

		foreach (TextMateTokenThemeRule rule in theme.Rules)
		{
			foreach (string selector in (rule.Scope ?? string.Empty).Split(','))
			{
				string[] selectorParts = [.. selector.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(part => part != ">")];

				foreach (string part in selectorParts)
				{
					if (!parts.Contains(part))
						parts.Add(part);
				}

				if (selectorParts.Length == 0)
					continue;

				stacks.Add(selectorParts);
				stacks.Add([.. selectorParts.SkipLast(1), selectorParts[^1] + ".extended"]);
				stacks.Add([.. selectorParts.SkipLast(1), selectorParts[^1] + ".extended.further"]);
				stacks.Add([.. selectorParts.SkipLast(1), selectorParts[^1] + "x"]);
				stacks.Add([.. selectorParts.SkipLast(1), selectorParts[^1][..Math.Max(1, selectorParts[^1].Length - 2)]]);
				stacks.Add(["unrelated.root", .. selectorParts, "unrelated.leaf"]);
				stacks.Add([.. selectorParts.SelectMany(part => new[] { part, "unrelated.between" })]);
			}
		}

		for (int index = 0; index < randomStackCount; index++)
		{
			int depth = random.Next(1, 7);
			string[] stack = new string[depth];

			for (int position = 0; position < depth; position++)
			{
				string part = parts[random.Next(parts.Count)];

				stack[position] = random.Next(4) switch
				{
					0 => part + ".lua",
					1 => part + ".a.b",
					_ => part,
				};
			}

			stacks.Add(stack);
		}

		return stacks;
	}

	/// <summary>The vocabulary of <see cref="CreateCollidingSelectorsTheme"/>: few names, nested and unnested, so selectors collide.</summary>
	private static readonly string[] CollidingVocabulary = ["a", "a.b", "a.b.c", "d", "d.e", "x.y", "x", "source.k", "source"];

	private static TextMateTokenThemeRule Rule(string scope, string foreground, string? fontStyle = null)
	{
		return new()
		{
			Scope = scope,
			Foreground = foreground,
			FontStyle = fontStyle,
		};
	}
}
