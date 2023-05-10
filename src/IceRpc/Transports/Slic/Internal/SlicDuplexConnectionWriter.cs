// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>A helper class to write data to a duplex connection. It provides a PipeWriter-like API but is not a
/// PipeWriter. Like a PipeWriter, its methods shouldn't be called concurrently. The data written to this writer is
/// copied and buffered with an internal pipe. The data from the pipe is written on the duplex connection with a
/// background task. This allows prompt cancellation of writes and improves write concurrency since multiple writes can
/// be buffered and sent with a single <see cref="IDuplexConnection.WriteAsync" /> call.</summary>
internal class SlicDuplexConnectionWriter : IBufferWriter<byte>, IAsyncDisposable
{
    private readonly Task _backgroundWriteTask;
    private readonly IDuplexConnection _connection;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _disposeTask;
    private readonly Pipe _pipe;
    // This field is temporary and will be removed once the IDuplexConnection WriteAsync operation no longer requires an
    // IReadOnlyList<ReadOnlyMemory<byte> parameter.
    private readonly List<ReadOnlyMemory<byte>> _segments = new() { ReadOnlyMemory<byte>.Empty };

    /// <inheritdoc/>
    public void Advance(int bytes) => _pipe.Writer.Advance(bytes);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _disposeTask ??= PerformDisposeAsync();
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            _disposeCts.Cancel();

            await _backgroundWriteTask.ConfigureAwait(false);

            _pipe.Reader.Complete();
            _pipe.Writer.Complete();

            _disposeCts.Dispose();
        }
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0) => _pipe.Writer.GetSpan(sizeHint);

    /// <summary>Constructs a duplex connection writer.</summary>
    /// <param name="connection">The duplex connection to write to.</param>
    /// <param name="pool">The memory pool to use.</param>
    /// <param name="minimumSegmentSize">The minimum segment size for buffers allocated from <paramref
    /// name="pool"/>.</param>
    internal SlicDuplexConnectionWriter(IDuplexConnection connection, MemoryPool<byte> pool, int minimumSegmentSize)
    {
        _connection = connection;

        // We set pauseWriterThreshold to 0 because Slic implements flow-control at the stream level. So there's no need
        // to limit the amount of data buffered by the writer pipe. The amount of data buffered is limited to
        // (MaxBidirectionalStreams + MaxUnidirectionalStreams) * PeerPauseWriterThreshold bytes.
        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            resumeWriterThreshold: 0));

        _backgroundWriteTask = Task.Run(
            async () =>
            {
                try
                {
                    while (true)
                    {
                        ReadResult readResult = await _pipe.Reader.ReadAsync(_disposeCts.Token).ConfigureAwait(false);

                        if (readResult.Buffer.Length > 0)
                        {
                            _segments.Clear();
                            foreach (ReadOnlyMemory<byte> segment in readResult.Buffer)
                            {
                                _segments.Add(segment);
                            }

                            // TODO: change the IDuplexConnection.WriteAsync API to use ReadOnlySequence<byte> instead.
                            await _connection.WriteAsync(_segments, _disposeCts.Token).ConfigureAwait(false);
                            _pipe.Reader.AdvanceTo(readResult.Buffer.End);
                        }

                        if (readResult.IsCompleted)
                        {
                            await _connection.ShutdownWriteAsync(_disposeCts.Token).ConfigureAwait(false);
                            break;
                        }
                    }
                    _pipe.Reader.Complete();
                }
                catch (OperationCanceledException)
                {
                    // DisposeAsync was called.
                }
                catch (Exception exception)
                {
                    _pipe.Reader.Complete(exception);
                }
            });
    }

    internal async ValueTask FlushAsync(CancellationToken cancellationToken) =>
        _ = await _pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Requests the shut down of the duplex connection after the buffered data is written on the duplex
    /// connection.</summary>
    internal void Shutdown() => _pipe.Writer.Complete();

    /// <summary>Writes two sequences of bytes.</summary>
    internal async ValueTask WriteAsync(
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        CancellationToken cancellationToken)
    {
        if (source1.Length > 0)
        {
            _pipe.Writer.Write(source1);
        }
        if (source2.Length > 0)
        {
            _pipe.Writer.Write(source2);
        }

        await _pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}