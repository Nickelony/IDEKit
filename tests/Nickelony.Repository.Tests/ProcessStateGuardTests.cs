using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A library never changes process-wide state that its host owns, so a host that registers an encoding provider decides it once at startup
/// and a library that needs a code page documents the requirement instead of registering the provider itself.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class ProcessStateGuardTests
{
	[TestMethod]
	public void NoLibraryRegistersAnEncodingProvider()
	{
		List<string> violations = [];

		foreach (ProjectFile project in RepositoryModel.SourceProjects)
		{
			foreach (string path in project.Sources)
			{
				foreach (SyntaxTree tree in RepositoryModel.Source(path).TreesOfEveryBranch(project.DefineConstants))
				{
					// Any use of the name counts, however the call is qualified: Encoding.RegisterProvider, System.Text.Encoding.RegisterProvider, a
					// bare RegisterProvider under using static, or a method group.
					IEnumerable<IdentifierNameSyntax> registrations = tree.GetRoot()
						.DescendantNodes()
						.OfType<IdentifierNameSyntax>()
						.Where(name => name.Identifier.ValueText == "RegisterProvider");

					violations.AddRange(registrations.Select(name => $"{path}: {name.Parent}"));
				}
			}
		}

		Assert.IsEmpty(
			violations,

			"A library registers an encoding provider, which is process-wide state that only the host sets:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, violations.Distinct().Order(StringComparer.Ordinal))
		);
	}
}
