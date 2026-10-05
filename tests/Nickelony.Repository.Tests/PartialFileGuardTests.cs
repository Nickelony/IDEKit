using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A type that spans several files names the reason in each file name: a part holds either an implemented contract or the engine-neutral half
/// of an editor type, and a part never holds one operation of the type, because a type that outgrows one sentence splits into collaborators.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class PartialFileGuardTests
{
	private const string Guard = nameof(PartialFileGuardTests);

	// The file-name suffixes of a part: the contracts that the part implements, and the engine-neutral half that shared editor files hold.
	private static readonly string[] s_partSuffixes = ["Features", "Sources", "Disposal", "Common"];

	// A part whose file name carries another suffix, keyed by the file path, with the contract that the part implements.
	private static readonly AllowList s_otherParts = new("partial files with another suffix");

	[TestMethod]
	public void EveryPartOfATypeThatSpansFilesIsNamedByAContractOrTheEngineHalf()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (IGrouping<ProjectFile, ProductFile> project in ProductSyntax.Files().GroupBy(file => file.Project))
		{
			Dictionary<string, List<(string Path, string TopLevelName)>> parts = new(StringComparer.Ordinal);

			foreach (ProductFile file in project)
			{
				foreach (TypeDeclarationSyntax type in file.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
				{
					if (type.Parent is TypeDeclarationSyntax || !type.Modifiers.Any(SyntaxKind.PartialKeyword))
						continue;

					string key = $"{type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name}.{type.Identifier.ValueText}";

					if (!parts.TryGetValue(key, out List<(string Path, string TopLevelName)>? list))
						parts[key] = list = [];

					list.Add((file.Path, type.Identifier.ValueText));
				}
			}

			foreach (
				List<(string Path, string TopLevelName)> type in parts.Values.Where(list => list.Select(part => part.Path).Distinct().Count() > 1)
			)
			{
				foreach ((string path, string name) in type)
				{
					string stem = Path.GetFileNameWithoutExtension(path);

					if (stem == name)
						continue;

					string suffix = stem.StartsWith(name + ".", StringComparison.Ordinal) ? stem[(name.Length + 1)..] : string.Empty;

					if (s_partSuffixes.Contains(suffix, StringComparer.Ordinal))
						continue;

					if (!s_otherParts.Allows(path))
					{
						violations.Add(
							s_otherParts.Rejection(
								$"{path} holds a part of {name}, and its name is neither {name}.cs nor {name}.{{{string.Join("|", s_partSuffixes)}}}.cs. Split {name} into collaborators, or name the contract that the part implements.",
								Guard
							)
						);
					}
				}
			}
		}

		violations.UnionWith(s_otherParts.UnmatchedMessages(Guard));

		Assert.IsEmpty(
			violations,
			"Partial files:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}
}
