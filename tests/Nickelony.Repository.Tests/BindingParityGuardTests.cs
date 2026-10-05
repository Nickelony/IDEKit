using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// The WPF and Avalonia editor bindings, the WPF and Avalonia key-binding adapters, and the WPF and Avalonia
/// quick-pick windows declare the same public type and member names, so a feature that exists in one exists in the other. The comparison reads
/// names from the public API baselines, not signatures, which differ by the toolkit types and, for a key-binding adapter, by an options parameter
/// that only one side takes.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class BindingParityGuardTests
{
	private const string Guard = nameof(BindingParityGuardTests);
	private const string Wpf = "Nickelony.IDEKit.AvalonEdit";
	private const string Avalonia = "Nickelony.IDEKit.AvaloniaEdit";
	private const string WpfKeyBindings = "Nickelony.KeyBindings.Wpf";
	private const string AvaloniaKeyBindings = "Nickelony.KeyBindings.Avalonia";
	private const string WpfQuickPick = "Nickelony.QuickPick.Wpf";
	private const string AvaloniaQuickPick = "Nickelony.QuickPick.Avalonia";

	private static readonly string[] s_modifiers =
	[
		"static",
		"const",
		"abstract",
		"virtual",
		"override",
		"sealed",
		"readonly",
		"new",
		"extern",
		"unsafe",
	];

	// The twin packages of each family: the root of the WPF side and of the Avalonia side, and the label and the name suffix of each package.
	private static readonly (string WpfRoot, string AvaloniaRoot, (string Label, string Suffix)[] Packages)[] s_families =
	[
		(Wpf, Avalonia, [("base", ""), ("LanguageFeatures", ".LanguageFeatures"), ("Markdown", ".Markdown"), ("TextMate", ".TextMate")]),
		(WpfKeyBindings, AvaloniaKeyBindings, [("KeyBindings", "")]),
		(WpfQuickPick, AvaloniaQuickPick, [("QuickPick", "")]),
	];

	// A name only one binding declares, keyed "{package}: {binding} only: {name below the WPF binding root}". An entry for a type or a namespace
	// covers every member below it.
	private static readonly AllowList s_differences = new(
		"parity differences",

		(
			"KeyBindings: Avalonia only: KeyRepeatGate",
			"Avalonia reports no auto-repeat flag, so the adapter tracks held keys; WPF reads KeyEventArgs.IsRepeat."
		),

		(
			"KeyBindings: Wpf only: KeyDownHandlingOptions",
			"Only the WPF adapter filters AltGr, and it ignores auto-repeat through these options; the Avalonia adapter ignores repeats through KeyRepeatGate and has no AltGr filter."
		),

		("KeyBindings: Wpf only: KeyEventArgsExtensions.IsAltGr", "The AltGr check serves the WPF filter; the Avalonia adapter has no filter."),

		(
			"KeyBindings: Wpf only: KeyGestureDisplayTextFormatter",
			"Renders through the WPF key-gesture converter; the Avalonia adapter ships no formatter."
		),

		(
			"KeyBindings: Wpf only: KeyCodeConversion.ToWpfKey",
			"Only the WPF gesture formatter converts a key code to the toolkit's key type; the Avalonia adapter ships no formatter."
		),

		(
			"KeyBindings: Wpf only: KeyCodeConversion.ToWpfModifierKeys",
			"Only the WPF gesture formatter converts modifiers to the toolkit's modifier type; the Avalonia adapter ships no formatter."
		),

		(
			"LanguageFeatures: AvalonEdit only: LanguageFeatures.Completion.CompletionTooltipTheme",
			"AvaloniaEdit's completion window publishes no tooltip member, so the binding builds no tooltip pipeline and no theme for it."
		),

		(
			"LanguageFeatures: AvalonEdit only: LanguageFeatures.Completion.CompletionPresenter.CancelTooltipUpdate",
			"The tooltip pipeline is WPF only; the Avalonia binding has no tooltip update to cancel."
		),

		(
			"LanguageFeatures: AvalonEdit only: LanguageFeatures.Completion.CompletionPresenter.IsTooltipSupported",
			"The tooltip pipeline is WPF only; the Avalonia binding has nothing to report."
		),

		(
			"LanguageFeatures: AvalonEdit only: LanguageFeatures.Completion.CompletionPresenter.TooltipTheme.get",
			"The tooltip pipeline is WPF only; the Avalonia binding has no tooltip theme."
		),

		(
			"LanguageFeatures: AvalonEdit only: LanguageFeatures.Completion.CompletionPresenter.TooltipTheme.set",
			"The tooltip pipeline is WPF only; the Avalonia binding has no tooltip theme."
		),

		(
			"LanguageFeatures: AvalonEdit only: LanguageFeatures.Completion.CompletionPresenterHooks.BuildMarkdownContent",
			"The tooltip pipeline is WPF only; the Avalonia binding has no tooltip to fill with rendered Markdown."
		),

		(
			"LanguageFeatures: AvalonEdit only: LanguageFeatures.Completion.CompletionPresenterHooks.ConfigureTooltip",
			"The tooltip pipeline is WPF only; the Avalonia binding has no tooltip to configure."
		),

		(
			"LanguageFeatures: AvalonEdit only: LanguageFeatures.Completion.CompletionPresenterHooks.ResolveDescriptionAsync",
			"The tooltip pipeline is WPF only; the Avalonia binding shows no tooltip to fill."
		),

		(
			"LanguageFeatures: AvalonEdit only: LanguageFeatures.Completion.CompletionPresentationOptions.TooltipResolveDelay",
			"The tooltip pipeline is WPF only; the Avalonia binding has no tooltip update to delay."
		),

		(
			"LanguageFeatures: AvalonEdit only: LanguageFeatures.Completion.CompletionPresentationOptions.TooltipTheme",
			"The tooltip pipeline is WPF only; the Avalonia binding has no tooltip to theme."
		),

		("base: AvalonEdit only: LineStatus.LineStatusIconMarginBase.HitTestCore", "A WPF margin hit-tests through HitTestCore."),

		(
			"base: AvalonEdit only: LineStatus.LineStatusIconMarginBase.OnMouseLeftButtonDown",
			"A WPF margin takes a click through OnMouseLeftButtonDown."
		),

		("base: AvalonEdit only: LineStatus.LineStatusMarginBase.OnRender", "A WPF margin draws through OnRender."),

		(
			"base: AvaloniaEdit only: LineStatus.LineStatusIconMarginBase.OnPointerPressed",
			"An Avalonia margin takes a click through OnPointerPressed."
		),

		("base: AvaloniaEdit only: LineStatus.LineStatusIconMarginBase.Render", "An Avalonia margin draws through Render."),
		("base: AvaloniaEdit only: LineStatus.LineStatusMarginBase.Render", "An Avalonia margin draws through Render.")
	);

	[TestMethod]
	public void EachWpfPackageDeclaresTheApiNamesOfItsAvaloniaTwin()
	{
		List<string> violations = [];

		foreach ((string wpfRoot, string avaloniaRoot, (string Label, string Suffix)[] packages) in s_families)
		{
			string wpfSide = wpfRoot[(wpfRoot.LastIndexOf('.') + 1)..];
			string avaloniaSide = avaloniaRoot[(avaloniaRoot.LastIndexOf('.') + 1)..];

			foreach ((string label, string suffix) in packages)
			{
				HashSet<string> wpf = ApiNames(wpfRoot + suffix, wpfRoot, avaloniaRoot);
				HashSet<string> avalonia = ApiNames(avaloniaRoot + suffix, wpfRoot, avaloniaRoot);

				foreach (string name in wpf.Except(avalonia, StringComparer.Ordinal).Order(StringComparer.Ordinal))
					Check(label, side: wpfSide, name, violations);

				foreach (string name in avalonia.Except(wpf, StringComparer.Ordinal).Order(StringComparer.Ordinal))
					Check(label, side: avaloniaSide, name, violations);
			}
		}

		violations.AddRange(s_differences.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "The bindings declare different API names:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	/// <summary>Accepts a name that the list holds, directly or through the type or namespace that contains it.</summary>
	private static void Check(string label, string side, string name, List<string> violations)
	{
		for (string path = name; path.Length > 0; path = path[..Math.Max(path.LastIndexOf('.'), 0)])
		{
			if (s_differences.Allows($"{label}: {side} only: {path}"))
				return;
		}

		violations.Add(s_differences.Rejection($"{label}: {side} only: {name}.", Guard));
	}

	/// <summary>
	/// Reads the declared API of a package and reduces each line to its member path, with the Avalonia root namespace mapped to the WPF one.
	/// </summary>
	private static HashSet<string> ApiNames(string package, string wpfRoot, string avaloniaRoot)
	{
		string folder = RepositoryModel.Project(package).RelativeDirectory;
		HashSet<string> names = new(StringComparer.Ordinal);

		foreach (string file in new[] { "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt" })
		{
			string path = RepositoryPaths.Below(folder, file);

			if (!File.Exists(path))
				continue;

			foreach (string line in File.ReadAllLines(path))
			{
				string? name = ApiName(line);

				if (name is not null)
				{
					names.Add(
						name.Replace(avaloniaRoot, wpfRoot, StringComparison.Ordinal).Replace(wpfRoot + ".", string.Empty, StringComparison.Ordinal)
					);
				}
			}
		}

		return names;
	}

	private static string? ApiName(string line)
	{
		string text = line.Trim();

		if (text.Length == 0 || text[0] == '#')
			return null;

		string[] words = text.Split(' ');
		int skip = 0;

		while (skip < words.Length - 1 && s_modifiers.Contains(words[skip], StringComparer.Ordinal))
			skip++;

		text = string.Join(' ', words[skip..]);

		int arrow = text.IndexOf(" -> ", StringComparison.Ordinal);

		if (arrow >= 0)
			text = text[..arrow];

		int equals = text.IndexOf(" = ", StringComparison.Ordinal);

		if (equals >= 0)
			text = text[..equals];

		int parenthesis = text.IndexOf('(', StringComparison.Ordinal);

		return parenthesis >= 0 ? text[..parenthesis] : text;
	}
}
