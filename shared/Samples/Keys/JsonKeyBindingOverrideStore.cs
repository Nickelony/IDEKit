using System.IO;
using System.Text.Json;
using Nickelony.KeyBindings;

namespace Nickelony.Samples;

/// <summary>Keeps the key-binding overrides of the sample in a JSON file next to the documents.</summary>
internal sealed class JsonKeyBindingOverrideStore : IKeyBindingOverrideStore
{
	private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

	private readonly string _path;

	private readonly bool _loadsExistingFile;

	/// <param name="path">The local path of the overrides file. A missing file means no overrides.</param>
	/// <param name="loadsExistingFile">Whether <see cref="Load"/> reads the file; when <see langword="false"/>, it returns no overrides.</param>
	internal JsonKeyBindingOverrideStore(string path, bool loadsExistingFile = true)
	{
		_path = path;
		_loadsExistingFile = loadsExistingFile;
	}

	public KeyBindingOverrides Load()
	{
		try
		{
			return _loadsExistingFile && File.Exists(_path)
				? JsonSerializer.Deserialize<KeyBindingOverrides>(File.ReadAllText(_path)) ?? new KeyBindingOverrides()
				: new KeyBindingOverrides();
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			// The service rejects a document of another version and a hand-edited one with conflicts; starting empty keeps the catalog defaults.
			return new KeyBindingOverrides();
		}
	}

	public bool Save(KeyBindingOverrides snapshot)
	{
		try
		{
			File.WriteAllText(_path, JsonSerializer.Serialize(snapshot, s_writeOptions));
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}
}
