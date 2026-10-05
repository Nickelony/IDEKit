using System.IO;
using System.IO.Compression;

namespace Nickelony.Samples;

/// <summary>Turns the path of a LuaLS archive into the path of its executable, extracting the archive once.</summary>
/// <remarks>
/// The archive extracts outside the folder of the sample files, because the server indexes its workspace folder and would otherwise read
/// its own scripts. A stamp file records which archive the folder holds, so a changed archive extracts again.
/// </remarks>
internal static class LuaServerArchive
{
	/// <summary>The default folder an archive extracts into.</summary>
	internal static string DefaultExtractionRoot { get; } = Path.Combine(Path.GetTempPath(), "Nickelony.Samples.LuaLS");

	/// <summary>Returns the executable of an archive, or the path itself when it names no archive.</summary>
	/// <param name="path">The path of a <c>.zip</c> archive or of a server executable.</param>
	/// <param name="extractionRoot">The folder the archive extracts into.</param>
	/// <exception cref="FileNotFoundException">The archive holds no <c>lua-language-server</c> executable.</exception>
	internal static string Resolve(string path, string extractionRoot)
	{
		if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
			return path;

		var archive = new FileInfo(path);

		string stamp = Path.Combine(extractionRoot, ".extracted");
		string version = $"{archive.FullName}|{archive.Length}|{archive.LastWriteTimeUtc.Ticks}";

		if (!File.Exists(stamp) || File.ReadAllText(stamp) != version)
		{
			if (Directory.Exists(extractionRoot))
				Directory.Delete(extractionRoot, recursive: true);

			ZipFile.ExtractToDirectory(archive.FullName, extractionRoot);
			File.WriteAllText(stamp, version);
		}

		string name = OperatingSystem.IsWindows() ? "lua-language-server.exe" : "lua-language-server";

		return Directory.EnumerateFiles(extractionRoot, name, SearchOption.AllDirectories).FirstOrDefault()
			?? throw new FileNotFoundException($"The archive holds no {name}.", path);
	}
}
