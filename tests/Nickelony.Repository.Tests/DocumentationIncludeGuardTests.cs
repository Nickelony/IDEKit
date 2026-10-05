using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// An <c>include</c> element of a doc comment names a file that exists and selects at least one element of it, because the compiler expands
/// an include that selects nothing into no documentation and reports no error.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed partial class DocumentationIncludeGuardTests
{
	[TestMethod]
	public void EveryIncludeResolvesToAnExistingFileAndAMember()
	{
		List<string> violations = [];
		Dictionary<string, XDocument> documents = new(StringComparer.Ordinal);
		int includes = 0;

		foreach (string path in RepositoryModel.SourcePathsUnder("src", "samples", "shared", "tests"))
		{
			string text = RepositoryModel.Source(path).Text;

			if (!text.Contains("<include", StringComparison.Ordinal))
				continue;

			string directory = Path.GetDirectoryName(RepositoryPaths.Below(path.Split('/')))!;
			string[] lines = text.Split('\n');

			for (int index = 0; index < lines.Length; index++)
			{
				if (!lines[index].TrimStart().StartsWith("///", StringComparison.Ordinal))
					continue;

				foreach (Match match in IncludeElement().Matches(lines[index]))
				{
					includes++;
					string? violation = Check(match.Value, directory, documents);

					if (violation is not null)
						violations.Add($"{path}:{index + 1}: {violation}");
				}
			}
		}

		Assert.IsGreaterThan(
			0,
			includes,
			"The guard found no include element, so it checks nothing. Read the doc-comment lines that hold an include."
		);

		Assert.IsEmpty(
			violations,

			"Documentation includes:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, violations)
				+ Environment.NewLine
				+ "Fix the file or the member name, or delete the include."
		);
	}

	/// <summary>Reports why an include selects nothing, or <see langword="null"/> when it selects a member.</summary>
	private static string? Check(string element, string directory, Dictionary<string, XDocument> documents)
	{
		XElement include;

		try
		{
			include = XElement.Parse(element);
		}
		catch (XmlException)
		{
			return $"the include '{element}' is not well-formed XML.";
		}

		string? file = (string?)include.Attribute("file");
		string? selector = (string?)include.Attribute("path");

		if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(selector))
			return $"the include '{element}' names no file or no path.";

		string target = Path.GetFullPath(Path.Combine(directory, file));

		if (!File.Exists(target))
			return $"includes the file '{file}', which does not exist.";

		if (!documents.TryGetValue(target, out XDocument? document))
		{
			document = XDocument.Load(target);
			documents[target] = document;
		}

		Match member = MemberSelector().Match(selector);

		if (member.Success)
		{
			string name = member.Groups["name"].Value;
			XElement? declared = document.XPathSelectElement($"doc/members/member[@name='{name}']");

			if (declared is null)
				return $"includes the member '{name}', which '{file}' does not hold.";

			return declared.HasElements ? null : $"includes the member '{name}', which holds no element in '{file}'.";
		}

		try
		{
			return document.XPathSelectElements(selector).Any() ? null : $"the path '{selector}' selects nothing in '{file}'.";
		}
		catch (XPathException)
		{
			return $"the path '{selector}' is not a valid XPath expression.";
		}
	}

	/// <summary>An include element on one line of a doc comment.</summary>
	[GeneratedRegex(@"<include\b[^>]*/>", RegexOptions.CultureInvariant)]
	private static partial Regex IncludeElement();

	/// <summary>The path that selects the content of one member of an external member doc.</summary>
	[GeneratedRegex(@"^doc/members/member\[@name='(?<name>[^']+)'\]/\*$", RegexOptions.CultureInvariant)]
	private static partial Regex MemberSelector();
}
