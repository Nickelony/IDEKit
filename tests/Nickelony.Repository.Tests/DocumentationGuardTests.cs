using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>The documentation rules of AGENTS.md that no compiler enforces.</summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed partial class DocumentationGuardTests
{
	private const string SecondPersonName = "second person";

	[GeneratedRegex(@"\bpreviously\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex Previously();

	[GeneratedRegex(@"\bno longer\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex NoLonger();

	[GeneratedRegex(@"\bwe decided\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex WeDecided();

	[GeneratedRegex(@"\b(?:was|were|has been|have been) renamed\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex WasRenamed();

	[GeneratedRegex(@"\blegacy\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex Legacy();

	[GeneratedRegex(@"\bmigrated\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex Migrated();

	/// <summary>Bans a switch verb before on or off, not the keyword noun.</summary>
	[GeneratedRegex(
		@"\bswitch(?:es|ed|ing)\b(?:[\s-]+\S+){0,3}?[\s-]+(?:on|off)\b"
			+ @"|\bswitch(?:es|ed|ing)?\s+(?:on|off)\b"
			+ @"|\bswitch\s+(?:the|this|these|those|it|them|its|their)\b(?:[\s-]+\S+){0,3}?[\s-]+(?:on|off)\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
	)]
	private static partial Regex SwitchOnOrOff();

	/// <summary>Allowed after a comma, a semicolon or an auxiliary verb.</summary>
	[GeneratedRegex(@"(?<![,;]\s)(?<!\b(?:is|are|was|were|be|been|being)\s)\bused to\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex UsedTo();

	[GeneratedRegex(@"\bnote that\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex NoteThat();

	[GeneratedRegex(@"\bkeep in mind\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex KeepInMind();

	/// <summary>Bans "it is worth" plus a noting or saying verb; "worth doing" passes.</summary>
	[GeneratedRegex(@"\bit is worth (?:noting|mentioning|saying|stating)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex ItIsWorth();

	[GeneratedRegex(@"\bin other words\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex InOtherWords();

	/// <summary>A claim that a set is complete, which ages on the next member.</summary>
	[GeneratedRegex(@"\bevery rule\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex EveryRule();

	[GeneratedRegex(@"\ball rules\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex AllRules();

	[GeneratedRegex(@"\bthe following rules\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex TheFollowingRules();

	/// <summary>A census of a set that can grow.</summary>
	[GeneratedRegex(@"\ball three\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex AllThree();

	[GeneratedRegex(@"\bboth (?:readmes?|packages?|products?|providers?|projects?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex BothFamily();

	[GeneratedRegex(
		@"\bthe (?:two|three|four|five) (?:rules?|keys?|options?|packages?|products?|providers?|projects?|states?|tiers?)\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
	)]
	private static partial Regex TheThreeSet();

	[GeneratedRegex(@"\bsimply\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex Simply();

	[GeneratedRegex(@"\bbasically\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex Basically();

	/// <summary>Filler, though a contrast such as "not just X but Y" also matches.</summary>
	[GeneratedRegex(@"\bjust\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex Just();

	[GeneratedRegex(@"\bin order to\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex InOrderTo();

	[GeneratedRegex(@"\bas well as\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex AsWellAs();

	[GeneratedRegex(@"\bthe fact that\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex TheFactThat();

	[GeneratedRegex(@"\bis able to\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex IsAbleTo();

	[GeneratedRegex(@"\bit(?:'s| is) important to note\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex ItIsImportantToNote();

	/// <summary>The reader addressed directly, which a page may do and a comment may not.</summary>
	[GeneratedRegex(@"\b(?:you|your|yours)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex SecondPerson();

	/// <summary>A dash other than the hyphen-minus, which the repository never uses.</summary>
	[GeneratedRegex(@"[\p{Pd}-[\-]]", RegexOptions.CultureInvariant)]
	private static partial Regex TypographicDash();

	[GeneratedRegex(
		@"\b(?:un|re|non|pre|post|dis|mis|over|under|co|de|sub|inter|anti|semi|multi|bi|tri|auto|super|out|mid)?(?:(?:acknowledgement|ageing|aluminium|apologis|armour|authoris|behaviour|catalogue|categoris|colour"
			+ @"|criticis|customis|defence|endeavour|equalis|favour|finalis|flavour|generalis|grey|harbour|honour"
			+ @"|humour|initialis|instalment|itemis|judgement|labell|labour|licence|litre|localis|manoeuvr|marshall|maximis"
			+ @"|memoris|minimis|modell(?:ed|ing|ers?)|moustache|neighbour|normalis|odour|offence|parlour|plough"
			+ @"|practis|pretence|prioritis|recognis|rigour|rumour|saviour|sceptic|serialis|sizeable|skilful"
			+ @"|splendour|stabilis|standardis|summaris|synchronis|tumour|utilis|valour|vapour|visualis|wilful|whilst"
			+ @"|amongst|behove|cheque|enquir|jewellery|kerb|mould|storey|tyre)\w*"
			+ @"|\w+isations?|cancell(?:ed|ing)|analys(?:e|ed|ing|ers?)|centr(?:e|es|ed)"
			+ @"|characteris(?:e|ed|es|ing)|emphasis(?:e|ed|es|ing)|dreamt|enrol|fulfil|learnt|leant|spelt"
			+ @"|optimis(?:e|ed|es|ing)|organis(?:e|ed|es|ing)|programmes?|realis(?:e|ed|es|ing)"
			+ @"|specialis(?:e|ed|es|ing)|full[\s-]+stop)\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
	)]
	private static partial Regex BritishSpelling();

	[TestMethod]
	public void NoBannedPhrases()
	{
		List<string> violations = DocumentationModel.PhraseViolations(PagePhrases(), BannedPhrases());
		Assert.IsEmpty(violations, FailMessage("Banned phrases", violations, "Remove the phrase, or reword the sentence without it."));
	}

	[TestMethod]
	public void NoBritishSpellings()
	{
		List<string> violations = DocumentationModel.SpellingViolations(BritishSpelling());
		Assert.IsEmpty(violations, FailMessage("British spellings", violations, "Write the US spelling."));
	}

	private static (string Name, Regex Pattern)[] BannedPhrases()
	{
		return
		[
			("previously", Previously()),
			("no longer", NoLonger()),
			("we decided", WeDecided()),
			("was renamed", WasRenamed()),
			("legacy", Legacy()),
			("migrated", Migrated()),
			("used to", UsedTo()),
			("note that", NoteThat()),
			("keep in mind", KeepInMind()),
			("it is worth", ItIsWorth()),
			("in other words", InOtherWords()),
			("every rule", EveryRule()),
			("all rules", AllRules()),
			("the following rules", TheFollowingRules()),
			("all three", AllThree()),
			("both packages", BothFamily()),
			("a set census", TheThreeSet()),
			("simply", Simply()),
			("basically", Basically()),
			("just", Just()),
			("in order to", InOrderTo()),
			("as well as", AsWellAs()),
			("the fact that", TheFactThat()),
			("is able to", IsAbleTo()),
			("switch on or off", SwitchOnOrOff()),
			("it's important to note", ItIsImportantToNote()),
			(SecondPersonName, SecondPerson()),
			("typographic dash", TypographicDash()),
		];
	}

	// A page may say "you" where the imperative does not fit; a comment never addresses its reader.
	private static (string Name, Regex Pattern)[] PagePhrases() => [.. BannedPhrases().Where(phrase => phrase.Name != SecondPersonName)];

	private static string FailMessage(string subject, List<string> violations, string fix)
	{
		return $"{subject} violate the documentation rules of AGENTS.md:"
			+ Environment.NewLine
			+ string.Join(Environment.NewLine, violations)
			+ Environment.NewLine
			+ fix;
	}
}
