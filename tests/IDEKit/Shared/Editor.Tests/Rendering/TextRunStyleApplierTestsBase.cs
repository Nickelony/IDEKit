#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Brush = Avalonia.Media.IBrush;
using FontStretches = Avalonia.Media.FontStretch;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Brush = System.Windows.Media.Brush;
using FontStretches = System.Windows.FontStretches;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>The shared <see cref="TextRunStyleApplier"/> scenarios that run identically across the editor bindings.</summary>
/// <remarks>
/// A binding supplies the engine-specific operations through the hooks below: reading a font family's display name,
/// creating a typeface from a family name, and the two <see langword="null"/>-argument assertions that depend on whether the engine's <c>Typeface</c>
/// is a value type. The typeface overload call itself is split by the <c>AVALONIAEDIT</c> define, because Avalonia passes the typeface
/// as a <c>Typeface?</c> value. The <c>FontWeights</c>/<c>FontStyles</c>/<c>FontStretches</c> aliases map the named font values onto Avalonia's
/// <c>FontWeight</c>/<c>FontStyle</c>/<c>FontStretch</c> enums, so the assertions read the same in both bindings.
/// </remarks>
public abstract class TextRunStyleApplierTestsBase
{
	/// <summary>Returns a font family's display name.</summary>
	/// <param name="fontFamily">The font family to read.</param>
	/// <returns>The family's display name.</returns>
	protected abstract string GetFontFamilyName(FontFamily fontFamily);

	/// <summary>Creates a typeface for the given family name, using the binding's typeface constructor shape.</summary>
	/// <param name="fontFamilyName">The family name.</param>
	/// <returns>The created typeface.</returns>
	protected abstract Typeface CreateNamedTypeface(string fontFamilyName);

	/// <summary>Asserts that the typeface overload rejects a <see langword="null"/> typeface.</summary>
	/// <param name="element">The element to apply the style to.</param>
	/// <remarks>
	/// Avalonia's <c>Typeface</c> is a value type, so the overload takes it by value and cannot represent a <see langword="null"/> argument;
	/// the test reports this case inconclusive.
	/// </remarks>
	protected abstract void AssertNullTypefaceRejected(VisualLineElement element);

	/// <summary>Asserts that <see cref="TextRunStyleApplier.CreateTypeface"/> rejects a <see langword="null"/> base typeface.</summary>
	/// <remarks>
	/// Avalonia's <c>Typeface</c> is a value type, so <c>CreateTypeface</c> takes it by value and cannot represent a <see langword="null"/> argument;
	/// the test reports this case inconclusive.
	/// </remarks>
	protected abstract void AssertNullBaseTypefaceRejected();

	/// <summary>Creates a red test style for the <see langword="null"/>-argument guard scenarios.</summary>
	/// <returns>The test style.</returns>
	protected static TextRunStyle CreateRedStyle() => new(Foreground: Brushes.Red, IsBold: false, IsItalic: false, TextDecorations: null);

	[TestMethod]
	public void Apply_WithStructStyle_AppliesForegroundAndFontTraits()
	{
		// The struct style is applied through the generic overload: the empty style applies nothing, and
		// the populated style applies its foreground and font traits.
		TextEditor emptyEditor = TestHost.CreateEditor("value = 1");
		emptyEditor.TextArea.TextView.LineTransformers.Add(new StructStyleApplyingTransformer(TextRunStyle.Empty));

		using (HostWindow emptyHostWindow = TestHost.ShowInHostWindow(emptyEditor))
		{
			VisualLineElementTextRunProperties emptyProperties = GetFirstElementProperties(emptyEditor);

			Assert.AreEqual(FontWeights.Normal, emptyProperties.Typeface.Weight);
			Assert.AreEqual(FontStyles.Normal, emptyProperties.Typeface.Style);
		}

		TextEditor editor = TestHost.CreateEditor("value = 1");

		editor.TextArea.TextView.LineTransformers.Add(
			new StructStyleApplyingTransformer(new TextRunStyle(Foreground: Brushes.Red, IsBold: true, IsItalic: false, TextDecorations: null))
		);

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual(Brushes.Red, properties.ForegroundBrush);
		Assert.AreEqual(FontWeights.Bold, properties.Typeface.Weight);
	}

