using System.IO.Pipelines;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class OutboundStreamForwarderTests
{
	[TestMethod]
	public async Task Start_CopiesEveryWrittenByteAndLeavesTheStreamOpen()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		using var writeStream = new MemoryStream();

		var forwarder = new OutboundStreamForwarder(writeStream, connectionGeneration: 1, logger);
		Task copy = forwarder.Start();

		await forwarder.Writer.WriteAsync(Encoding.UTF8.GetBytes("first "));
		await forwarder.Writer.WriteAsync(Encoding.UTF8.GetBytes("second"));

		await forwarder.Writer.CompleteAsync();

		await copy.WaitAsync(TestPolling.DefaultTimeout);

		Assert.AreEqual("first second", Encoding.UTF8.GetString(writeStream.ToArray()));
		Assert.IsTrue(writeStream.CanWrite);
		Assert.IsEmpty(logger.Logs);
	}

	[TestMethod]
	public async Task Start_WhenAWriteFailsAndTheLoggerThrows_StillCompletesThePipeWithTheWriteFailure()
	{
		var writeStream = new MemoryStream();

		// A disposed stream fails every write.
		writeStream.Dispose();

		var forwarder = new OutboundStreamForwarder(writeStream, connectionGeneration: 1, new ThrowingLogger());
		Task copy = forwarder.Start();

		await forwarder.Writer.WriteAsync(Encoding.UTF8.GetBytes("first"));

		// The copy reports the failed write through the throwing logger, so its task faults with the logger's exception.
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => copy.WaitAsync(TestPolling.DefaultTimeout));

		// The pipe completed before the log, so the writer's next flush throws the write failure.
		await Assert.ThrowsAsync<ObjectDisposedException>(async () => await forwarder.Writer.WriteAsync(Encoding.UTF8.GetBytes("second")));
	}
}
