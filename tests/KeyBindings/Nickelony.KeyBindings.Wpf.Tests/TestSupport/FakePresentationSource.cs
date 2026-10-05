using System.Windows;
using System.Windows.Media;

namespace Nickelony.KeyBindings.Wpf.Tests;

/// <summary>Presentation source stub that allows constructing routed key events without a visual host.</summary>
internal sealed class FakePresentationSource : PresentationSource
{
	public override Visual RootVisual { get; set; } = new ContainerVisual();

	public override bool IsDisposed => false;

	protected override CompositionTarget GetCompositionTargetCore() => null!;
}