	[TestMethod]
	[TestCategory(TestCategories.Performance)]
	public void Apply_WithStructStyle_DoesNotAllocatePerCall()
	{
		TextEditor editor = TestHost.CreateEditor("value = 1");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		VisualLineElement element = GetFirstElement(editor);

		// The style must not allocate on apply: a foreground brush is stored as-is and no font traits
		// request a derived typeface, so the only possible per-call allocation is a boxed struct.
		var style = new TextRunStyle(Foreground: Brushes.Red, IsBold: false, IsItalic: false, TextDecorations: null);

		// Warm up the path so JIT tiering and lazy element state stay out of the measured window.
		for (int i = 0; i < 1_000; i++)
			TextRunStyleApplier.Apply(element, style);

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int i = 0; i < 10_000; i++)
			TextRunStyleApplier.Apply(element, style);

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		// A boxed struct style would allocate once per call (~40 bytes each, ~400 KB over the 10,000
		// calls); the raised bound tolerates runtime and tiered-JIT noise (a tier-up or a GC bookkeeping
		// burst) while still failing on per-call boxing, which stays an order of magnitude above that noise.
		Assert.IsLessThan(100_000, allocated, $"Expected no per-call allocation, but {allocated} bytes were allocated.");
	}

	[TestMethod]
	public void Apply_WithForeground_SetsForegroundBrush()
	{
		TextEditor editor = CreateEditorWithStyle(new TextRunStyle(Foreground: Brushes.Red, IsBold: false, IsItalic: false, TextDecorations: null));
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
	}

	[TestMethod]
	public void Apply_WithBoldAndItalic_AppliesBothFontTraits()
	{
		TextEditor editor = CreateEditorWithStyle(new TextRunStyle(Foreground: null, IsBold: true, IsItalic: true, TextDecorations: null));
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual(FontWeights.Bold, properties.Typeface.Weight);
		Assert.AreEqual(FontStyles.Italic, properties.Typeface.Style);
	}

	[TestMethod]
	public void Apply_WithBoldOnly_PreservesBaseFontStyle()
	{
		TextEditor editor = CreateEditorWithStyle(new TextRunStyle(Foreground: null, IsBold: true, IsItalic: false, TextDecorations: null));
		editor.FontStyle = FontStyles.Oblique;

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual(FontWeights.Bold, properties.Typeface.Weight);
		Assert.AreEqual(FontStyles.Oblique, properties.Typeface.Style);
	}

	[TestMethod]
	public void Apply_WithoutFontTraits_PreservesElementTypeface()
	{
		TextEditor editor = CreateEditorWithStyle(new TextRunStyle(Foreground: Brushes.Red, IsBold: false, IsItalic: false, TextDecorations: null));

		// A non-default base style makes the assertion meaningful: an unconditional typeface overwrite
		// would replace the oblique style with the typeface default.
		editor.FontStyle = FontStyles.Oblique;

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual(Brushes.Red, properties.ForegroundBrush);
		Assert.AreEqual(FontWeights.Normal, properties.Typeface.Weight);
		Assert.AreEqual(FontStyles.Oblique, properties.Typeface.Style);
	}

	[TestMethod]
	public void Apply_WithTextDecorations_AppliesDecorations()
	{
		TextEditor editor = CreateEditorWithStyle(
			new TextRunStyle(Foreground: null, IsBold: false, IsItalic: false, TextDecorations: TextDecorations.Strikethrough)
		);

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextDecorationCollection? decorations = GetFirstElementProperties(editor).TextDecorations;

		Assert.IsNotNull(decorations);
		Assert.HasCount(1, decorations);
		Assert.AreEqual(TextDecorationLocation.Strikethrough, decorations[0].Location);
	}

	[TestMethod]
	public void Apply_WithTextDecorations_UnionsWithExistingDecorations()
	{
		TextEditor editor = TestHost.CreateEditor("value = 1");

		editor.TextArea.TextView.LineTransformers.Add(
			new SequentialStyleApplyingTransformer(
				new TextRunStyle(Foreground: null, IsBold: false, IsItalic: false, TextDecorations: TextDecorations.Underline),
				new TextRunStyle(Foreground: null, IsBold: false, IsItalic: false, TextDecorations: TextDecorations.Strikethrough)
			)
		);

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextDecorationCollection? decorations = GetFirstElementProperties(editor).TextDecorations;

		Assert.IsNotNull(decorations);

		// The editor's SetTextDecorations unions with the decorations already present, so the second
		// apply must not discard the underline the first apply set.
		Assert.Contains(decoration => decoration.Location == TextDecorationLocation.Underline, decorations);
		Assert.Contains(decoration => decoration.Location == TextDecorationLocation.Strikethrough, decorations);
	}

	[TestMethod]
	public void Apply_WithEmptyTextDecorationCollection_LeavesDecorationsUnchanged()
	{
		TextEditor editor = CreateEditorWithStyle(new TextRunStyle(Foreground: null, IsBold: false, IsItalic: false, TextDecorations: []));
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextDecorationCollection? decorations = GetFirstElementProperties(editor).TextDecorations;

		// A non-null but empty collection requests nothing, so the applier must not touch the element's
		// existing decoration state.
		Assert.IsNull(decorations);
	}

	[TestMethod]
	public void Apply_WithProvidedTypeface_UsesTheProvidedTypefaceForFontTraits()
	{
		var providedTypeface = new Typeface(
			fontFamily: new FontFamily("Courier New"),
			style: FontStyles.Italic,
			weight: FontWeights.Bold,
			stretch: FontStretches.Condensed
		);

		TextEditor editor = CreateEditorWithStyle(
			new TextRunStyle(Foreground: null, IsBold: true, IsItalic: false, TextDecorations: null),
			providedTypeface
		);

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual("Courier New", GetFontFamilyName(properties.Typeface.FontFamily));
		Assert.AreEqual(FontWeights.Bold, properties.Typeface.Weight);
		Assert.AreEqual(FontStyles.Italic, properties.Typeface.Style);
		Assert.AreEqual(FontStretches.Condensed, properties.Typeface.Stretch);
	}

	[TestMethod]
	public void Apply_WithProvidedTypeface_WhenStyleHasNoFontTraits_PreservesElementTypeface()
	{
		var providedTypeface = new Typeface(
			fontFamily: new FontFamily("Courier New"),
			style: FontStyles.Italic,
			weight: FontWeights.Bold,
			stretch: FontStretches.Condensed
		);

		TextEditor editor = CreateEditorWithStyle(
			new TextRunStyle(Foreground: Brushes.Red, IsBold: false, IsItalic: false, TextDecorations: null),
			providedTypeface
		);

		// A controlled base family and stretch make the assertion exact: the provided typeface must not
		// be applied, and the element typeface must survive field by field.
		editor.FontFamily = new FontFamily("Times New Roman");
		editor.FontStretch = FontStretches.Expanded;

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

		Assert.AreEqual(Brushes.Red, properties.ForegroundBrush);
		Assert.AreEqual("Times New Roman", GetFontFamilyName(properties.Typeface.FontFamily));
		Assert.AreEqual(FontWeights.Normal, properties.Typeface.Weight);
		Assert.AreEqual(FontStretches.Expanded, properties.Typeface.Stretch);
	}

	[TestMethod]
	public void Apply_WithNullElement_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			TextRunStyleApplier.Apply(null!, new TextRunStyle(Foreground: Brushes.Red, IsBold: false, IsItalic: false, TextDecorations: null))
		);
	}

	[TestMethod]
	public void Apply_WithNullTypeface_Throws()
	{
		TextEditor editor = TestHost.CreateEditor("value = 1");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		VisualLineElement element = GetFirstElement(editor);

		AssertNullTypefaceRejected(element);
	}

	[TestMethod]
	public void CreateTypeface_WithBoldAndItalic_AppliesTraitsAndPreservesBaseFamilyAndStretch()
	{
		var baseTypeface = new Typeface(
			fontFamily: new FontFamily("Segoe UI"),
			style: FontStyles.Normal,
			weight: FontWeights.Normal,
			stretch: FontStretches.Condensed
		);

		Typeface bold = TextRunStyleApplier.CreateTypeface(
			baseTypeface,
			new TextRunStyle(Foreground: null, IsBold: true, IsItalic: false, TextDecorations: null)
		);

		Typeface italic = TextRunStyleApplier.CreateTypeface(
			baseTypeface,
			new TextRunStyle(Foreground: null, IsBold: false, IsItalic: true, TextDecorations: null)
		);

		Assert.AreEqual(FontWeights.Bold, bold.Weight);
		Assert.AreEqual(FontStyles.Normal, bold.Style);
		Assert.AreEqual(FontWeights.Normal, italic.Weight);
		Assert.AreEqual(FontStyles.Italic, italic.Style);
		Assert.AreEqual(baseTypeface.FontFamily, bold.FontFamily);
		Assert.AreEqual(FontStretches.Condensed, bold.Stretch);
	}

	[TestMethod]
	public void CreateTypeface_WithoutFontTraits_PreservesBaseTypeface()
	{
		var baseTypeface = new Typeface(
			fontFamily: new FontFamily("Segoe UI"),
			style: FontStyles.Oblique,
			weight: FontWeights.Light,
			stretch: FontStretches.Expanded
		);

		Typeface derived = TextRunStyleApplier.CreateTypeface(
			baseTypeface,
			new TextRunStyle(Foreground: null, IsBold: false, IsItalic: false, TextDecorations: null)
		);

		Assert.AreEqual(baseTypeface.FontFamily, derived.FontFamily);
		Assert.AreEqual(baseTypeface.Style, derived.Style);
		Assert.AreEqual(baseTypeface.Weight, derived.Weight);
		Assert.AreEqual(baseTypeface.Stretch, derived.Stretch);
	}

	[TestMethod]
	public void CreateTypeface_WithNullBaseTypeface_Throws() => AssertNullBaseTypefaceRejected();

	private sealed class StructStyleApplyingTransformer(TextRunStyle style) : DocumentColorizingTransformer
	{
		protected override void ColorizeLine(DocumentLine line)
		{
			if (line.LineNumber != 1)
				return;

			ChangeLinePart(line.Offset, line.EndOffset, element => TextRunStyleApplier.Apply(element, style));
		}
	}

	private sealed class StyleApplyingTransformer(TextRunStyle style, Typeface? typeface) : DocumentColorizingTransformer
	{
		protected override void ColorizeLine(DocumentLine line)
		{
			if (line.LineNumber != 1)
				return;

			if (typeface is null)
				ChangeLinePart(line.Offset, line.EndOffset, element => TextRunStyleApplier.Apply(element, style));
			else
				ChangeLinePart(line.Offset, line.EndOffset, element => ApplyWithTypeface(element, style, typeface));
		}
	}

	private sealed class SequentialStyleApplyingTransformer(TextRunStyle first, TextRunStyle second) : DocumentColorizingTransformer
	{
		protected override void ColorizeLine(DocumentLine line)
		{
			if (line.LineNumber != 1)
				return;

			ChangeLinePart(
				line.Offset,
				line.EndOffset,

				element =>
				{
					TextRunStyleApplier.Apply(element, first);
					TextRunStyleApplier.Apply(element, second);
				}
			);
		}
	}

