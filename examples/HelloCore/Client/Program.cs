// Copyright (c) ZeroC, Inc. All rights reserved.

using HelloCoreExample;
using IceRpc;

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));

// Construct an outgoing request for the icerpc protocol.
using var request = new OutgoingRequest(new ServiceAddress(Protocol.IceRpc))
{
    Operation = "sayHelloCore",
    Payload = StringCodec.EncodeString(Environment.UserName)
};

// Make the invocation: we send the request using the client connection and then wait for the response.
// Since the client connection is not connected yet, this call also connects the connection.
IncomingResponse response = await connection.InvokeAsync(request);

// When the response's status code is Success, we decode its payload.
if (response.StatusCode == StatusCode.Success)
{
    string greeting = await StringCodec.DecodePayloadStringAsync(response.Payload);

    Console.WriteLine(greeting);
}
else
{
    Console.WriteLine($"request failed: {response.StatusCode}");
}