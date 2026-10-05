using System.IO;
using System.Text.Json;
using Nickelony.QuickPick;

namespace Nickelony.Samples;

/// <summary>Keeps the recently accepted items of the sample in a JSON file next to the documents, as the key-binding overrides are.</summary>
/// <remarks>
/// The controller owns the order and the capacity of the keys, so the store only reads and writes the document.
/// </remarks>
internal sealed class JsonQuickPickRecentStore : IQuickPickRecentStore
{
	private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

	private readonly string _path;

	/// <param name="path">The local path of the file. A missing or damaged file means no recent items.</param>
	internal JsonQuickPickRecentStore(string path) => _path = path;

	public IReadOnlyDictionary<string, IReadOnlyList<string>> Load()
	{
		try
		{
			if (!File.Exists(_path))
				return new Dictionary<string, IReadOnlyList<string>>();

			var document = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path)) ?? [];

			return document.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value);
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			// The recent items only order the list, so a file that cannot be read starts the sample with none.
			return new Dictionary<string, IReadOnlyList<string>>();
		}
	}

	public bool Save(IReadOnlyDictionary<string, IReadOnlyList<string>> keysByPrefix)
	{
		try
		{
			File.WriteAllText(_path, JsonSerializer.Serialize(keysByPrefix, s_writeOptions));
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// The controller keeps the list in memory for the session, so a folder that cannot be written loses the recent items at exit and nothing else.
			return false;
		}
	}
}
