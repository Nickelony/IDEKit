using Nickelony.IDEKit.Core.Text;

namespace Nickelony.Samples;

/// <summary>A function the sample language declares: its name, the range of its header and its parameters.</summary>
/// <param name="Name">The name of the function.</param>
/// <param name="HeaderRange">The range of the <c>function name(...)</c> header.</param>
/// <param name="NameRange">The range of the name.</param>
/// <param name="Parameters">The parameters, in declaration order.</param>
internal sealed record FunctionDeclaration(string Name, TextRange HeaderRange, TextRange NameRange, IReadOnlyList<VariableDeclaration> Parameters);
