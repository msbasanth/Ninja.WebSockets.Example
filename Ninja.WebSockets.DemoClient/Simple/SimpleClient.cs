using Ninja.WebSockets;
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipelines;
using System.Buffers;
using System.Runtime.InteropServices;

namespace WebSockets.DemoClient.Simple
{
    class SimpleClient
    {
        public async Task Run()
        {
            var factory = new WebSocketClientFactory();
            var uri = new Uri("ws://localhost:27416/chat");
            using (WebSocket webSocket = await factory.ConnectAsync(uri))
            {
                var pipe = new Pipe();
                // receive loop
                Task readTask = Receive(webSocket, pipe.Writer);
                Task reading = ReadPipeAsync(webSocket, pipe.Reader);

                for (int i = 0; i < 10; i++)
                {
                    // send a message
                    await Send(webSocket);
                    Console.ReadLine();
                }

                // initiate the close handshake
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

                // wait for server to respond with a close frame
                await readTask; 
            }           
        }

        private async Task Send(WebSocket webSocket)
        {
            var array = Encoding.UTF8.GetBytes("Hello World\n");
            var buffer = new ArraySegment<byte>(array);
            await webSocket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        private async Task Receive(WebSocket webSocket, PipeWriter writer)
        {
            const int minimumBufferSize = 1024;
            while (true)
            {
                try
                {
                    Memory<byte> memory = writer.GetMemory(minimumBufferSize);
                    WebSocketReceiveResult result = await webSocket.ReceiveAsync(memory);

                    if (result.Count == 0)
                    {
                        break;
                    }
                    // Tell the PipeWriter how much was read
                    writer.Advance(result.Count);
                    switch (result.MessageType)
                    {
                        case WebSocketMessageType.Close:
                            return;
                        case WebSocketMessageType.Text:
                        case WebSocketMessageType.Binary:
                            string value = Encoding.UTF8.GetString(memory.ToArray(), 0, result.Count);
                            Console.WriteLine(value);
                            break;
                    }
                }
                catch
                {
                    break;
                }
                // Make the data available to the PipeReader
                FlushResult flushResult = await writer.FlushAsync();

                if (flushResult.IsCompleted)
                {
                    break;
                }
            }
            // Signal to the reader that we're done writing
            writer.Complete();
        }

        private static async Task ReadPipeAsync(WebSocket socket, PipeReader reader)
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync();

                ReadOnlySequence<byte> buffer = result.Buffer;
                SequencePosition? position = null;

                do
                {
                    // Find the EOL
                    position = buffer.PositionOf((byte)'\n');

                    if (position != null)
                    {
                        var line = buffer.Slice(0, position.Value);
                        ProcessLine(socket, line);

                        // This is equivalent to position + 1
                        var next = buffer.GetPosition(1, position.Value);

                        // Skip what we've already processed including \n
                        buffer = buffer.Slice(next);
                    }
                }
                while (position != null);

                // We sliced the buffer until no more data could be processed
                // Tell the PipeReader how much we consumed and how much we left to process
                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }

            reader.Complete();
        }

        private static void ProcessLine(WebSocket socket, in ReadOnlySequence<byte> buffer)
        {
                Console.Write($"[{socket.ToString()}]: ");
                foreach (var segment in buffer)
                {
#if NETCOREAPP2_1
                Console.Write(Encoding.UTF8.GetString(segment.Span));
#else
                    Console.Write(Encoding.UTF8.GetString(segment));
#endif
                }
                Console.WriteLine();
        }
    }
    internal static class Extensions
    {
        public static Task<WebSocketReceiveResult> ReceiveAsync(this WebSocket socket, Memory<byte> memory)
        {
            var arraySegment = GetArray(memory);
            return socket.ReceiveAsync(arraySegment, CancellationToken.None);
        }

        public static string GetString(this Encoding encoding, ReadOnlyMemory<byte> memory)
        {
            var arraySegment = GetArray(memory);
            return encoding.GetString(arraySegment.Array, arraySegment.Offset, arraySegment.Count);
        }

        private static ArraySegment<byte> GetArray(Memory<byte> memory)
        {
            return GetArray((ReadOnlyMemory<byte>)memory);
        }

        private static ArraySegment<byte> GetArray(ReadOnlyMemory<byte> memory)
        {
            if (!MemoryMarshal.TryGetArray(memory, out var result))
            {
                throw new InvalidOperationException("Buffer backed by array was expected");
            }

            return result;
        }
    }
}
