using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Nickelony.IDEKit.Testing;

/// <summary>Provides Avalonia bitmap-rendering and pixel-scanning helpers for hosted UI tests.</summary>
/// <remarks>
/// The Avalonia suite runs the Skia-backed headless renderer, so an element can be rendered into a <see cref="RenderTargetBitmap"/>
/// and its pixels scanned.
/// </remarks>
internal static class TestBitmapRendering
{
	/// <summary>Pumps queued render-priority dispatcher work and then renders the element (after layout) into a bitmap.</summary>
	public static Bitmap PumpAndRender(Visual element)
	{
		ArgumentNullException.ThrowIfNull(element);

		AvaloniaTestHost.PumpDispatcher(element.Dispatcher ?? Dispatcher.UIThread, DispatcherPriority.Render);
		return RenderToBitmap(element);
	}

	/// <summary>Renders the element at its current bounds into a 96-DPI bitmap.</summary>
	public static Bitmap RenderToBitmap(Visual element)
	{
		ArgumentNullException.ThrowIfNull(element);

		int width = Math.Max(1, (int)Math.Ceiling(element.Bounds.Width));
		int height = Math.Max(1, (int)Math.Ceiling(element.Bounds.Height));

		var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96.0, 96.0));
		bitmap.Render(element);
		return bitmap;
	}

	/// <summary>
	/// Determines whether any pixel in the leftmost <paramref name="stripWidth"/> columns approximately matches <paramref name="target"/>.
	/// </summary>
	public static bool HasColorInLeftStrip(Bitmap bitmap, int stripWidth, Color target)
	{
		ArgumentNullException.ThrowIfNull(bitmap);

		var pixels = Pixels.Read(bitmap);
		int scanWidth = Math.Min(stripWidth, pixels.Width);

		for (int y = 0; y < pixels.Height; y++)
		{
			if (RowHasColorInLeftStrip(pixels, scanWidth, y, target))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Counts the contiguous vertical runs of rows (segments) within the leftmost <paramref name="stripWidth"/> columns that each contain
	/// at least one pixel matching <paramref name="target"/>. Runs separated by at least one row without a match are counted separately.
	/// </summary>
	public static int CountColorRowSegments(Bitmap bitmap, int stripWidth, Color target)
	{
		ArgumentNullException.ThrowIfNull(bitmap);

		var pixels = Pixels.Read(bitmap);

		int scanWidth = Math.Min(stripWidth, pixels.Width);

		int segments = 0;
		bool inSegment = false;

		for (int y = 0; y < pixels.Height; y++)
		{
			bool rowHasColor = RowHasColorInLeftStrip(pixels, scanWidth, y, target);

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
	/// Determines the horizontal extent of pixels matching <paramref name="target"/> within the leftmost <paramref name="stripWidth"/> columns.
	/// </summary>
	/// <param name="minColumn">The smallest matching column when a match exists.</param>
	/// <param name="maxColumn">The largest matching column when a match exists.</param>
	/// <returns><see langword="true"/> when at least one matching pixel exists.</returns>
	public static bool TryGetColorColumnBounds(Bitmap bitmap, int stripWidth, Color target, out int minColumn, out int maxColumn)
	{
		ArgumentNullException.ThrowIfNull(bitmap);

		var pixels = Pixels.Read(bitmap);
		int scanWidth = Math.Min(stripWidth, pixels.Width);

		minColumn = int.MaxValue;
		maxColumn = -1;

		for (int y = 0; y < pixels.Height; y++)
		{
			for (int x = 0; x < scanWidth; x++)
			{
				if (!pixels.Matches(x, y, target))
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
	/// Determines the vertical extent of pixels matching <paramref name="target"/> within the leftmost <paramref name="stripWidth"/> columns.
	/// </summary>
	/// <param name="minRow">The smallest matching row when a match exists.</param>
	/// <param name="maxRow">The largest matching row when a match exists.</param>
	/// <returns><see langword="true"/> when at least one matching pixel exists.</returns>
	public static bool TryGetColorRowBounds(Bitmap bitmap, int stripWidth, Color target, out int minRow, out int maxRow)
	{
		ArgumentNullException.ThrowIfNull(bitmap);

		var pixels = Pixels.Read(bitmap);
		int scanWidth = Math.Min(stripWidth, pixels.Width);

		minRow = int.MaxValue;
		maxRow = -1;

		for (int y = 0; y < pixels.Height; y++)
		{
			for (int x = 0; x < scanWidth; x++)
			{
				if (!pixels.Matches(x, y, target))
					continue;

				if (y < minRow)
					minRow = y;

				if (y > maxRow)
					maxRow = y;
			}
		}

		return maxRow >= 0;
	}

	private static bool RowHasColorInLeftStrip(Pixels pixels, int scanWidth, int y, Color target)
	{
		for (int x = 0; x < scanWidth; x++)
		{
			if (pixels.Matches(x, y, target))
				return true;
		}

		return false;
	}

	/// <summary>A bitmap's pixels copied into a managed BGRA buffer, so the scanners can read them without pinning.</summary>
	private readonly struct Pixels
	{
		private readonly byte[] _data;

		private Pixels(byte[] data, int width, int height)
		{
			_data = data;
			Width = width;
			Height = height;
		}

		public int Width { get; }

		public int Height { get; }

		public static Pixels Read(Bitmap bitmap)
		{
			PixelSize size = bitmap.PixelSize;

			int width = size.Width;
			int height = size.Height;
			int stride = width * 4;

			byte[] data = new byte[stride * height];
			var handle = GCHandle.Alloc(data, GCHandleType.Pinned);

			try
			{
				bitmap.CopyPixels(
					sourceRect: new PixelRect(x: 0, y: 0, width, height),
					buffer: handle.AddrOfPinnedObject(),
					bufferSize: data.Length,
					stride
				);
			}
			finally
			{
				handle.Free();
			}

			return new Pixels(data, width, height);
		}

		public bool Matches(int x, int y, Color target)
		{
			int index = ((y * Width) + x) * 4;

			byte blue = _data[index];
			byte green = _data[index + 1];
			byte red = _data[index + 2];
			byte alpha = _data[index + 3];

			return alpha > 0 && Math.Abs(red - target.R) < 40 && Math.Abs(green - target.G) < 40 && Math.Abs(blue - target.B) < 40;
		}
	}
}
