using Microsoft.Extensions.Logging;
using static Nickelony.IDEKit.TextMate.Tests.TextMateThemeTestHelpers;

namespace Nickelony.IDEKit.TextMate.Tests;

[TestClass]
public sealed class TextMateScopeResolverLoggingTests
{
	[TestMethod]
	public void Warnings_RaiseStableEventIdsAndLevels()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(
			CreateTheme(
				null!,
				new TextMateTokenThemeRule { Scope = "keyword", Foreground = "not-a-color" },
				new TextMateTokenThemeRule { Scope = "source.lua - comment", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "string", FontStyle = "sparkle" },
				new TextMateTokenThemeRule { Scope = "keyword > > control", Foreground = "#222222" }
			),

			logger
		);

		(int Id, string Name)[] expected =
		[
			(3004, "NullThemeRule"),
			(3000, "InvalidForegroundColor"),
			(3001, "UnsupportedSelector"),
			(3002, "UnknownFontStyleTrait"),
			(3003, "MisplacedChildCombinator"),
		];

		// The resolver parses its rules eagerly, so the constructor raises the entries in rule order;
		// each warning uses the level, id, and name that its LoggerMessage attribute declares.
		Assert.HasCount(expected.Length, logger.Entries);

		for (int i = 0; i < expected.Length; i++)
		{
			Assert.AreEqual(LogLevel.Warning, logger.Entries[i].Level, $"entry {i} level");
			Assert.AreEqual(expected[i].Id, logger.Entries[i].EventId.Id, $"entry {i} id");
			Assert.AreEqual(expected[i].Name, logger.Entries[i].EventId.Name, $"entry {i} name");
		}

		Assert.IsFalse(resolver.Resolve(["source.lua", "keyword.control.lua"]).HasFormatting);
	}
}
