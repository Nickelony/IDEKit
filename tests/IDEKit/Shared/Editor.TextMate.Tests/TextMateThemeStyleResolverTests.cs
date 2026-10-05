using System.Collections.Concurrent;
#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#else
using System.Windows;
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

#endif

/// <summary>
/// Pins the conversion of the neutral resolution result into the toolkit's brush and text decorations; the selector, ranking, and value rules
/// are pinned by the neutral scope resolver suite.
/// </summary>
[TestClass]
public sealed class TextMateThemeStyleResolverTests
{
	[TestMethod]
	public void Resolve_Foreground_BecomesTheBrushOfTheRuleColor()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#11223380" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(Color.FromArgb(a: 0x80, r: 0x11, g: 0x22, b: 0x33), GetBrushColor(style.Foreground!));
	}

	[TestMethod]
	public void Resolve_NoForeground_LeavesTheBrushUnset()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsNull(style.Foreground);
		Assert.IsTrue(style.IsBold);
	}

	[TestMethod]
	public void Resolve_BoldAndItalic_MapToTheRunStyleFlags()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold italic" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsBold);
		Assert.IsTrue(style.IsItalic);
		Assert.IsNull(style.TextDecorations);
	}

	[TestMethod]
	public void Resolve_UnderlineAndStrikethrough_MapToBothTextDecorations()
	{
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "underline strikethrough" })
		);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsNotNull(style.TextDecorations);
		Assert.AreEqual(2, style.TextDecorations.Count);
	}

	[TestMethod]
	public void Resolve_UnderlineOnly_ProducesSingleUnderlineDecoration()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "underline" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsNotNull(style.TextDecorations);
		Assert.AreEqual(1, style.TextDecorations.Count);
		Assert.AreEqual(TextDecorationLocation.Underline, style.TextDecorations[0].Location);
	}

	[TestMethod]
	public void Resolve_StrikethroughOnly_ProducesSingleStrikethroughDecoration()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "strikethrough" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsNotNull(style.TextDecorations);
		Assert.AreEqual(1, style.TextDecorations.Count);
		Assert.AreEqual(TextDecorationLocation.Strikethrough, style.TextDecorations[0].Location);
	}

	[TestMethod]
	public void Resolve_NoneFontStyle_ProducesNoFormatting()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "none" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.AreEqual(TextRunStyle.Empty, style);
	}

	[TestMethod]
	public void Resolve_EmptyScopes_ReturnsSharedEmptyStyle()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme());

		Assert.AreEqual(TextRunStyle.Empty, resolver.Resolve(null));
		Assert.AreEqual(TextRunStyle.Empty, resolver.Resolve([]));
	}

	[TestMethod]
	public void Resolve_RulesWithTheSameStyle_ShareTheBrushAndTheDecorations()
	{
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(
				new TextMateTokenThemeRule
				{
					Scope = "keyword",
					Foreground = "#123456",
					FontStyle = "underline",
				},

				new TextMateTokenThemeRule
				{
					Scope = "storage",
					Foreground = "#123456",
					FontStyle = "underline",
				}
			)
		);

		TextRunStyle keyword = resolver.Resolve(["source.lua", "keyword.control.lua"]);
		TextRunStyle storage = resolver.Resolve(["source.lua", "storage.modifier.lua"]);

		// Two separate rules that resolve to the same color and traits share one converted style.
		Assert.AreSame(keyword.Foreground, storage.Foreground);
		Assert.AreSame(keyword.TextDecorations, storage.TextDecorations);
		Assert.AreEqual(keyword, storage);
	}

	[TestMethod]
	public void Resolve_FromMultipleThreadsOnAColdResolver_SharesOneBrushForOneStyle()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" }));
		var results = new ConcurrentBag<TextRunStyle>();

		Parallel.For(0, 500, _ => results.Add(resolver.Resolve(["source.lua", "keyword.control.lua"])));

		object? first = results.First().Foreground;

		Assert.IsNotNull(first);
		Assert.IsTrue(results.All(style => ReferenceEquals(style.Foreground, first)), "Equal styles must share one brush instance.");
	}
}
