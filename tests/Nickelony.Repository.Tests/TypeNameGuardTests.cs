using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nickelony.Testing;

namespace Nickelony.Repository.Tests;

/// <summary>
/// A type name carries one meaning per role suffix, an enum that classifies is an outcome and never a result, an event payload
/// is named after its event, a public type takes no product prefix and never its namespace's name, and a public type sits in a namespace
/// that names its feature, so a new use forces a naming decision.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Repository)]
public sealed class TypeNameGuardTests
{
	private const string Guard = nameof(TypeNameGuardTests);

	private const string ClientProject = "Nickelony.LanguageServer.Client";

	private const string WireNamespace = ClientProject + ".Protocol";

	// The role words that a plain noun could misuse, so a type that ends in one carries its reason here. The other words of the vocabulary table
	// are read from CONVENTIONS.md and need no row.
	private static readonly string[] s_restrictedSuffixes = ["Coordinator", "Gate", "Service", "Lifetime", "Guard", "Teardown"];

	// A product type whose last word the vocabulary retires, keyed by the type name.
	private static readonly AllowList s_retiredTypes = new(
		"retired role words",
		("TolerantHeaderDelimitedMessageHandler", "The type derives from a framework type and keeps the framework's word.")
	);

	// A [Flags] enum with a singular name, keyed by the enum name.
	private static readonly AllowList s_flagsEnums = new("flags enums");

	// The suffixes of a wire structure. An enum, an identifier structure and a root name of the specification keep the bare name.
	private static readonly string[] s_wireSuffixes = ["Params", "Payload", "Response", "Capability"];

	// A type of the Client wire model that is no enum, no serializer converter and takes no wire suffix, keyed by the type name.
	private static readonly AllowList s_wireNames = new(
		"wire names",
		("LspMethodNames", "Names the methods of the LSP specification and is no wire structure."),
		("DocumentUriValidation", "A wire helper that validates document URIs, and no wire structure."),
		("JsonElementReader", "A wire helper that the converters call, and no wire structure."),
		("MarkupContentReader", "A wire helper that the converters call, and no wire structure."),
		("WorkspaceEditPayloadCloner", "A wire helper that the converters call, and no wire structure."),
		("OptionalVersionedTextDocumentIdentifier", "An identifier structure, which keeps the bare name of the specification."),
		("ServerCapabilities", "A root name of the specification, which keeps the bare name."),
		("TextDocumentIdentifier", "An identifier structure, which keeps the bare name of the specification."),
		("VersionedTextDocumentIdentifier", "An identifier structure, which keeps the bare name of the specification."),
		("WorkspaceFolder", "A root name of the specification, which keeps the bare name.")
	);

