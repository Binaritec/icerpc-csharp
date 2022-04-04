// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports
{
    /// <summary>A multiplexed stream enables byte data exchange over a multiplexed transport.</summary>
    public interface IMultiplexedStream : IDuplexPipe
    {
        /// <summary>The stream ID.</summary>
        /// <exception cref="InvalidOperationException">Raised if the stream is not started. Local streams are not
        /// started until data is written. A remote stream is always started.</exception>
        long Id { get; }

        /// <summary>Returns <c>true</c> if the stream is a bidirectional stream, <c>false</c> otherwise.</summary>
        bool IsBidirectional { get; }

        /// <summary>Returns <c>true</c> if the local stream is started, <c>false</c> otherwise.</summary>
        bool IsStarted { get; }

        /// <summary>Sets the action which is called when the stream is shutdown.</summary>
        /// <param name="action">The callback to register.</param>
        /// <remarks>If the stream is already shutdown, the callback is called synchronously by this method.</remarks>
        void OnShutdown(Action action);

        /// <summary>Sets the action which is called when the peer <see cref="IDuplexPipe.Input"/> completes.</summary>
        /// <param name="action">The callback to register.</param>
        /// <remarks>If the he peer <see cref="IDuplexPipe.Input"/> is already completed, the callback is called
        /// synchronously by this method.</remarks>
        void OnPeerInputCompleted(Action action);
    }
}
