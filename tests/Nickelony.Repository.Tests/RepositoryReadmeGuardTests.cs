using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>The readme of this project names each guard and report class of the project, so a reader finds what the project checks.</summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed partial class RepositoryReadmeGuardTests
{
	private static readonly string s_projectName = typeof(RepositoryReadmeGuardTests).Assembly.GetName().Name!;

	[TestMethod]
	public void TheReadmeNamesEveryGuardAndReportClass()
	{
		string folder = RepositoryPaths.Below("tests", s_projectName);

		string[] classes =
		[
			.. Directory
				.EnumerateFiles(folder, "*Tests*.cs", SearchOption.TopDirectoryOnly)
				.Select(path => Path.GetFileName(path).Split('.')[0])
				.Where(name => name.EndsWith("GuardTests", StringComparison.Ordinal) || name.EndsWith("ReportTests", StringComparison.Ordinal))
				.Distinct(StringComparer.Ordinal),
		];

		string readme = File.ReadAllText(Path.Combine(folder, "README.md"));

		List<string> violations = [];

		foreach (string name in classes.Where(name => !readme.Contains($"`{name}`", StringComparison.Ordinal)))
			violations.Add($"The readme does not name {name}.");

		foreach (Match match in GuardTestNameRegex().Matches(readme))
		{
			string name = match.Groups["name"].Value;

			if (!classes.Contains(name, StringComparer.Ordinal))
				violations.Add($"The readme names {name}, which this project does not hold.");
		}

		Assert.IsEmpty(
			violations,

			"The readme differs from the guard classes:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, violations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
		);
	}

	[GeneratedRegex(@"`(?<name>\w+(?:GuardTests|ReportTests))`")]
	private static partial Regex GuardTestNameRegex();
}