	// A library type whose name ends in a suffix that the vocabulary restricts to one meaning, keyed by the type name.
	private static readonly AllowList s_roleTypes = new(
		"role-suffix types",
		("AutoClosingService", "Applies the Core auto-closing operation to an engine editor."),
		("ClientLifetime", "Owns the cancellation token and the disposed state of one client."),
		("ClientTeardown", "Runs the ordered shutdown of the collaborators of one client."),
		("CompletionRequestGate", "Admits a completion decision only while its request generation is current."),

		(
			"DeleteLeaseCoordinator",
			"Keeps the delete leases of several views consistent through entry, rollback and release, and decides which guards an exit path still has to release."
		),

		(
			"DiagnosticsPullCoordinator",
			"Sequences the pull request, the result id and the report mapping of one document and decides that the newest report applies."
		),

		("DisposalGate", "Admits the member calls of an adapter until its disposal is claimed, and translates a failure that disposal caused."),

		(
			"DocumentRenameCoordinator",
			"Keeps the tracked record, the pending close and the server copy of a moved document consistent and decides what a failed send leaves behind."
		),

		("DocumentFormatterService", "Applies formatted document changes to an engine editor."),
		("FormattingService", "Applies a formatting provider's edits to an engine editor."),
		("FileReloadCoordinator", "Keeps the queued file changes consistent and decides the reload or conflict path."),
		("ChildProcessLifetimeGuard", "The operating-system guard that ends child processes with their parent."),
		("KeyBindingService", "The host-facing facade of the key bindings, which holds their runtime state."),
		("KeyRepeatGate", "Admits the press of a key once while the key is held, and forgets the key on its release."),
		("LatestRequestGate", "Admits the newest request of one slot, cancels the superseded one and lets only the newest request publish."),
		("LineCommentService", "Applies Core line-comment edits to an engine editor."),
		("LineStatusInvalidationGate", "Coalesces the visual invalidation of a line-status margin."),
		("NoOpChildProcessLifetimeGuard", "The guard on a platform without an operating-system process guard."),
		("OperationGate", "Admits operations on tracked documents under the store's lock, holding each one until it completes."),
		("OperationReentrancyGate", "Admits a scheduler call only from outside a queued operation of the same flow."),

		(
			"PerDocumentRequestCoordinator",
			"Keeps the superseding gate, the concurrency bound and the failure policy of one feature's per-document requests consistent and decides that the newest request for a path applies."
		),

		("KeyedLatestRequestGate", "Supersedes the earlier request for a key and rejects every request once it is closed."),
		("PlatformChildProcessGuard", "Selects the operating-system guard for child processes."),
		("PublicationGate", "Holds a text view's publication back while an earlier one awaits its acknowledgment."),
		("QuickPickAcceptGate", "Holds the accept of a provider back until the earlier accept has ended."),

		(
			"ProviderLifecycleCoordinator",
			"Keeps the startup state, the provider events and the tracked documents consistent through start and restart, and decides which start applies."
		),

		(
			"ProviderLifetime",
			"Owns the cancellation token and the disposed state of a provider's teardown, and releases the callers that wait for it."
		),

		("RedrawGate", "Coalesces queued redraws with an interlocked flag."),
		("RequestLifetime", "Owns the cancellation token and the disposed state of a feature's requests."),
		("SessionTeardown", "Runs the ordered shutdown stages of a connection session."),

		(
			"StartupCoordinator",
			"Sequences the connect, activation and handshake collaborators of one startup attempt and decides how the attempt ended."
		),

		(
			"StoreTeardown",
			"Runs the ordered shutdown of a workspace document store: it stops admitting work, cancels what is in flight and releases the tracked state."
		),

		(
			"CoordinatorTeardown",
			"Runs the ordered shutdown of a view coordinator: it rejects new operations, waits for the active ones and closes the registered views."
		),

		(
			"StopGate",
			"Admits the flow of an operation and refuses a stop that the operation itself requests, unless it is built for a stop that does not wait."
		),

		("StoreLifetime", "Owns the cancellation that every operation of a store observes until the store is disposed."),
		("WorkspaceViewCoordinator", "Keeps the views attached to one document consistent and decides which view applies an edit.")
	);

	// The stems a public type outside the Client wire model may begin with after "Text" or "IText": text is the subject, or the
	// type wraps, extends or adapts an engine type, or carries a brand. A type with any other prefixed name drops the prefix.
	private static readonly AllowList s_textStems = new(
		"Text stems",
		("TextArea", "Begins with the engine's TextArea, which the type extends or adapts."),
		("TextCharSource", "Text is the subject: a source of characters."),
		("TextDocumentEdit", "The LSP name of the edits to one text document."),
		("TextDocumentExtensions", "Extends the engine's TextDocument."),
		("TextDocumentSnapshot", "Wraps the engine's TextDocument."),
		("TextEdit", "Text is the subject: an edit of text."),
		("TextEditor", "Begins with the engine's TextEditor, which the type extends or adapts."),
		("TextEncoding", "Describes the encoding of a text file."),
		("TextFileFormat", "Describes the format of a text file."),
		("TextIncrementalEdit", "Text is the subject: an edit of text computed as a difference."),
		("TextLine", "Text is the subject: a line of text."),
		("TextMatchProbe", "Text is the subject: matching text at an offset."),
		("TextMate", "A brand: the TextMate grammar engine."),
		("TextOffsetValidation", "Text is the subject: the validation of an offset in text."),
		("TextPosition", "Text is the subject: a line and character position in text."),
		("TextRange", "Text is the subject: an offset span in text."),
		("TextRunStyle", "Begins with the engine's text run, whose style the type describes."),
		("TextSnapshot", "Text is the subject: an immutable snapshot of text.")
	);

	// A public type whose name begins with "Lsp" names the protocol as a whole, never a server, a session or a library part.
	private static readonly AllowList s_lspTypes = new(
		"Lsp types",
		("LspMethodNames", "Names the methods of the LSP specification, which is the protocol as a whole.")
	);

