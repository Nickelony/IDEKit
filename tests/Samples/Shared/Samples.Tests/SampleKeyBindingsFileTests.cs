using System.IO;
#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Covers the sample's start over a <c>keybindings.json</c> that the key-binding service rejects.</summary>
[STATestClass]
public sealed class SampleKeyBindingsFileTests
{
	private const string UnsupportedVersionJson = """{"version": 99, "commands": []}""";

	[TestMethod]
	public async Task CreateAsync_OverAnUnsupportedSchemaVersion_StartsWithTheDefaultBindings()
	{
		using var directory = new TemporaryDirectory("sample-");
		string path = Path.Combine(directory.Path, SampleFiles.KeyBindingsFileName);
		File.WriteAllText(path, UnsupportedVersionJson);

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				Assert.IsNotEmpty(workbench.KeyBindings.GetDisplayText(SampleCommandIds.Save));
				return Task.CompletedTask;
			}
		);

		Assert.AreEqual(UnsupportedVersionJson, File.ReadAllText(path), "The rejected file stays as the user wrote it.");
	}

	[TestMethod]
	public async Task CreateAsync_OverAFileOfInvalidJson_StartsWithTheDefaultBindings()
	{
		using var directory = new TemporaryDirectory("sample-");
		File.WriteAllText(Path.Combine(directory.Path, SampleFiles.KeyBindingsFileName), "{ not json");

		await SampleTestHost.RunWorkbenchAsync(
			directory,

			workbench =>
			{
				Assert.IsNotEmpty(workbench.KeyBindings.GetDisplayText(SampleCommandIds.Save));
				return Task.CompletedTask;
			}
		);
	}
}
