using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A namespace holds at most 30 public types, so a package with more takes feature namespaces and no host imports the types of an audience
/// it does not serve. A namespace that is larger by design is listed with its reason.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class PublicTypeBoundGuardTests
{
	private const string Guard = nameof(PublicTypeBoundGuardTests);

	private const int Bound = 30;

	// A namespace with more public types than the bound, keyed by the namespace.
	private static readonly AllowList s_largeNamespaces = new(
		"large namespaces",

		(
			"Nickelony.IDEKit.Workspace.Documents",
			"The store API: its interfaces and its operations, each with a request, a result and an outcome, which a split would put in a cycle with the store interface."
		),

		(
			"Nickelony.LanguageServer.Client.Protocol",
			"The wire model: the records of the specification, which the protocol names flat and no host imports by audience."
		)
	);

	[TestMethod]
	public void ANamespaceHoldsNoMoreThanThirtyPublicTypes()
	{
		string root = RepositoryPaths.Root();
		Dictionary<string, int> counts = new(StringComparer.Ordinal);

		foreach (
			string baseline in new[] { "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt" }
				.SelectMany(name => RepositoryModel.EnumerateFiles(Path.Combine(root, "src"), name))
				.Order(StringComparer.Ordinal)
		)
		{
			foreach (string line in File.ReadLines(baseline))
			{
				// A type declaration is the one baseline line without a space: members carry a signature, a value or a modifier.
				if (line.Length == 0 || line[0] == '#' || line.Contains(' ', StringComparison.Ordinal))
					continue;

				int lastDot = line.LastIndexOf('.');

				if (lastDot < 0)
					continue;

				string ns = line[..lastDot];
				counts[ns] = counts.GetValueOrDefault(ns) + 1;
			}
		}

		List<string> violations = [];

		foreach ((string ns, int count) in counts.Where(pair => pair.Value > Bound).OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			if (!s_largeNamespaces.Allows(ns))
			{
				violations.Add(
					s_largeNamespaces.Rejection(
						$"{ns} holds {count} public types, and the bound is {Bound}. Split it by audience, or list it with the reason it stays whole.",
						Guard
					)
				);
			}
		}

		violations.AddRange(s_largeNamespaces.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Large namespaces:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}
}