	[TestMethod]
	public void NoProductTypeEndsInARetiredWord()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (BaseTypeDeclarationSyntax type in LibraryNodes().OfType<BaseTypeDeclarationSyntax>())
		{
			string name = type.Identifier.ValueText;
			string last = VocabularyTable.LastWord(name);

			// A delegate named ...Handler is allowed, and a delegate is no type declaration, so only a class, struct, interface or record
			// named ...Handler is retired.
			if (!VocabularyTable.RetiredWords.Contains(last, StringComparer.Ordinal) && last != "Handler")
				continue;

			if (!s_retiredTypes.Allows(name))
			{
				violations.Add(
					s_retiredTypes.Rejection(
						$"{name} ends in the retired word {last}. Name it for what it does, with a last word from the vocabulary table of CONVENTIONS.md.",
						Guard
					)
				);
			}
		}

		foreach (string message in s_retiredTypes.UnmatchedMessages(Guard))
			violations.Add(message);

		Assert.IsEmpty(
			violations,
			"Retired role words:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void ATryMemberReturnsBool()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (MethodDeclarationSyntax method in LibraryNodes().OfType<MethodDeclarationSyntax>())
		{
			string name = method.Identifier.ValueText;

			if (!name.StartsWith("Try", StringComparison.Ordinal) || name.Length == 3 || !char.IsUpper(name[3]))
				continue;

			string returnType = method.ReturnType.ToString();

			if (returnType is "bool" or "Task<bool>" or "ValueTask<bool>")
				continue;

			violations.Add(
				$"{DeclaringTypeName(method)}.{name} returns {returnType}. A Try member returns bool and its value through out; a member that returns a result object takes no Try prefix."
			);
		}

		Assert.IsEmpty(violations, "Try members:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
	}

	[TestMethod]
	public void AFlagsEnumTakesAPluralName()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (EnumDeclarationSyntax declaration in LibraryNodes().OfType<EnumDeclarationSyntax>())
		{
			bool isFlags = declaration
				.AttributeLists.SelectMany(list => list.Attributes)
				.Any(attribute => attribute.Name.ToString() is "Flags" or "System.Flags" or "FlagsAttribute");

			string name = declaration.Identifier.ValueText;

			if (!isFlags || name.EndsWith('s') || s_flagsEnums.Allows(name))
				continue;

			violations.Add(s_flagsEnums.Rejection($"{name} is a [Flags] enum with a singular name. A flags enum takes a plural noun.", Guard));
		}

		foreach (string message in s_flagsEnums.UnmatchedMessages(Guard))
			violations.Add(message);

		Assert.IsEmpty(violations, "Flags enums:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
	}

	// A non-flags enum whose last word is none of the classifying words, keyed by the enum name.
	private static readonly AllowList s_enumNames = new(
		"enum names",
		("AbortReason", "Names the property the value sets: why an operation stopped."),
		("Claim", "Names the property the value sets: whether a view or its id is already registered."),
		("CompletionInsertTextFormat", "The LSP names this enumeration, and the model keeps the specification name."),
		("CompletionItemTag", "The LSP names this enumeration, and the model keeps the specification name."),
		("DiagnosticTag", "The LSP names this enumeration, and the model keeps the specification name."),
		("FileChangeType", "The LSP names this enumeration, and the model keeps the specification name."),
		("InsertTextFormat", "The LSP names this enumeration, and the model keeps the specification name."),
		("MessageType", "The LSP names this enumeration, and the model keeps the specification name."),
		("SymbolTag", "The LSP names this enumeration, and the model keeps the specification name."),
		("JobObjectInformationClass", "The Win32 name of the P/Invoke enumeration."),
		("JsonSchemaPropertyType", "Names the property the value sets: the JSON type of a schema property."),
		("KeyCode", "The key code enumeration, named as the toolkits name theirs."),
		("LanguageServerFeatureProviderState", "A State word of the table: the condition of one owner."),
		("MarkdownColumnAlignment", "Names the property the value sets: the alignment of a table column."),
		("MarkdownFlowDirection", "Names the property the value sets: the flow direction of the rendered text."),
		("RoslynLanguage", "Names the property the value sets: the language a Roslyn profile serves."),
		("SampleSaveDecision", "A Decision word of the table: the window's answer to the workbench's question about unsaved changes."),
		("StringState", "A State word of the table: the condition of one scanner."),
		("UiDispatcherPriority", "Names the property the value sets: the priority of a dispatched call.")
	);

	[TestMethod]
	public void AnEnumTakesAClassifyingLastWord()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);
		string[] words = ["Kind", "Mode", "Style", "Outcome", "Policy", "Severity"];

