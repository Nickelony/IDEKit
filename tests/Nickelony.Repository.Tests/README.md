# Repository guards

This project checks the repository's own files, not product behavior, so a rule that no compiler enforces fails a
test run instead of waiting for a review. Run the guards with:

```powershell
dotnet test .\Nickelony.IDEKit.slnx --filter TestCategory=Repository
```

A guard that carries an allow-list row names the exception and the reason, and it fails when the row
matches nothing. A row that ends in `*` covers every key that starts with the text before the star.
The `RepositoryReport` category holds the report classes, which print a measure and never fail:
run them with `--logger "console;verbosity=detailed"`.

## What each family guards

| Family | Rule | Test classes |
|---|---|---|
| Documentation | The prose rules of `AGENTS.md`: banned phrases, spellings and shapes of a doc comment. | `DocumentationGuardTests`, `DocumentationIncludeGuardTests` |
| Namespaces | A file declares the namespace its location derives, a root namespace holds only what the whole package uses, and the global usings of a project name only logging and referenced packages. | `NamespaceGuardTests`, `RootNamespaceGuardTests` |
| Structure | Folders and namespaces of a package depend on each other in one direction, and a feature has one name in every tier. | `FolderCycleGuardTests`, `NamespaceCycleGuardTests`, `SliceGuardTests` |
| Projects | References point down the tiers, name only what the code uses, and grant `InternalsVisibleTo` only to the own test assembly. | `ProjectReferenceGuardTests`, `ReferenceUsageGuardTests`, `InternalsVisibleToGuardTests` |
| Names and members | A type name follows the vocabulary table, a member of an internal type is internal or private, a log event id lies in its package's block, and a partial file is named by the contract or engine half it holds. | `TypeNameGuardTests`, `MemberModifierGuardTests`, `EventIdGuardTests`, `PublicTypeBoundGuardTests`, `PartialFileGuardTests` |
| Member conventions | A cancellation token and a logger parameter carry their standard names, a method that returns a task ends in `Async`, a method that reacts to an event is `Handle{Event}`, an options type is a sealed record with a `Default`, a package keeps one logging style and takes a plain `ILogger`, and an await states the context it resumes on. | `ParameterNameGuardTests`, `MethodNameGuardTests`, `OptionsShapeGuardTests`, `LoggingGuardTests`, `AwaitContextGuardTests` |
| Public surface | A public type ships with a consumer, a public member rejects a null argument and a null member of an input record that it reads, and a public payload collection is a read-only interface. | `PublicSurfaceGuardTests`, `PublicArgumentGuardTests`, `PublicCollectionGuardTests` |
| Test seams | A test hook runs only through a null-conditional invoke, a hooks type holds no settable factory, and no type keeps static mutable state. | `TestSeamGuardTests` |
| Process state | A library registers no process-wide state that only its host sets, such as an encoding provider. | `ProcessStateGuardTests` |
| Parity | The WPF and Avalonia twin of a package, and the two halves of an engine seam, declare the same names. | `BindingParityGuardTests`, `EngineParityGuardTests` |
| Tests | A test class is sealed, test helpers sit in a `TestSupport` folder, and shared test source lives in its family's folder. Shared product source is compiled by two projects, and `shared/README.md` names each home, its compilers and its define. | `TestClassGuardTests`, `TestLayoutGuardTests`, `TestOrganizationGuardTests` |
| Reports | Measures for a reviewer to read. | `DocumentationReportTests`, `StructureReportTests`, `VocabularyReportTests` |

A guard keeps this list in sync: `RepositoryReadmeGuardTests` fails when a guard or report class is missing from the
table or the table names a class that does not exist.

## Where the helpers live

The model that the guards read - paths, project files, source files, the vocabulary table and the allow-list - lives
in `TestSupport/`, so a guard class holds only its rules.