#if AVALONIAEDIT
	private static void ApplyWithTypeface(VisualLineElement element, TextRunStyle style, Typeface? typeface) =>
		TextRunStyleApplier.Apply(element, style, typeface.GetValueOrDefault());
#else
	private static void ApplyWithTypeface(VisualLineElement element, TextRunStyle style, Typeface? typeface) =>
		TextRunStyleApplier.Apply(element, style, typeface!);
#endif

	private static TextEditor CreateEditorWithStyle(TextRunStyle style, Typeface? typeface = null)
	{
		TextEditor editor = TestHost.CreateEditor("value = 1");
		editor.TextArea.TextView.LineTransformers.Add(new StyleApplyingTransformer(style, typeface));

		return editor;
	}

	private static VisualLineElement GetFirstElement(TextEditor editor)
	{
		editor.TextArea.TextView.EnsureVisualLines();

		VisualLine visualLine =
			editor.TextArea.TextView.GetVisualLine(1) ?? throw new InvalidOperationException("The first visual line is not available.");

		foreach (VisualLineElement element in visualLine.Elements)
		{
			if (element.DocumentLength > 0)
				return element;
		}

		throw new InvalidOperationException("The first visual line has no element with document content.");
	}

	private static VisualLineElementTextRunProperties GetFirstElementProperties(TextEditor editor) => GetFirstElement(editor).TextRunProperties;
}
