using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class JsonConfigurationSectionReaderTests
{
	[TestMethod]
	public void GetSection_EmptySection_ReturnsRootClone()
	{
		JsonElement settings = JsonSerializer.SerializeToElement(new
		{
			editor = new { fontSize = 14 }
		});

		object? section = JsonConfigurationSectionReader.GetSection(settings, "   ");

		Assert.IsInstanceOfType<JsonElement>(section);
		Assert.AreEqual(14, ((JsonElement)section!).GetProperty("editor").GetProperty("fontSize").GetInt32());
	}

	[TestMethod]
	public void GetSection_DottedSection_ReturnsNestedObject()
	{
		JsonElement settings = JsonSerializer.SerializeToElement(new
		{
			example = new
			{
				runtime = new
				{
					version = "5.4"
				}
			}
		});

		object? section = JsonConfigurationSectionReader.GetSection(settings, "example.runtime");

		Assert.IsInstanceOfType<JsonElement>(section);
		Assert.AreEqual("5.4", ((JsonElement)section!).GetProperty("version").GetString());
	}

	[TestMethod]
	public void GetSection_MatchesSectionNamesCaseInsensitively()
	{
		JsonElement settings = JsonSerializer.SerializeToElement(new
		{
			example = new
			{
				runtime = new
				{
					version = "5.4"
				}
			}
		});

		object? section = JsonConfigurationSectionReader.GetSection(settings, "EXAMPLE.Runtime");

		Assert.IsInstanceOfType<JsonElement>(section);
		Assert.AreEqual("5.4", ((JsonElement)section!).GetProperty("version").GetString());
	}

	[TestMethod]
	public void GetSection_MissingSection_ReturnsNull()
	{
		JsonElement settings = JsonSerializer.SerializeToElement(new
		{
			example = new
			{
				runtime = new
				{
					version = "5.4"
				}
			}
		});

		Assert.IsNull(JsonConfigurationSectionReader.GetSection(settings, "example.missing"));
		Assert.IsNull(JsonConfigurationSectionReader.GetSection(settings, "missing.deep"));
	}

	[TestMethod]
	public void GetSection_NonObjectIntermediateSection_ReturnsNull()
	{
		JsonElement settings = JsonSerializer.SerializeToElement(new
		{
			example = 5
		});

		Assert.IsNull(JsonConfigurationSectionReader.GetSection(settings, "example.runtime"));
	}

	[TestMethod]
	public void GetSection_LeafValue_ReturnsLeafElement()
	{
		JsonElement settings = JsonSerializer.SerializeToElement(new
		{
			example = new
			{
				runtime = new
				{
					version = "5.4"
				}
			}
		});

		object? section = JsonConfigurationSectionReader.GetSection(settings, "example.runtime.version");

		Assert.IsInstanceOfType<JsonElement>(section);
		Assert.AreEqual(JsonValueKind.String, ((JsonElement)section!).ValueKind);
		Assert.AreEqual("5.4", ((JsonElement)section!).GetString());
	}
}
