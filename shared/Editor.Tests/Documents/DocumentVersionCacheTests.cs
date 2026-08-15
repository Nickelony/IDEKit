#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[TestClass]
public sealed class DocumentVersionCacheTests
{
	[TestMethod]
	public void GetOrCreate_SameDocumentAndVersion_ReturnsCachedValueAndBuildsOnce()
	{
		var document = new TextDocument("one");
		var cache = new DocumentVersionCache<object>();
		TextDocument? factoryDocument = null;
		int buildCount = 0;

		object first = cache.GetOrCreate(document, source =>
		{
			buildCount++;
			factoryDocument = source;
			return new object();
		});

		object second = cache.GetOrCreate(document, _ =>
		{
			buildCount++;
			return new object();
		});

		Assert.AreSame(first, second);
		Assert.AreEqual(1, buildCount);
		Assert.AreSame(document, factoryDocument);
	}

	[TestMethod]
	public void GetOrCreate_FactoryMutatesDocument_ReturnsValueWithoutCachingIt()
	{
		var document = new TextDocument("one");
		var cache = new DocumentVersionCache<object>();
		int buildCount = 0;

		// The factory edits the document, so the value it produced describes text the mutated document
		// no longer holds. The value is returned but not cached, because a cached value would describe
		// stale text forever.
		object first = cache.GetOrCreate(document, source =>
		{
			buildCount++;
			source.Insert(0, "x");
			return new object();
		});

		Assert.AreEqual(1, buildCount);
		Assert.AreEqual("xone", document.Text);

		// The next request recomputes instead of serving the stale-tagged value.
		object second = cache.GetOrCreate(document, _ =>
		{
			buildCount++;
			return new object();
		});

		Assert.AreNotSame(first, second);
		Assert.AreEqual(2, buildCount);

		// A factory that leaves the document alone caches its value again.
		Assert.AreSame(second, cache.GetOrCreate(document, _ => new object()));
		Assert.AreEqual(2, buildCount);
	}

	[TestMethod]
	public void GetOrCreate_AfterDocumentChange_RebuildsValue()
	{
		var document = new TextDocument("one");
		var cache = new DocumentVersionCache<object>();

		object first = cache.GetOrCreate(document, _ => new object());

		document.Insert(0, "x");

		object second = cache.GetOrCreate(document, _ => new object());

		Assert.AreNotSame(first, second);

		// The rebuilt value is cached again while the document stays unchanged.
		Assert.AreSame(second, cache.GetOrCreate(document, _ => new object()));
	}

	[TestMethod]
	public void GetOrCreate_AfterUndo_RebuildsValue()
	{
		var document = new TextDocument("one");
		var cache = new DocumentVersionCache<object>();

		object first = cache.GetOrCreate(document, _ => new object());

		document.Insert(0, "x");
		document.UndoStack.Undo();

		// The text is identical again, but the edits replaced the version object,
		// so the next request rebuilds instead of trusting the restored text.
		object second = cache.GetOrCreate(document, _ => new object());

		Assert.AreNotSame(first, second);
	}

	[TestMethod]
	public void GetOrCreate_RevisionChangedDuringFactory_RebuildsValue()
	{
		var document = new TextDocument("one");
		var cache = new DocumentVersionCache<object>();
		long revision = 0;

		// The captured revision (0) is passed before the factory runs and the writer bumps the epoch
		// while the factory runs, so the value is stored with a stale revision on purpose.
		object first = cache.GetOrCreate(document, _ =>
		{
			revision++;
			return new object();
		}, revision);

		// The next request captures the bumped revision, so the stale-tagged value is not served.
		object second = cache.GetOrCreate(document, _ => new object(), revision);

		Assert.AreNotSame(first, second);

		// Once the revision is stable again, the rebuilt value is cached.
		Assert.AreSame(second, cache.GetOrCreate(document, _ => new object(), revision));
	}

	[TestMethod]
	public void GetOrCreate_FactoryThrows_NextCallRetriesAndCachesValue()
	{
		var document = new TextDocument("one");
		var cache = new DocumentVersionCache<object>();

		Assert.ThrowsExactly<InvalidOperationException>(() =>
			cache.GetOrCreate(document, _ => throw new InvalidOperationException("Factory failed.")));

		// The failed factory produced no value to cache, so the next call retries and caches.
		object value = cache.GetOrCreate(document, _ => new object());

		Assert.AreSame(value, cache.GetOrCreate(document, _ => new object()));
	}

	[TestMethod]
	public void GetOrCreate_DifferentDocument_RebuildsValue()
	{
		var firstDocument = new TextDocument("same");
		var secondDocument = new TextDocument("same");
		var cache = new DocumentVersionCache<object>();

		object first = cache.GetOrCreate(firstDocument, _ => new object());
		object second = cache.GetOrCreate(secondDocument, _ => new object());

		Assert.AreNotSame(first, second);
	}

	[TestMethod]
	public void Invalidate_NextRequestRebuildsValue()
	{
		var document = new TextDocument("one");
		var cache = new DocumentVersionCache<object>();

		object first = cache.GetOrCreate(document, _ => new object());

		cache.Invalidate();

		object second = cache.GetOrCreate(document, _ => new object());

		Assert.AreNotSame(first, second);
	}

	[TestMethod]
	public void EditorVersion_IsReplacedOnEveryChange()
	{
		var document = new TextDocument("one");
		ITextSourceVersion before = document.Version;

		document.Insert(0, "x");

		// DocumentVersionCache compares version references instead of version numbers, so this test pins
		// the editor behavior the cache relies on and fails loudly if a version object ever mutates.
		Assert.AreNotSame(before, document.Version);
	}
}