		foreach (EnumDeclarationSyntax declaration in LibraryNodes().OfType<EnumDeclarationSyntax>())
		{
			bool isFlags = declaration
				.AttributeLists.SelectMany(list => list.Attributes)
				.Any(attribute => attribute.Name.ToString() is "Flags" or "System.Flags" or "FlagsAttribute");

			string name = declaration.Identifier.ValueText;

			if (isFlags || words.Contains(VocabularyTable.LastWord(name), StringComparer.Ordinal) || s_enumNames.Allows(name))
				continue;

			violations.Add(
				s_enumNames.Rejection(
					$"{name} is an enum whose last word classifies nothing. End it in Kind, Mode, Style, Outcome, Policy or Severity.",
					Guard
				)
			);
		}

		foreach (string message in s_enumNames.UnmatchedMessages(Guard))
			violations.Add(message);

		Assert.IsEmpty(violations, "Enum names:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
	}

	[TestMethod]
	public void ATypeWithARestrictedRoleSuffixIsListed()
	{
		List<string> violations = [];

		// An interface is listed through the class it describes: IKeyBindingService is covered by the row of KeyBindingService.
		IEnumerable<string> stems = LibraryTypeNames()
			.Select(name => name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) ? name[1..] : name)
			.Distinct(StringComparer.Ordinal);

