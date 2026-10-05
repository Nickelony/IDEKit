using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A feature has one name in every tier, so a reader finds it by that name in Core, LanguageFeatures and each editor binding, and a slice name
/// that Core and LanguageFeatures both declare is listed with the half of the feature that each holds.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class SliceGuardTests
{
	private const string Guard = nameof(SliceGuardTests);

	private static readonly string[] s_bindingRoots =
	[
		"Nickelony.IDEKit.AvalonEdit",
		"Nickelony.IDEKit.AvaloniaEdit",
		"Nickelony.IDEKit.AvalonEdit.LanguageFeatures",
		"Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures",
	];

	// A namespace directly below an editor binding root that Core and LanguageFeatures do not name, keyed by its last segment.
	private static readonly AllowList s_bindingOnlySlices = new(
		"binding-only slices",
		("Rendering", "Text-run styling types that exist only against an engine's rendering types.")
	);

	// A slice name that Core and LanguageFeatures both declare as a namespace directly below their own root, keyed by the slice name.
	private static readonly AllowList s_homonymSlices = new(
		"homonym slices",
		("Diagnostics", "Core holds the render primitive of a diagnostic span, and LanguageFeatures holds the diagnostic payload."),
		("Editing", "Core holds the offset-based edit kernel, and LanguageFeatures holds the LSP edit payloads."),
		("Formatting", "Core holds the synchronous text-to-text formatter, and LanguageFeatures holds the asynchronous provider of edits."),

		(
			"Navigation",
			"Core holds caret history and the navigation target of an editor, and LanguageFeatures holds definition and reference locations."
		)
	);

	// A folder directly under a language-server package or under the Client wire model that no slice of Core or LanguageFeatures names, keyed by
	// the package name below Nickelony.LanguageServer and its folder path.
	private static readonly AllowList s_roleFolders = new(
		"language-server role folders",
		("Client/Protocol/Capabilities", "The capability payloads of the initialize handshake belong to no editor feature."),
		("Client/Protocol/Documents", "The document identifiers that every feature request carries are shared wire structures."),
		("Client/Protocol/Json", "The converters and the read and write helpers that the payloads of every feature share."),
		("Client/Protocol/Ranges", "The position and range payloads that every feature carries are shared wire structures."),
		("Client/Protocol/Window", "The window message payloads that a server sends to its client belong to no editor feature."),
		("Client/Protocol/Workspace", "The workspace configuration and watched-file payloads belong to no editor feature."),
		("Provider/ClientCapabilities", "The client-capability payload of the initialize handshake belongs to no editor feature."),
		("Provider/Lifecycle", "The startup and replay flows and the provider state."),
		("Provider/Mirroring", "The mirroring of tracked documents to the server serves every feature and belongs to none."),

		(
			"Provider/Pipeline",
			"The document-request pipeline and the runner that send every request to the server under one timeout and restart policy."
		),

		("Provider/Ranges", "The position and range rules that every feature shares."),
		("Provider/Scheduling", "The scheduler that serializes the operations of each document, which every request uses."),
		("Provider/Session", "The context, the events, the lifetime and the timeout count that every collaborator of a provider shares."),
		("Provider/Tracking", "The tracker of the documents mirrored to the server serves every feature and belongs to none."),
		("Provider/Watching", "The file watching and change forwarding that mirror workspace files to the server belong to no editor feature.")
	);

	[TestMethod]
	public void EditorBindingNamespacesNameASliceOfCoreOrLanguageFeatures()
	{
		HashSet<string> sliceNames = CoreAndLanguageFeaturesSliceNames();
		List<string> violations = [];

		foreach (string root in s_bindingRoots)
		{
			foreach (
				string child in RepositoryModel.DeclaredNamespaces(RepositoryModel.Project(root)).Where(declared => IsDirectChild(root, declared))
			)
			{
				string slice = LastSegment(child);

				if (!sliceNames.Contains(slice) && !s_bindingOnlySlices.Allows(slice))
					violations.Add(s_bindingOnlySlices.Rejection($"{child} names no slice of Core or LanguageFeatures.", Guard));
			}
		}

		violations.AddRange(s_bindingOnlySlices.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Slice names differ across tiers:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void LanguageServerFoldersNameASliceOfCoreOrLanguageFeaturesOrAreListed()
	{
		HashSet<string> sliceNames = CoreAndLanguageFeaturesSliceNames();
		List<string> violations = [];

		// The provider framework and each language package keep their folders at the root; the client keeps the folders of its wire model.
		IEnumerable<(string Package, string Folder)> packages =
		[
			.. new[] { RepositoryModel.Project(RepositoryModel.ProviderPackage) }
				.Concat(RepositoryModel.LanguagePackages)
				.Select(project => (project.Name["Nickelony.LanguageServer.".Length..], string.Empty)),

			("Client", "Protocol"),
		];

		foreach ((string package, string folder) in packages)
		{
			string directory = RepositoryPaths.Below("src", "LanguageServer", $"Nickelony.LanguageServer.{package}", folder);

			foreach (
				string child in Directory.EnumerateDirectories(directory).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)
			)
			{
				if (child is "bin" or "obj" or "Properties")
					continue;

				string key = folder.Length == 0 ? $"{package}/{child}" : $"{package}/{folder}/{child}";

				if (!sliceNames.Contains(child) && !s_roleFolders.Allows(key))
					violations.Add(s_roleFolders.Rejection($"{key} names no slice of Core or LanguageFeatures.", Guard));
			}
		}

		violations.AddRange(s_roleFolders.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Language-server folders name no slice:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void ASliceNameThatCoreAndLanguageFeaturesShareIsListed()
	{
		List<string> violations = [];

		foreach (
			string slice in DirectSliceNames("Nickelony.IDEKit.Core")
				.Intersect(DirectSliceNames("Nickelony.IDEKit.LanguageFeatures"), StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
		)
		{
			if (!s_homonymSlices.Allows(slice))
			{
				violations.Add(
					s_homonymSlices.Rejection($"Core and LanguageFeatures both declare the slice {slice}. Give one of them another name.", Guard)
				);
			}
		}

		violations.AddRange(s_homonymSlices.UnmatchedMessages(Guard));

		Assert.IsEmpty(
			violations,
			"Slices that share a name across Core and LanguageFeatures:" + Environment.NewLine + string.Join(Environment.NewLine, violations)
		);
	}

	private static IEnumerable<string> DirectSliceNames(string package) =>
		RepositoryModel.DeclaredNamespaces(RepositoryModel.Project(package)).Where(declared => IsDirectChild(package, declared)).Select(LastSegment);

	private static HashSet<string> CoreAndLanguageFeaturesSliceNames()
	{
		HashSet<string> sliceNames = new(StringComparer.Ordinal);

		foreach (string package in new[] { "Nickelony.IDEKit.Core", "Nickelony.IDEKit.LanguageFeatures" })
		{
			foreach (
				string declared in RepositoryModel
					.DeclaredNamespaces(RepositoryModel.Project(package))
					.Where(name => name.StartsWith(package + ".", StringComparison.Ordinal))
			)
			{
				sliceNames.Add(LastSegment(declared));
			}
		}

		return sliceNames;
	}

	private static bool IsDirectChild(string root, string declared) =>
		declared.StartsWith(root + ".", StringComparison.Ordinal) && !declared[(root.Length + 1)..].Contains('.', StringComparison.Ordinal);

	private static string LastSegment(string name) => name[(name.LastIndexOf('.') + 1)..];
}
