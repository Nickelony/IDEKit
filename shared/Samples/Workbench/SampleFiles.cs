using System.IO;

namespace Nickelony.Samples;

/// <summary>
/// The folder the sample edits, written with its starter files the first time it is opened, and the file of its key-binding overrides.
/// </summary>
internal sealed class SampleFiles
{
	internal const string LuaFileName = "sample.lua";
	internal const string SettingsFileName = "settings.json";
	internal const string ReadmeFileName = "README.md";
	internal const string CSharpFileName = "Sample.cs";
	internal const string CSharpProjectFileName = "Sample.csproj";
	internal const string VisualBasicFileName = "Sample.vb";
	internal const string KeyBindingsFileName = "keybindings.json";
	internal const string QuickPickRecentsFileName = "quickpick.json";

	private const string LuaTemplate = """
		-- A sample script for the Nickelony editor features.
		-- Hover a name, press Ctrl+Space, type "print(", or use the Navigate menu on "greet".
		-- The Language menu starts the Lua language server, which also follows "require" into lib/util.lua.

		local util = require("lib.util")

		local GREETINGS = 3

		--- Returns a greeting for a name.
		---@param name string The name to greet.
		---@return string
		local function greet(name)
		    if name == nil then
		        return "nobody" -- TODO: reject a missing name
		    end

		    return string.format("hello, %s", name)
		end

		--- Counts the words of a text.
		---@param text string The text to count in.
		---@return integer
		local function count_words(text)
		    local total = 0

		    for _ in string.gmatch(text, "%S+") do
		        total = total + 1
		    end

		    return total
		end

		for index = 1, GREETINGS do
		    print(greet("world"), index)
		end

		print(count_words("the quick brown fox"))
		print(util.shout("done"))
		prnt("this call has a typo")
		""";

	private const string UtilTemplate = """
		-- A module that sample.lua requires: with the Lua language server, Go to definition on util.shout opens this file.
		local util = {}

		--- Returns the text in capitals, followed by an exclamation mark.
		---@param text string The text to shout.
		---@return string
		function util.shout(text)
		    return string.upper(text) .. "!"
		end

		return util
		""";

	private const string SettingsTemplate = """
		{
		  "theme": "Light",
		  "fontSize": 14,
		  "tabSize": 4,
		  "hoverDelay": 400,
		  "trimTrailingWhitespace": true,
		  "runCommand": "dotnet --info"
		}
		""";

	private const string ReadmeTemplate = """
		# The sample folder

		Every file here opens in the editor from the **Explorer**. *View > Open preview* shows this file rendered
		beside its text, and the preview follows each edit.

		## What to try

		1. Hover `greet` in `sample.lua`, then press **F12** on it.
		2. Open `lib/util.lua` and rename the folder `lib` in the explorer: the open tab follows.
		3. Split a tab with *View > Split editor* and type in either half.
		4. Write TODO or FIXME in this file or in any other Markdown or text file: each open editor of such a file marks it.

		| File | Language features |
		| --- | --- |
		| `sample.lua` | In-process providers, or LuaLS from the Language menu |
		| `settings.json` | The JSON Schema of the settings |
		| `Sample.cs`, `Sample.vb` | The Roslyn language server, when it is started; Visual Basic needs a build that carries it |
		| `Sample.csproj` | The project that Roslyn loads, so it reports compiler errors and not only syntax errors |

		> A deleted file goes to the trash folder of the sample, not the recycle bin.

		```lua
		print("code blocks are highlighted too")
		```
		""";

	private const string CSharpTemplate = """
		// A sample class for the Roslyn language server: hover a type, or press Ctrl+Space after a dot.
		using System;

		internal static class Greeter
		{
		    internal static string Greet(string name) => $"hello, {name}";

		    internal static void Main() => Console.WriteLine(Greet("world"));
		}
		""";

