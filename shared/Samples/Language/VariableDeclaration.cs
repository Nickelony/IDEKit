using Nickelony.IDEKit.Core.Text;

namespace Nickelony.Samples;

/// <summary>A parameter or a variable the sample language declares.</summary>
/// <param name="Name">The declared name.</param>
/// <param name="NameRange">The range of the name.</param>
internal sealed record VariableDeclaration(string Name, TextRange NameRange);