		foreach (string name in stems.Where(name => s_restrictedSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal))))
		{
			if (!s_roleTypes.Allows(name))
				violations.Add(s_roleTypes.Rejection($"{name} ends in a suffix that the vocabulary restricts to one meaning.", Guard));
		}

		violations.AddRange(s_roleTypes.UnmatchedMessages(Guard));

		Assert.IsEmpty(violations, "Role-suffix types:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AWireTypeTakesAWireSuffixUnlessItIsAnEnumAConverterOrListed()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);
		string suffixes = string.Join(", ", s_wireSuffixes[..^1]) + " or " + s_wireSuffixes[^1];

		IEnumerable<LibraryType> wireTypes = LibraryTypes()
			.Where(type =>
				type.Project == ClientProject
				&& type.Namespace == WireNamespace
				&& type.IsTopLevel
				&& !type.IsEnum
				&& !type.Name.EndsWith("JsonConverter", StringComparison.Ordinal)
			);

		foreach (LibraryType type in wireTypes)
		{
			if (s_wireSuffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal)) || s_wireNames.Allows(type.Name))
				continue;

			violations.Add(
				s_wireNames.Rejection(
					$"{type.Name} is a wire type that does not end in {suffixes}. A wire structure takes the name of the specification and one suffix.",
					Guard
				)
			);
		}

		foreach (string message in s_wireNames.UnmatchedMessages(Guard))
			violations.Add(message);

		Assert.IsEmpty(
			violations,
			"Wire type names:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void NoEnumIsNamedResult()
	{
		List<string> violations =
		[
			.. LibraryNodes()
				.OfType<EnumDeclarationSyntax>()
				.Where(declaration => declaration.Identifier.ValueText.EndsWith("Result", StringComparison.Ordinal))
				.Select(declaration =>
					$"{declaration.SyntaxTree.FilePath}: {declaration.Identifier.ValueText} is an enum named ...Result. A ...Result is an envelope around a payload; an enum that classifies what happened is an ...Outcome."
				)
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal),
		];

		Assert.IsEmpty(violations, "Result enums:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AnEventIsAnEventHandler()
	{
		List<string> violations =
		[
			.. LibraryEvents()
				.Where(libraryEvent => !IsEventHandler(libraryEvent.Type))
				.Select(libraryEvent =>
					$"{libraryEvent.Path}: {libraryEvent.Name} is typed {libraryEvent.Type}. An event is an EventHandler or an EventHandler<TEventArgs>, never an Action or a custom delegate."
				)
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal),
		];

		Assert.IsEmpty(violations, "Event types:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void AnEventPayloadIsNamedAfterItsEvent()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (LibraryEvent libraryEvent in LibraryEvents())
		{
			if (
				SimpleName(libraryEvent.Type)
				is not GenericNameSyntax { Identifier.ValueText: "EventHandler", TypeArgumentList.Arguments: [var payload] }
			)
			{
				continue;
			}

			string payloadName = SimpleName(payload)?.Identifier.ValueText ?? payload.ToString();
			string expected = libraryEvent.EventName + "EventArgs";

			if (!string.Equals(payloadName, expected, StringComparison.Ordinal))
			{
				violations.Add(
					$"{libraryEvent.Path}: {libraryEvent.Name} carries {payloadName}. The payload of an EventHandler<T> event is named {{EventName}}EventArgs, so name it {expected}."
				);
			}
		}

		Assert.IsEmpty(
			violations,
			"Event payloads:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void NoPublicTypeLivesInAnInfrastructureNamespace()
	{
		string root = RepositoryPaths.Root();
		List<string> violations = [];

		foreach (
			string baseline in new[] { "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt" }
				.SelectMany(name => RepositoryModel.EnumerateFiles(Path.Combine(root, "src"), name))
				.Order(StringComparer.Ordinal)
		)
		{
			foreach (string line in File.ReadLines(baseline))
			{
				// A type declaration is the one baseline line without a space: members carry a signature, a value or a modifier.
				if (line.Length == 0 || line[0] == '#' || line.Contains(' ', StringComparison.Ordinal))
					continue;

				string[] segments = line.Split('.');

				if (segments[..^1].Contains("Infrastructure", StringComparer.Ordinal))
				{
					violations.Add(
						$"{Path.GetRelativePath(root, baseline).Replace('\\', '/')}: {line} is public in an Infrastructure namespace. Move it to the namespace of its feature, or make it internal."
					);
				}
			}
		}

		Assert.IsEmpty(violations, "Public Infrastructure types:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[TestMethod]
	public void APublicTypeBeginsWithTextOnlyWhereTextIsTheSubject()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (LibraryType type in LibraryTypes().Where(type => type.IsPublic && type.Project != ClientProject && IsTextPrefixed(type.Name)))
		{
			string stem = type.Name.StartsWith("IText", StringComparison.Ordinal) ? type.Name[1..] : type.Name;

			string? entry = s_textStems
				.Entries.Where(candidate => stem.StartsWith(candidate, StringComparison.Ordinal))
				.MaxBy(candidate => candidate.Length);

			if (entry is null || !s_textStems.Allows(entry))
			{
				violations.Add(
					s_textStems.Rejection(
						$"{type.Name} begins with Text, so it names text or an engine type or a brand. Drop the prefix and name the concept, as the LSP does.",
						Guard
					)
				);
			}
		}

		foreach (string message in s_textStems.UnmatchedMessages(Guard))
			violations.Add(message);

		Assert.IsEmpty(
			violations,
			"Text-prefixed types:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void APublicTypeBeginsWithLspOnlyWhenItNamesTheProtocolAsAWhole()
	{
		HashSet<string> violations = new(StringComparer.Ordinal);

		foreach (
			string name in LibraryTypes()
				.Where(type => type.IsPublic && IsPrefixed(type.Name, "Lsp"))
				.Select(type => type.Name)
				.Distinct(StringComparer.Ordinal)
		)
		{
			if (!s_lspTypes.Allows(name))
			{
				violations.Add(
					s_lspTypes.Rejection(
						$"{name} begins with Lsp, which names the protocol as a whole. A type about the server, its process or a session with it takes LanguageServer.",
						Guard
					)
				);
			}
		}

		foreach (string message in s_lspTypes.UnmatchedMessages(Guard))
			violations.Add(message);

		Assert.IsEmpty(
			violations,
			"Lsp-prefixed types:" + Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal))
		);
	}

	[TestMethod]
	public void NoTypeTakesTheNameOfItsNamespace()
	{
		List<string> violations =
		[
			.. LibraryTypes()
				.Where(type => type.IsTopLevel && type.Namespace is not null && type.Namespace.Split('.')[^1] == type.Name)
				.Select(type =>
					$"{type.Namespace}.{type.Name} takes the name of its namespace, so a sibling namespace sees the namespace and not the type. Name what the type holds, such as an ...Information type for a feature's answer."
				)
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal),
		];

		Assert.IsEmpty(violations, "Types named like their namespace:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	private static bool IsTextPrefixed(string name) => IsPrefixed(name, "Text") || (name.StartsWith('I') && IsPrefixed(name[1..], "Text"));

	private static bool IsPrefixed(string name, string prefix) =>
		name.StartsWith(prefix, StringComparison.Ordinal) && (name.Length == prefix.Length || char.IsUpper(name[prefix.Length]));

	private static bool IsEventHandler(TypeSyntax type)
	{
		return SimpleName(type)
			is IdentifierNameSyntax { Identifier.ValueText: "EventHandler" }
				or GenericNameSyntax { Identifier.ValueText: "EventHandler", TypeArgumentList.Arguments.Count: 1 };
	}

	/// <summary>
	/// The last name of a possibly qualified or nullable type, such as <c>EventHandler&lt;X&gt;</c> from <c>System.EventHandler&lt;X&gt;?</c>
	/// or <c>global::System.EventHandler&lt;X&gt;</c>.
	/// </summary>
	private static SimpleNameSyntax? SimpleName(TypeSyntax type)
	{
		return type switch
		{
			NullableTypeSyntax nullable => SimpleName(nullable.ElementType),
			QualifiedNameSyntax qualified => qualified.Right,
			AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name,
			SimpleNameSyntax simple => simple,
			_ => null,
		};
	}

	/// <summary>The events the library projects declare, as a field-like event or one with accessors.</summary>
	private static IEnumerable<LibraryEvent> LibraryEvents()
	{
		foreach (SyntaxNode node in LibraryNodes())
		{
			switch (node)
			{
				case EventFieldDeclarationSyntax field:
					foreach (VariableDeclaratorSyntax variable in field.Declaration.Variables)
					{
						yield return new LibraryEvent(
							Path: node.SyntaxTree.FilePath,
							Owner: DeclaringTypeName(node),
							EventName: variable.Identifier.ValueText,
							Type: field.Declaration.Type
						);
					}

					break;

				case EventDeclarationSyntax declaration:
					yield return new LibraryEvent(
						Path: node.SyntaxTree.FilePath,
						Owner: DeclaringTypeName(node),
						EventName: declaration.Identifier.ValueText,
						Type: declaration.Type
					);

					break;
			}
		}
	}

	private static string DeclaringTypeName(SyntaxNode node)
	{
		BaseTypeDeclarationSyntax? owner = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
		return owner?.Identifier.ValueText ?? string.Empty;
	}

	/// <summary>
	/// The names of the types the library projects declare, with the files they link, reading every branch of each file's conditional blocks.
	/// </summary>
	private static IEnumerable<string> LibraryTypeNames()
	{
		HashSet<string> names = new(StringComparer.Ordinal);

		foreach (SyntaxNode node in LibraryNodes())
		{
			switch (node)
			{
				case BaseTypeDeclarationSyntax type:
					names.Add(type.Identifier.ValueText);
					break;

				case DelegateDeclarationSyntax @delegate:
					names.Add(@delegate.Identifier.ValueText);
					break;
			}
		}

		return names.Order(StringComparer.Ordinal);
	}

	/// <summary>
	/// The types the library projects declare, with the project that compiles them, reading every branch of each file's conditional blocks.
	/// </summary>
	private static IEnumerable<LibraryType> LibraryTypes()
	{
		foreach (ProjectFile project in RepositoryModel.SourceProjects.Concat(RepositoryModel.SampleProjects))
		{
			foreach (string path in project.Sources)
			{
				foreach (SyntaxTree tree in RepositoryModel.Source(path).TreesOfEveryBranch(project.DefineConstants))
				{
					foreach (BaseTypeDeclarationSyntax type in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
					{
						yield return new LibraryType(
							Project: project.Name,
							Name: type.Identifier.ValueText,
							Namespace: type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString(),
							IsPublic: type.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)),
							IsTopLevel: type.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax,
							IsEnum: type is EnumDeclarationSyntax
						);
					}
				}
			}
		}
	}

	/// <summary>Every syntax node of the files the library projects compile, reading every branch of each file's conditional blocks.</summary>
	private static IEnumerable<SyntaxNode> LibraryNodes()
	{
		foreach (ProjectFile project in RepositoryModel.SourceProjects.Concat(RepositoryModel.SampleProjects))
		{
			foreach (string path in project.Sources)
			{
				foreach (SyntaxTree tree in RepositoryModel.Source(path).TreesOfEveryBranch(project.DefineConstants))
				{
					foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
						yield return node;
				}
			}
		}
	}

	/// <summary>A type a library project declares, with the namespace that holds it.</summary>
	private readonly record struct LibraryType(string Project, string Name, string? Namespace, bool IsPublic, bool IsTopLevel, bool IsEnum);

	/// <summary>An event of a library type, with the file that declares it.</summary>
	private readonly record struct LibraryEvent(string Path, string Owner, string EventName, TypeSyntax Type)
	{
		/// <summary>The event as a reader names it, <c>Owner.EventName</c>.</summary>
		internal string Name => Owner.Length == 0 ? EventName : Owner + "." + EventName;
	}
}
