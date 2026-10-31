using Bonsai.Harp;

namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Sends commands to a Harp device over a stream and matches each command to its reply.
/// </summary>
/// <remarks>
/// Every message the device sends, including replies, is also reported by <see cref="MessageReceived"/>.
/// The connection does not own the stream: close the port after disposing the connection.
/// </remarks>
public sealed class HarpConnection : IAsyncDisposable
{
    readonly Stream stream;
    readonly HarpMessageReader reader;
    readonly CancellationTokenSource readCancellation = new();
    readonly List<PendingReply> pending = [];
    readonly object gate = new();
    Exception? failure;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarpConnection"/> class and starts reading messages.
    /// </summary>
    /// <param name="stream">The stream used to communicate with the device.</param>
    public HarpConnection(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        reader = new HarpMessageReader(stream);
        Completion = ReadLoopAsync(readCancellation.Token);
    }

    /// <summary>
    /// Occurs when the device sends a message.
    /// </summary>
    public event Action<HarpMessage>? MessageReceived;

    /// <summary>
    /// Occurs when the connection stops reading, with the error that stopped it, if any.
    /// </summary>
    public event Action<Exception?>? Closed;

    /// <summary>
    /// Gets or sets the time to wait for the device to reply to a command.
    /// </summary>
    public TimeSpan ReplyTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the task that completes when the connection stops reading messages.
    /// </summary>
    public Task Completion { get; }

    /// <summary>
    /// Gets the number of received bytes that did not form a valid message.
    /// </summary>
    public long DiscardedBytes => reader.DiscardedBytes;

    /// <summary>
    /// Asynchronously sends a command and waits for the device to reply.
    /// </summary>
    /// <param name="command">The read or write command to send.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the reply.</param>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="Task{TResult}.Result"/>
    /// property contains the reply.
    /// </returns>
    /// <exception cref="TimeoutException">The device did not reply in time.</exception>
    /// <exception cref="HarpException">The device replied with an error.</exception>
    public async Task<HarpMessage> CommandAsync(HarpMessage command, CancellationToken cancellationToken = default)
    {
        var reply = await SendAsync(command, cancellationToken);
        if (reply.Error)
            throw new HarpException(reply);
        return reply;
    }

    /// <summary>
    /// Asynchronously sends a command and waits for the device to reply, without treating an
    /// error reply as a failure.
    /// </summary>
    /// <param name="command">The read or write command to send.</param>
    /// <param name="cancellationToken">The token that cancels the wait for the reply.</param>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="Task{TResult}.Result"/>
    /// property contains the reply, whose <see cref="HarpMessage.Error"/> property reports whether
    /// the device rejected the command.
    /// </returns>
    /// <exception cref="TimeoutException">The device did not reply in time.</exception>
    public async Task<HarpMessage> SendAsync(HarpMessage command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reply = new PendingReply(command.Address, command.MessageType);
        lock (gate)
        {
            if (failure is not null)
                throw new IOException("The connection to the device is closed.", failure);
            pending.Add(reply);
        }

        try
        {
            await stream.WriteAsync(command.MessageBytes, cancellationToken);
            return await reply.Completion.Task.WaitAsync(ReplyTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"The device did not reply to the {command.MessageType.ToString().ToLowerInvariant()} " +
                $"command for register {command.Address} in time.");
        }
        finally
        {
            lock (gate) pending.Remove(reply);
        }
    }

    /// <summary>
    /// Asynchronously sends a message without waiting for a reply.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public Task WriteAsync(HarpMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return stream.WriteAsync(message.MessageBytes, cancellationToken).AsTask();
    }

    /// <summary>
    /// Asynchronously stops reading messages and fails any command still waiting for a reply.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        readCancellation.Cancel();
        await Completion;
        readCancellation.Dispose();
    }

    async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        Exception? error = null;
        try
        {
            // Let the constructor return before the first read completes.
            await Task.Yield();
            while (true)
            {
                var message = await reader.ReadAsync(cancellationToken);
                if (message is null)
                    break;
                Dispatch(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            error = ex;
        }

        PendingReply[] waiting;
        lock (gate)
        {
            failure = error ?? new ObjectDisposedException(nameof(HarpConnection));
            waiting = pending.ToArray();
            pending.Clear();
        }

        foreach (var reply in waiting)
        {
            reply.Completion.TrySetException(new IOException("The connection to the device closed.", error));
        }
        Closed?.Invoke(error);
    }

    void Dispatch(HarpMessage message)
    {
        if (message.MessageType is MessageType.Read or MessageType.Write)
        {
            PendingReply? reply = null;
            lock (gate)
            {
                var index = pending.FindIndex(item => item.Address == message.Address && item.MessageType == message.MessageType);
                if (index >= 0)
                {
                    reply = pending[index];
                    pending.RemoveAt(index);
                }
            }
            reply?.Completion.TrySetResult(message);
        }

        MessageReceived?.Invoke(message);
    }

    sealed class PendingReply(int address, MessageType messageType)
    {
        public int Address { get; } = address;

        public MessageType MessageType { get; } = messageType;

        public TaskCompletionSource<HarpMessage> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
