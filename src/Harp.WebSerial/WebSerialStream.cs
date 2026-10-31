using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Harp.WebSerial;

/// <summary>
/// Provides a stream over an open Web Serial port.
/// </summary>
/// <remarks>
/// The browser cannot block while waiting for data, so <see cref="Read(Span{byte})"/> returns only
/// data that has already arrived, and throws when none has. Use <see cref="ReadAsync(Memory{byte}, CancellationToken)"/>
/// to wait for data. Writes are queued in order and never block; <see cref="FlushAsync(CancellationToken)"/> waits
/// until the port has sent every queued write, and reports any write that failed.
/// </remarks>
[SupportedOSPlatform("browser")]
sealed class WebSerialStream : Stream
{
    readonly WebSerialPort owner;
    readonly JSObject handle;
    bool disposed;

    internal WebSerialStream(WebSerialPort owner, JSObject handle)
    {
        this.owner = owner;
        this.handle = handle;
    }

    public override bool CanRead => !disposed;

    public override bool CanWrite => !disposed;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.IsEmpty) return 0;

        if (Interop.BytesAvailable(handle) == 0)
        {
            try
            {
                // Cancellation abandons the wait but not the browser read, which keeps its data
                // for the next call.
                var available = await Interop.WaitForDataAsync(handle).WaitAsync(cancellationToken);
                if (available == 0) return 0;
            }
            catch (JSException ex)
            {
                throw new IOException($"The port failed to read data. {ex.Message}", ex);
            }
        }

        return Interop.Take(handle, buffer.Span);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.IsEmpty) return 0;
        if (Interop.BytesAvailable(handle) == 0)
        {
            throw new NotSupportedException(
                "No data has arrived, and the browser cannot block while waiting for it. Use ReadAsync instead.");
        }

        return Interop.Take(handle, buffer);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (buffer.IsEmpty) return;

        // The browser copies the bytes before this call returns and never writes to them.
        var span = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(buffer), buffer.Length);
        Interop.Write(handle, span);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        await FlushAsync(cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override void Flush()
    {
        // Writes are already queued in order, and the browser cannot block until they are sent.
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            await Interop.DrainAsync(handle).WaitAsync(cancellationToken);
        }
        catch (JSException ex)
        {
            throw new IOException($"The port failed to write data. {ex.Message}", ex);
        }
    }

    internal void Detach()
    {
        disposed = true;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!disposed)
        {
            disposed = true;
            if (disposing) owner.CloseFromStream(handle);
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            await owner.CloseAsync();
        }
        GC.SuppressFinalize(this);
    }
}
