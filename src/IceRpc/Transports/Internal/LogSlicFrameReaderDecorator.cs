// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    /// <summary>The LogSlicFrameReaderDecorator is a decorator to log Slic frames read from the
    /// decorated Slic frame reader.</summary>
    internal sealed class LogSlicFrameReaderDecorator : ISlicFrameReader
    {
        private readonly ISlicFrameReader _decoratee;
        private FrameType _frameType;
        private int _frameDataSize;
        private long? _frameStreamId;
        private readonly ILogger _logger;

        public async ValueTask ReadFrameDataAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            await _decoratee.ReadFrameDataAsync(buffer, cancel).ConfigureAwait(false);
            if (_frameType != FrameType.Stream && _frameType != FrameType.StreamLast)
            {
                LogReadFrame(_frameType, _frameDataSize, _frameStreamId, buffer);
            }
        }

        public async ValueTask<(FrameType, int)> ReadFrameHeaderAsync(CancellationToken cancel)
        {
            (_frameType, _frameDataSize) = await _decoratee.ReadFrameHeaderAsync(cancel).ConfigureAwait(false);
            _frameStreamId = null;
            return (_frameType, _frameDataSize);
        }

        public async ValueTask<(FrameType, int, long)> ReadStreamFrameHeaderAsync(CancellationToken cancel)
        {
            (_frameType, _frameDataSize, _frameStreamId) =
                await _decoratee.ReadStreamFrameHeaderAsync(cancel).ConfigureAwait(false);
            using IDisposable? scope = _logger.StartStreamScope(_frameStreamId.Value);
            if (_frameType == FrameType.Stream || _frameType == FrameType.StreamLast)
            {
                _logger.LogReceivingSlicDataFrame(_frameType, _frameDataSize);
            }
            return (_frameType, _frameDataSize, _frameStreamId.Value);
        }

        internal LogSlicFrameReaderDecorator(ISlicFrameReader decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }

        private void LogReadFrame(FrameType type, int dataSize, long? streamId, ReadOnlyMemory<byte> buffer)
        {
            // If the frame is not a stream frame, we need to re-encode the frame with the header and data because
            // Slice Read reader extension methods read the header.
            int frameSize;
            if (streamId == null)
            {
                var bufferWriter = new BufferWriter();
                var encoder = new Ice20Encoder(bufferWriter);
                encoder.EncodeByte((byte)type);
                BufferWriter.Position sizePos = encoder.StartFixedLengthSize();
                bufferWriter.WriteByteSpan(buffer.Span);
                frameSize = encoder.EndFixedLengthSize(sizePos);
                buffer = bufferWriter.Finish().Span[0];
            }
            else
            {
                frameSize = dataSize + IceEncoder.GetVarULongEncodedSize((ulong)streamId.Value);
            }

            // Create a reader to read again the frame from the memory buffer.
            using var reader = new BufferedReceiverSlicFrameReader(new BufferedReceiver(buffer));

            switch (type)
            {
                case FrameType.Initialize:
                {
                    (uint version, InitializeBody? initializeBody) =
                        ReadFrame(() => reader.ReadInitializeAsync(default));
                    if (initializeBody == null)
                    {
                        _logger.LogReceivedSlicUnsupportedInitializeFrame(frameSize, version);
                    }
                    else
                    {
                        _logger.LogReceivedSlicInitializeFrame(frameSize, version, initializeBody.Value);
                    }
                    break;
                }
                case FrameType.InitializeAck:
                case FrameType.Version:
                {
                    (InitializeAckBody? initializeAckBody, VersionBody? versionBody) =
                        ReadFrame(() => reader.ReadInitializeAckOrVersionAsync(default));
                    if (initializeAckBody != null)
                    {
                        _logger.LogReceivedSlicInitializeAckFrame(frameSize, initializeAckBody.Value);
                    }
                    else
                    {
                        _logger.LogReceivedSlicVersionFrame(frameSize, versionBody!.Value);
                    }
                    break;
                }
                case FrameType.StreamReset:
                {
                    StreamResetBody body = ReadFrame(() => reader.ReadStreamResetAsync(dataSize, default));
                    _logger.LogReceivedSlicResetFrame(frameSize, (StreamError)body.ApplicationProtocolErrorCode);
                    break;
                }
                case FrameType.StreamConsumed:
                {
                    StreamConsumedBody body = ReadFrame(() => reader.ReadStreamConsumedAsync(dataSize, default));
                    _logger.LogReceivedSlicConsumedFrame(frameSize, (int)body.Size);
                    break;
                }
                case FrameType.StreamStopSending:
                {
                    StreamStopSendingBody body = ReadFrame(() => reader.ReadStreamStopSendingAsync(dataSize, default));
                    _logger.LogReceivedSlicStopSendingFrame(frameSize, (StreamError)body.ApplicationProtocolErrorCode);
                    break;
                }
                default:
                {
                    Debug.Assert(false, $"unexpected Slic frame {type}");
                    break;
                }
            }

            static T ReadFrame<T>(Func<ValueTask<T>> readFunc)
            {
                try
                {
                    // The reading of the frame should always complete synchronously since we're reading the
                    // frame from a memory buffer.
                    ValueTask<T> task = readFunc();
                    Debug.Assert(task.IsCompleted);
                    return task.Result;
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, $"failed to read Slic frame\n{ex}");
                    return default;
                }
            }
        }
    }
}