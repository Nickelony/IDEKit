using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// The two halves of an engine seam declare the same internal member names, so shared code that calls one half compiles
/// against the other. The comparison reads member names, not signatures, which differ by the toolkit types.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class EngineParityGuardTests
{
	private const string Guard = nameof(EngineParityGuardTests);

	// The seams of each family: the folder of the WPF half and of the Avalonia half, their package names, and the symbol that selects the Avalonia branch.
	private static readonly (string WpfRoot, string AvaloniaRoot, string WpfPackage, string AvaloniaPackage, string AvaloniaSymbol)[] s_families =
	[
		("src/IDEKit/AvalonEdit/", "src/IDEKit/AvaloniaEdit/", "Nickelony.IDEKit.AvalonEdit", "Nickelony.IDEKit.AvaloniaEdit", "AVALONIAEDIT"),

		(
			"src/QuickPick/Nickelony.QuickPick.Wpf/",
			"src/QuickPick/Nickelony.QuickPick.Avalonia/",
			"Nickelony.QuickPick.Wpf",
			"Nickelony.QuickPick.Avalonia",
			"AVALONIAQUICKPICK"
		),
	];

	// A member only one half declares, keyed "{pair}: {half} only: {member}", where the pair is the type name and the half is Wpf or Avalonia.
	private static readonly AllowList s_differences = new("engine parity differences");

	[TestMethod]
	public void EachSeamDeclaresTheMemberNamesOfItsTwin()
	{
		List<string> violations = [];

		foreach ((string wpfRoot, string avaloniaRoot, string wpfPackage, string avaloniaPackage, string avaloniaSymbol) in s_families)
		{
			IEnumerable<string> wpfSeams = RepositoryModel
				.SourcePathsUnder(wpfRoot)
				.Where(path => path.EndsWith("Seam.cs", StringComparison.Ordinal));

			IEnumerable<string> avaloniaSeams = RepositoryModel
				.SourcePathsUnder(avaloniaRoot)
				.Where(path => path.EndsWith("Seam.cs", StringComparison.Ordinal));

			var wpfByKey = wpfSeams.ToDictionary(path => SeamKey(path, wpfRoot, wpfPackage), StringComparer.Ordinal);
			var avaloniaByKey = avaloniaSeams.ToDictionary(path => SeamKey(path, avaloniaRoot, avaloniaPackage), StringComparer.Ordinal);

			foreach (string key in wpfByKey.Keys.Union(avaloniaByKey.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
			{
				if (!wpfByKey.TryGetValue(key, out string? wpf) || !avaloniaByKey.TryGetValue(key, out string? avalonia))
				{
					string half = wpfByKey.ContainsKey(key) ? "Wpf" : "Avalonia";

					violations.Add(
						$"The seam {key} exists in the {half} binding only, so shared code that calls it does not compile against the other."
					);

					continue;
				}

				string type = System.IO.Path.GetFileNameWithoutExtension(wpf);
				Compare(type, wpf: MemberNames(wpf, type, symbols: []), avalonia: MemberNames(avalonia, type, symbols: [avaloniaSymbol]), violations);
			}
		}

		violations.AddRange(s_differences.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Seam halves differ:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	private static void Compare(string type, HashSet<string> wpf, HashSet<string> avalonia, List<string> violations)
	{
		foreach (string name in wpf.Except(avalonia, StringComparer.Ordinal).Order(StringComparer.Ordinal))
			Check(type, half: "Wpf", name, violations);

		foreach (string name in avalonia.Except(wpf, StringComparer.Ordinal).Order(StringComparer.Ordinal))
			Check(type, half: "Avalonia", name, violations);
	}

	private static void Check(string type, string half, string name, List<string> violations)
	{
		if (!s_differences.Allows($"{type}: {half} only: {name}"))
			violations.Add(s_differences.Rejection($"{type}: {half} only: {name}.", Guard));
	}

	/// <summary>The path of a seam below its engine root, with the engine's package name replaced by a neutral word.</summary>
	private static string SeamKey(string path, string root, string package) =>
		path[root.Length..].Replace(package, "Engine", StringComparison.Ordinal);

	/// <summary>The names of the members of a type in a file that code outside the type can reach: every one not declared private.</summary>
	private static HashSet<string> MemberNames(string path, string type, string[] symbols)
	{
		HashSet<string> names = new(StringComparer.Ordinal);

		foreach (
			TypeDeclarationSyntax declaration in RepositoryModel
				.Source(path)
				.Tree(symbols)
				.GetRoot()
				.DescendantNodes()
				.OfType<TypeDeclarationSyntax>()
		)
		{
			if (declaration.Identifier.ValueText != type)
				continue;

			foreach (MemberDeclarationSyntax member in declaration.Members.Where(IsReachable))
				names.Add(NameOf(member));
		}

		return names;
	}

	private static bool IsReachable(MemberDeclarationSyntax member)
	{
		return member is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
			&& member.Modifiers.Any(modifier =>
				modifier.IsKind(SyntaxKind.PublicKeyword)
				|| modifier.IsKind(SyntaxKind.InternalKeyword)
				|| modifier.IsKind(SyntaxKind.ProtectedKeyword)
			);
	}

	private static string NameOf(MemberDeclarationSyntax member)
	{
		return member switch
		{
			MethodDeclarationSyntax method => method.Identifier.Text,
			PropertyDeclarationSyntax property => property.Identifier.Text,
			EventDeclarationSyntax @event => @event.Identifier.Text,
			EventFieldDeclarationSyntax eventField => eventField.Declaration.Variables[0].Identifier.Text,
			FieldDeclarationSyntax field => field.Declaration.Variables[0].Identifier.Text,
			IndexerDeclarationSyntax => "this[]",
			ConstructorDeclarationSyntax => ".ctor",
			_ => member.Kind().ToString(),
		};
	}
}
