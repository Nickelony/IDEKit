using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>Resolves a completion window's tooltip through the reflected tooltip field.</summary>
/// <remarks>
/// The completion window exposes no public tooltip, so the lookup reflects the private <c>toolTip</c> field, walking the hierarchy with declared-only
/// lookups so the field moving to a base class still resolves. A missing field, or a reflected read that throws, degrades to a failed attempt;
/// the lookup never throws on the selection or render paths that call it.
/// </remarks>
internal static class CompletionTooltipResolver
{
	private static readonly Lazy<FieldInfo?> s_tooltipField = new(FindTooltipField);

	/// <summary>Whether the running editor exposes a completion-window tooltip that <see cref="TryGetTooltip"/> can read.</summary>
	/// <remarks>
	/// When the reflected field is missing from the referenced AvalonEdit version, <see cref="TryGetTooltip"/> reports a failed attempt.
	/// </remarks>
	internal static bool IsTooltipAvailable => s_tooltipField.Value is not null;

	/// <summary>Tries to retrieve the tooltip of a completion window.</summary>
	/// <param name="completionWindow">The completion window whose tooltip is requested.</param>
	/// <param name="tooltip">The resolved tooltip when available; otherwise, <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the window's tooltip was resolved; otherwise, <see langword="false"/>.</returns>
	internal static bool TryGetTooltip(CompletionWindow completionWindow, [NotNullWhen(true)] out ToolTip? tooltip)
	{
		FieldInfo? field = s_tooltipField.Value;

		if (field is null)
		{
			tooltip = null;
			return false;
		}

		try
		{
			tooltip = field.GetValue(completionWindow) as ToolTip;
		}
		catch (TargetException)
		{
			// A reflected field read on a mismatched instance; this lookup runs on the selection and render
			// paths, where degrading to "tooltip unavailable" is better than throwing.
			tooltip = null;
			return false;
		}
		catch (FieldAccessException)
		{
			// The field is not accessible in this context (for example a trimmed build).
			tooltip = null;
			return false;
		}
		catch (ArgumentException)
		{
			// The reflecting type changed so the field is not valid for the instance.
			tooltip = null;
			return false;
		}
		catch (NotSupportedException)
		{
			// The runtime does not support the reflected read.
			tooltip = null;
			return false;
		}

		return tooltip is not null;
	}

	// GetField never returns private fields declared on a base type, so each level is searched explicitly.
	// The walk stops at object, the root of every type hierarchy, so no declaring level is skipped.
	private static FieldInfo? FindTooltipField()
	{
		for (Type? type = typeof(CompletionWindow); type is not null && type != typeof(object); type = type.BaseType)
		{
			FieldInfo? field = type.GetField("toolTip", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

			if (field is not null)
				return field;
		}

		return null;
	}
}
