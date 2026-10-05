using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

public sealed partial class CompletionControllerTests
{
	// The request offset is the end of "string.", where a server names the members of the table.
	private const string MemberAccess = "string.";

	[TestMethod]
	public void RequestAsync_WordTypedWhileTheAnswerWasPending_PresentsTheDecisionOnTheTypedWord()
	{
		using var host = new LateAnswerHost();

		host.Host.Type("re");

		Assert.IsTrue(host.Answer());
		Assert.HasCount(1, host.Host.Presenter.Applied);

		// The decision spans the typed word, so the list filters by it, as a list opened before the typing would.
		Assert.AreEqual(new TextRange(MemberAccess.Length, 2), host.Host.Presenter.Applied[0].ReplacementRange);
		Assert.HasCount(3, host.Host.Presenter.Applied[0].Items!);
	}

	[TestMethod]
	public void RequestAsync_NothingTypedWhileTheAnswerWasPending_PresentsTheDecisionAsRequested()
	{
		using var host = new LateAnswerHost();

		Assert.IsTrue(host.Answer());
		Assert.AreEqual(new TextRange(MemberAccess.Length, 0), host.Host.Presenter.Applied.Single().ReplacementRange);
		Assert.HasCount(3, host.Host.Presenter.Applied[0].Items!);
	}

	[TestMethod]
	public void RequestAsync_SpaceTypedWhileTheAnswerWasPending_DiscardsTheAnswer()
	{
		using var host = new LateAnswerHost();

		host.Host.Type(" ");

		Assert.IsFalse(host.Answer());
		Assert.IsEmpty(host.Host.Presenter.Applied);
	}

	[TestMethod]
	public void RequestAsync_WordFollowedBySpaceWhileTheAnswerWasPending_DiscardsTheAnswer()
	{
		using var host = new LateAnswerHost();

		host.Host.Type("re ");

		Assert.IsFalse(host.Answer());
		Assert.IsEmpty(host.Host.Presenter.Applied);
	}

	[TestMethod]
	public void RequestAsync_CaretMovedAwayWhileTheAnswerWasPending_DiscardsTheAnswer()
	{
		using var host = new LateAnswerHost();

		host.Host.CaretOffset = 0;

		Assert.IsFalse(host.Answer());
		Assert.IsEmpty(host.Host.Presenter.Applied);
	}

	[TestMethod]
	public void RequestAsync_CaretMovedBackIntoTheWordWhileTheAnswerWasPending_DiscardsTheAnswer()
	{
		using var host = new LateAnswerHost();

		host.Host.Type("re");
		host.Host.CaretOffset = MemberAccess.Length - 1;

		Assert.IsFalse(host.Answer());
		Assert.IsEmpty(host.Host.Presenter.Applied);
	}

	[TestMethod]
	public void RequestAsync_TextEditedBeforeTheWordWhileTheAnswerWasPending_DiscardsTheAnswer()
	{
		using var host = new LateAnswerHost();

		host.Host.Text = "--" + host.Host.Text;
		host.Host.CaretOffset = MemberAccess.Length + 2;

		Assert.IsFalse(host.Answer());
		Assert.IsEmpty(host.Host.Presenter.Applied);
	}

	[TestMethod]
	public void RequestAsync_EditorLostItsDocumentWhileTheAnswerWasPending_PresentsTheDecisionAsRequested()
	{
		using var host = new LateAnswerHost();

		// Without a document there is no word to move the answer onto, so the answer stands as the request made it.
		host.Host.HasDocument = false;

		Assert.IsTrue(host.Answer());
		Assert.AreEqual(new TextRange(MemberAccess.Length, 0), host.Host.Presenter.Applied.Single().ReplacementRange);
	}

	/// <summary>A controller whose first request is held back, so the test types or moves the caret before the provider answers.</summary>
	private sealed class LateAnswerHost : IDisposable
	{
		private readonly CompletionController _controller;
		private readonly CompletionControllerTestHost.GatedAnswer _answer = new("byte", "rep", "find");
		private readonly Task<bool> _request;

		internal LateAnswerHost()
		{
			// A long debounce keeps a request that typing schedules from superseding the pending one.
			Host = new CompletionControllerTestHost(MemberAccess)
			{
				Options = CompletionControllerOptions.Default with { RequestDebounceDelay = TimeSpan.FromHours(1.0) },
			};

			Host.Provider.Answer = _answer.AnswerAsync;
			_controller = Host.CreateController();
			_request = _controller.RequestAsync();

			Host.Dispatcher.PumpUntil(() => _answer.Started.IsCompleted);
		}

		internal CompletionControllerTestHost Host { get; }

		/// <summary>Lets the provider answer and waits until the controller has applied or discarded the answer.</summary>
		/// <returns>Whether the answer was presented.</returns>
		internal bool Answer()
		{
			_answer.Release();
			return Host.RunToCompletion(_request);
		}

		public void Dispose()
		{
			_answer.Release();

			_controller.Dispose();
			Host.Dispose();
		}
	}
}
