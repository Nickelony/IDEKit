using System.Runtime.CompilerServices;

// Exposes the internal provider seams (the framework payload store, the response-parser helpers, the
// internal provider constructor, and the launch/settings factories) to the test project and to the
// language packages that derive from the core: a language package reaches the base's internal
// constructor so it can inject a client in its own tests.
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.Roslyn.Tests")]
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.CSharp")]
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.CSharp.Tests")]
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.VisualBasic")]
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.VisualBasic.Tests")]
