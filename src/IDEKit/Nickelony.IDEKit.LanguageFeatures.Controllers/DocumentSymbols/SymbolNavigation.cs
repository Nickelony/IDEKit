using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;

/// <summary>The place an item of the document symbol modes leads to: the editor that shows the symbol, and the range the preview selects.</summary>
/// <param name="Target">The editor that shows the document.</param>
/// <param name="Range">The range of the name of the symbol, or of the symbol when the provider gave no name range.</param>
internal sealed record SymbolNavigation(IEditorNavigationTarget Target, TextPositionRange Range);
