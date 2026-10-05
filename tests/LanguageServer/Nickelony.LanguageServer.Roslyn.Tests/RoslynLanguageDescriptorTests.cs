using System.Reflection;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>Covers the identity and the watch patterns that each shipped language declares.</summary>
[TestClass]
public sealed class RoslynLanguageDescriptorTests
{
	public static string GetLanguageDisplayName(MethodInfo methodInfo, object[] values) => $"{methodInfo.Name} ({values[0]})";

	[TestMethod]
	[DynamicData(nameof(ShippedLanguageDescriptions), DynamicDataDisplayName = nameof(GetLanguageDisplayName))]
	public void Descriptor_DeclaresTheIdentityAndTheRecursiveSourceAndProjectPatterns(
		RoslynLanguage language,
		string expectedLanguageId,
		string expectedDisplayName,
		string expectedSourcePattern,
		string expectedProjectPattern
	)
	{
		var descriptor = RoslynLanguageDescriptor.For(language);

		Assert.AreEqual(expectedLanguageId, descriptor.LanguageId);
		Assert.AreEqual(expectedDisplayName, descriptor.DisplayName);

		IReadOnlyList<FileWatchSpecification> specifications = descriptor.WatchSpecifications;

		Assert.HasCount(2, specifications);

		// The source pattern drives document synchronization, and the Roslyn language server analyzes the whole
		// workspace tree, so the pattern is recursive.
		Assert.AreEqual(expectedSourcePattern, specifications[0].Filter);
		Assert.IsTrue(specifications[0].IncludeSubdirectories);

		// The project pattern tells the server which projects to load; a project file can live in any directory of
		// the workspace tree. The shared build inputs are composed in by the provider, not declared here.
		Assert.AreEqual(expectedProjectPattern, specifications[1].Filter);
		Assert.IsTrue(specifications[1].IncludeSubdirectories);
	}

	private static IEnumerable<object[]> ShippedLanguageDescriptions()
	{
		yield return new object[] { RoslynLanguage.CSharp, "csharp", "C#", "*.cs", "*.csproj" };
		yield return new object[] { RoslynLanguage.VisualBasic, "vb", "Visual Basic", "*.vb", "*.vbproj" };
	}
}
