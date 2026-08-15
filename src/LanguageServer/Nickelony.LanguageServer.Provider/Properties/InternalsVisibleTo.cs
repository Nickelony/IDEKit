using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Nickelony.LanguageServer.Provider.Tests")]
// The provider packages consume the shared LSP response mapping, which stays internal so it is not
// committed as public surface until an external provider needs it.
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.Lua")]
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.Lua.Tests")]
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.Roslyn")]
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.Roslyn.Tests")]
