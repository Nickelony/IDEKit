using ICSharpCode.AvalonEdit.CodeCompletion;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Windows.Controls;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Provides access to the tooltip associated with an AvalonEdit <see cref="CompletionWindow"/>.
/// </summary>
/// <remarks>
/// The completion window does not expose its tooltip publicly, so the lookup is performed lazily and an
/// unavailable tooltip is reported as a failed attempt. The lookup reflects the private
/// <c>toolTip</c> field of AvalonEdit 6.3.x, walking the type hierarchy with declared-only lookups so the
/// accessor survives the field moving from <see cref="CompletionWindow"/> to its base class; an AvalonEdit
/// upgrade that renames or removes the field is a breaking change for this accessor (the accessor then reports
/// a failed attempt, and the tooltip-aware controller paths stop reporting tooltips). A reflected read that
/// itself throws (for example a field whose declaring type changed) is caught the same way, so the accessor
/// never throws on the selection or render paths that call it.
/// </remarks>
internal static class CompletionWindowTooltipAccess
{
	private static readonly Lazy<FieldInfo?> s_tooltipField = new(FindTooltipField);

	/// <summary>
	/// Gets a value indicating whether the AvalonEdit tooltip field this accessor reflects exists in the
	/// referenced AvalonEdit version. When it is missing, <see cref="TryGetTooltip"/> always reports a failed
	/// attempt and controller tooltip paths are disabled.
	/// </summary>
	internal static bool IsFieldAvailable => s_tooltipField.Value is not null;

	/// <summary>
	/// Tries to retrieve the tooltip associated with the completion window.
	/// </summary>
	/// <param name="completionWindow">The completion window whose tooltip is requested.</param>
	/// <param name="tooltip">The resolved tooltip when available.</param>
	/// <returns><see langword="true"/> when the tooltip field contains a <see cref="ToolTip"/>; otherwise, <see langword="false"/>.</returns>
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
		catch (Exception)
		{
			// A field read can fail in several ways - the field moved to a type the walk misses, its
			// declaring type changed, or a trimmed build dropped it - and this accessor runs on the
			// selection and render paths, where degrading to "tooltip unavailable" is better than
			// throwing. The tooltip presenter reports the unsupported access once; the other callers
			// report a failed attempt exactly like a missing field.
			tooltip = null;
			return false;
		}

		return tooltip is not null;
	}

	// GetField never returns private fields declared on a base type, so each level is searched explicitly.
	// The walk stops at object; the tooltip is a WPF-free AvalonEdit type, so no deeper level can declare it.
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
