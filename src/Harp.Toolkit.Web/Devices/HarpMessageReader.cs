using Bonsai.Harp;

namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Reads Harp messages from a stream, skipping any bytes that do not form a valid message.
/// </summary>
/// <remarks>
/// This is an asynchronous version of the parser in Bonsai.Harp, which reads its serial port
/// synchronously on a dedicated thread and so cannot run in the browser.
/// </remarks>
public sealed class HarpMessageReader
{
    const byte IdMask = 0x03;
    const byte ErrorMask = 0x08;
    const byte TypeLengthMask = 0x0F;
    const int MinLength = 4;

    readonly Stream stream;
    readonly byte[] buffer;
    int start;
    int end;
    bool ended;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarpMessageReader"/> class over the specified stream.
    /// </summary>
    /// <param name="stream">The stream from which to read Harp messages.</param>
    /// <param name="bufferSize">The size, in bytes, of each read from the stream.</param>
    public HarpMessageReader(Stream stream, int bufferSize = 65536)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        buffer = new byte[Math.Max(bufferSize, 512)];
    }

    /// <summary>
    /// Gets the number of bytes discarded because they did not form a valid message.
    /// </summary>
    public long DiscardedBytes { get; private set; }

    /// <summary>
    /// Asynchronously reads the next valid Harp message from the stream.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="ValueTask{TResult}.Result"/>
    /// property contains the message, or <see langword="null"/> if the stream ended.
    /// </returns>
    public async ValueTask<HarpMessage?> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (!await FillAsync(2, cancellationToken))
                return null;

            var type = buffer[start];
            var length = buffer[start + 1];
            if (type == 0 || (type & ~(IdMask | ErrorMask)) != 0 || length < MinLength)
            {
                Discard();
                continue;
            }

            var messageLength = length + 2;
            if (!await FillAsync(messageLength, cancellationToken))
            {
                // The stream ended inside what looked like a message: the header may be noise,
                // so keep scanning the bytes already received.
                Discard();
                continue;
            }

            byte sum = 0;
            for (int i = start; i < start + messageLength - 1; i++)
            {
                sum += buffer[i];
            }

            if (sum != buffer[start + messageLength - 1] || !IsValidPayloadType(buffer[start + 4]))
            {
                Discard();
                continue;
            }

            var messageBytes = buffer.AsSpan(start, messageLength).ToArray();
            start += messageLength;
            return new HarpMessage(messageBytes);
        }
    }

    void Discard()
    {
        start++;
        DiscardedBytes++;
    }

    async ValueTask<bool> FillAsync(int count, CancellationToken cancellationToken)
    {
        while (end - start < count)
        {
            if (start == end)
            {
                start = end = 0;
            }
            else if (start > 0 && buffer.Length - start < count + 1)
            {
                Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                end -= start;
                start = 0;
            }

            if (ended)
                return false;

            var bytesRead = await stream.ReadAsync(buffer.AsMemory(end), cancellationToken);
            if (bytesRead == 0)
            {
                ended = true;
                return false;
            }
            end += bytesRead;
        }

        return true;
    }

    static bool IsValidPayloadType(byte type)
    {
        switch ((byte)(type & TypeLengthMask))
        {
            case (byte)PayloadType.U8:
            case (byte)PayloadType.U16:
            case (byte)PayloadType.U32:
            case (byte)PayloadType.U64:
                break;
            default:
                return false;
        }

        if ((type & 0x20) == 0x20)
            return false;

        if ((type & 0x40) == 0x40 && (type & 0xEF) != (byte)PayloadType.Float)
            return false;

        return true;
    }
}
