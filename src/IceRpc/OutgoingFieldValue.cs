// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;

namespace IceRpc;

/// <summary>Represents the value of a field that is about to be sent. It's a kind of discriminated union: only one
/// of the struct's properties can be set.</summary>
public readonly record struct OutgoingFieldValue
{
    /// <summary>Gets the value of this outgoing field when <see cref="EncodeAction"/> is <c>null</c>.</summary>
    public ReadOnlySequence<byte> ByteSequence { get; }

    /// <summary>Gets the action used to encode this field or <c>null</c>, when <see cref="ByteSequence"/> holds
    /// the encoded value.</summary>
    /// <value>An encode action used to create a Slice2 encoded field value when the fields are about to be sent.
    /// </value>
    public EncodeAction? EncodeAction { get; }

    /// <summary>Constructs an outgoing field value that holds a byte sequence.</summary>
    /// <param name="byteSequence">The field encoded value.</param>
    public OutgoingFieldValue(ReadOnlySequence<byte> byteSequence)
    {
        ByteSequence = byteSequence;
        EncodeAction = null;
    }

    /// <summary>Constructs an outgoing field value that holds an encode action.</summary>
    /// <param name="encodeAction">The action used to encode the field value.</param>
    public OutgoingFieldValue(EncodeAction encodeAction)
    {
        ByteSequence = default;
        EncodeAction = encodeAction;
    }

    /// <summary>Encodes this field value using a Slice encoder.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="sizeLength">The number of bytes to use to encode the size when <see cref="EncodeAction"/> is
    /// not null.</param>
    public void Encode(ref SliceEncoder encoder, int sizeLength = 2)
    {
        if (encoder.Encoding == SliceEncoding.Slice1)
        {
            throw new NotSupportedException($"cannot encode an {nameof(OutgoingFieldValue)} using Slice1");
        }

        if (EncodeAction is EncodeAction encodeAction)
        {
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(sizeLength);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
        }
        else
        {
            encoder.EncodeSize(checked((int)ByteSequence.Length));
            encoder.WriteByteSequence(ByteSequence);
        }
    }
}