	private const string VisualBasicTemplate = """
		' A sample module for a Roslyn language server that carries Visual Basic, which the build that Visual Studio Code installs does not.
		Imports System

		Friend Module Greeter
		    Friend Function Greet(name As String) As String
		        Return $"hello, {name}"
		    End Function

		    Friend Sub Main()
		        Console.WriteLine(Greet("world"))
		    End Sub
		End Module
		""";

	// Roslyn reads a source file that no project contains as a loose script, which gets syntax errors and no compiler errors, so the starter files
	// include the project that makes Sample.cs a part of a compilation.
	private static string CSharpProjectTemplate()
	{
		return $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <OutputType>Exe</OutputType>
			    <TargetFramework>net{Environment.Version.Major}.0</TargetFramework>
			  </PropertyGroup>
			</Project>
			""";
	}

	/// <param name="root">The folder the files live in, which the constructor creates when it is missing.</param>
	/// <param name="writeStarterFiles">
	/// Whether each missing starter file is written, which a folder of the user's own, such as a project, does without.
	/// </param>
	internal SampleFiles(string root, bool writeStarterFiles = true)
	{
		Root = Path.GetFullPath(root);
		LuaPath = Path.Combine(Root, LuaFileName);
		SettingsPath = Path.Combine(Root, SettingsFileName);
		ReadmePath = Path.Combine(Root, ReadmeFileName);
		CSharpPath = Path.Combine(Root, CSharpFileName);
		VisualBasicPath = Path.Combine(Root, VisualBasicFileName);
		KeyBindingsPath = Path.Combine(Root, KeyBindingsFileName);
		QuickPickRecentsPath = Path.Combine(Root, QuickPickRecentsFileName);

		Directory.CreateDirectory(Root);

		if (!writeStarterFiles)
			return;

		Directory.CreateDirectory(Path.Combine(Root, "lib"));

		WriteWhenMissing(LuaPath, LuaTemplate);
		WriteWhenMissing(Path.Combine(Root, "lib", "util.lua"), UtilTemplate);
		WriteWhenMissing(SettingsPath, SettingsTemplate);
		WriteWhenMissing(ReadmePath, ReadmeTemplate);
		WriteWhenMissing(CSharpPath, CSharpTemplate);
		WriteWhenMissing(Path.Combine(Root, CSharpProjectFileName), CSharpProjectTemplate());
		WriteWhenMissing(VisualBasicPath, VisualBasicTemplate);
	}

	internal string Root { get; }

	internal string LuaPath { get; }

	internal string SettingsPath { get; }

	internal string ReadmePath { get; }

	internal string CSharpPath { get; }

	internal string VisualBasicPath { get; }

	/// <summary>The local path of the key-binding overrides, which does not exist until a binding changes.</summary>
	internal string KeyBindingsPath { get; }

	/// <summary>The local path of the recently accepted items of the quick pick, which does not exist until the user accepts one.</summary>
	internal string QuickPickRecentsPath { get; }

	/// <summary>The folder a command line names, such as a project to try the editor on, or the default folder of the sample.</summary>
	internal static SampleFiles FromCommandLine(IReadOnlyList<string> args)
	{
		return args.Count > 0
			? new SampleFiles(args[0], writeStarterFiles: !Directory.Exists(args[0]) || !Directory.EnumerateFileSystemEntries(args[0]).Any())
			: CreateDefault();
	}

	/// <summary>The default folder of the sample under the user's temporary folder.</summary>
	internal static SampleFiles CreateDefault() => new(Path.Combine(Path.GetTempPath(), "Nickelony.Samples"));

	/// <summary>The language of a file, read from its extension.</summary>
	internal static SampleDocumentKind KindOf(string path)
	{
		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			".lua" => SampleDocumentKind.Lua,
			".json" => SampleDocumentKind.Json,
			".cs" => SampleDocumentKind.CSharp,
			".vb" => SampleDocumentKind.VisualBasic,
			".md" => SampleDocumentKind.Markdown,
			_ => SampleDocumentKind.PlainText,
		};
	}

	private static void WriteWhenMissing(string path, string content)
	{
		if (!File.Exists(path))
			File.WriteAllText(path, content.ReplaceLineEndings("\n"));
	}
}
