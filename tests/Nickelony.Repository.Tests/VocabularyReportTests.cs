using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>Lists the last words of product types that the vocabulary table does not name, for a reviewer to read. It never fails.</summary>
[TestClass]
[TestCategory(TestCategories.RepositoryReport)]
public sealed class VocabularyReportTests
{
	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void ReportsTheLastWordsThatTheVocabularyTableDoesNotName()
	{
		Dictionary<string, SortedSet<string>> unlisted = new(StringComparer.Ordinal);

		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			foreach (string path in project.Sources)
			{
				foreach (Microsoft.CodeAnalysis.SyntaxTree tree in RepositoryModel.Source(path).TreesOfEveryBranch(project.DefineConstants))
				{
					foreach (BaseTypeDeclarationSyntax type in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
					{
						string name = type.Identifier.ValueText;
						string last = VocabularyTable.LastWord(name);

						bool named =
							VocabularyTable.RoleWords.Any(word => name.EndsWith(word, StringComparison.Ordinal))
							|| VocabularyTable.RetiredWords.Contains(last, StringComparer.Ordinal)
							|| last == "Handler";

						if (named)
							continue;

						if (!unlisted.TryGetValue(last, out SortedSet<string>? names))
						{
							names = new SortedSet<string>(StringComparer.Ordinal);
							unlisted[last] = names;
						}

						names.Add(name);
					}
				}
			}
		}

		TestContext.WriteLine(
			$"{unlisted.Values.Sum(names => names.Count)} product types end in {unlisted.Count} words that the vocabulary table does not name. Each is a concept noun, or a word to add to the table."
		);

		foreach (
			(string word, SortedSet<string> names) in unlisted
				.OrderByDescending(pair => pair.Value.Count)
				.ThenBy(pair => pair.Key, StringComparer.Ordinal)
		)
		{
			TestContext.WriteLine($"{names.Count, 4}  {word}: {string.Join(", ", names.Take(4))}{(names.Count > 4 ? ", ..." : string.Empty)}");
		}
	}
}
