using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// Provides WPF bitmap-rendering and pixel-scanning helpers for hosted UI tests.
/// </summary>
internal static class TestBitmapRendering
{
	/// <summary>
	/// Pumps queued render-priority dispatcher work and then renders the element (after layout) into a
	/// 96-DPI bitmap.
	/// </summary>
	/// <param name="element">The element to render.</param>
	/// <returns>The rendered bitmap.</returns>
	public static BitmapSource PumpAndRender(FrameworkElement element)
	{
		ArgumentNullException.ThrowIfNull(element);

		WPFTestHost.PumpDispatcher(element.Dispatcher, DispatcherPriority.Render);
		return RenderToBitmap(element);
	}

	/// <summary>
	/// Renders the element (after layout) into a 96-DPI bitmap.
	/// </summary>
	/// <param name="element">The element to render.</param>
	/// <returns>The rendered bitmap.</returns>
	public static BitmapSource RenderToBitmap(FrameworkElement element)
	{
		element.UpdateLayout();

		var bitmap = new RenderTargetBitmap(
			Math.Max(1, (int)element.ActualWidth),
			Math.Max(1, (int)element.ActualHeight),
			96.0,
			96.0,
			PixelFormats.Pbgra32);

		bitmap.Render(element);
		return bitmap;
	}

	/// <summary>
	/// Determines whether any pixel in the leftmost <paramref name="stripWidth"/> columns approximately
	/// matches <paramref name="target"/>.
	/// </summary>
	/// <param name="bitmap">The bitmap to scan.</param>
	/// <param name="stripWidth">The number of leftmost columns to scan.</param>
	/// <param name="target">The color to look for.</param>
	/// <returns><see langword="true"/> when a matching pixel exists.</returns>
	public static bool HasColorInLeftStrip(BitmapSource bitmap, int stripWidth, Color target)
	{
		int width = bitmap.PixelWidth;
		int height = bitmap.PixelHeight;
		int scanWidth = Math.Min(stripWidth, width);

		var pixels = new byte[width * height * 4];
		bitmap.CopyPixels(pixels, width * 4, 0);

		for (int y = 0; y < height; y++)
		{
			if (RowHasColorInLeftStrip(pixels, width, scanWidth, y, target))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Counts the contiguous vertical runs of rows (segments) within the leftmost
	/// <paramref name="stripWidth"/> columns that each contain at least one pixel matching <paramref name="target"/>.
	/// Runs separated by at least one row without a match are counted separately.
	/// </summary>
	/// <param name="bitmap">The bitmap to scan.</param>
	/// <param name="stripWidth">The number of leftmost columns to scan.</param>
	/// <param name="target">The color to look for.</param>
	/// <returns>The number of matching row segments.</returns>
	public static int CountColorRowSegments(BitmapSource bitmap, int stripWidth, Color target)
	{
		int width = bitmap.PixelWidth;
		int height = bitmap.PixelHeight;
		int scanWidth = Math.Min(stripWidth, width);

		var pixels = new byte[width * height * 4];
		bitmap.CopyPixels(pixels, width * 4, 0);

		int segments = 0;
		bool inSegment = false;

		for (int y = 0; y < height; y++)
		{
			bool rowHasColor = RowHasColorInLeftStrip(pixels, width, scanWidth, y, target);

			if (rowHasColor && !inSegment)
			{
				segments++;
				inSegment = true;
			}
			else if (!rowHasColor)
			{
				inSegment = false;
			}
		}

		return segments;
	}

	/// <summary>
	/// Determines the horizontal extent of pixels matching <paramref name="target"/>
	/// within the leftmost <paramref name="stripWidth"/> columns.
	/// </summary>
	/// <param name="bitmap">The bitmap to scan.</param>
	/// <param name="stripWidth">The number of leftmost columns to scan.</param>
	/// <param name="target">The color to look for.</param>
	/// <param name="minColumn">The smallest matching column when a match exists.</param>
	/// <param name="maxColumn">The largest matching column when a match exists.</param>
	/// <returns><see langword="true"/> when at least one matching pixel exists.</returns>
	public static bool TryGetColorColumnBounds(
		BitmapSource bitmap,
		int stripWidth,
		Color target,
		out int minColumn,
		out int maxColumn)
	{
		int width = bitmap.PixelWidth;
		int height = bitmap.PixelHeight;
		int scanWidth = Math.Min(stripWidth, width);

		var pixels = new byte[width * height * 4];
		bitmap.CopyPixels(pixels, width * 4, 0);

		minColumn = int.MaxValue;
		maxColumn = -1;

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < scanWidth; x++)
			{
				if (!Matches(pixels, width, x, y, target))
					continue;

				if (x < minColumn)
					minColumn = x;

				if (x > maxColumn)
					maxColumn = x;
			}
		}

		return maxColumn >= 0;
	}

	/// <summary>
	/// Determines the vertical extent of pixels matching <paramref name="target"/>
	/// within the leftmost <paramref name="stripWidth"/> columns.
	/// </summary>
	/// <param name="bitmap">The bitmap to scan.</param>
	/// <param name="stripWidth">The number of leftmost columns to scan.</param>
	/// <param name="target">The color to look for.</param>
	/// <param name="minRow">The smallest matching row when a match exists.</param>
	/// <param name="maxRow">The largest matching row when a match exists.</param>
	/// <returns><see langword="true"/> when at least one matching pixel exists.</returns>
	public static bool TryGetColorRowBounds(
		BitmapSource bitmap,
		int stripWidth,
		Color target,
		out int minRow,
		out int maxRow)
	{
		int width = bitmap.PixelWidth;
		int height = bitmap.PixelHeight;
		int scanWidth = Math.Min(stripWidth, width);

		var pixels = new byte[width * height * 4];
		bitmap.CopyPixels(pixels, width * 4, 0);

		minRow = int.MaxValue;
		maxRow = -1;

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < scanWidth; x++)
			{
				if (!Matches(pixels, width, x, y, target))
					continue;

				if (y < minRow)
					minRow = y;

				if (y > maxRow)
					maxRow = y;
			}
		}

		return maxRow >= 0;
	}

	private static bool RowHasColorInLeftStrip(byte[] pixels, int width, int scanWidth, int y, Color target)
	{
		for (int x = 0; x < scanWidth; x++)
		{
			if (Matches(pixels, width, x, y, target))
				return true;
		}

		return false;
	}

	private static bool Matches(byte[] pixels, int width, int x, int y, Color target)
	{
		int index = ((y * width) + x) * 4;

		byte blue = pixels[index];
		byte green = pixels[index + 1];
		byte red = pixels[index + 2];
		byte alpha = pixels[index + 3];

		return alpha > 0
			&& Math.Abs(red - target.R) < 40
			&& Math.Abs(green - target.G) < 40
			&& Math.Abs(blue - target.B) < 40;
	}
}
