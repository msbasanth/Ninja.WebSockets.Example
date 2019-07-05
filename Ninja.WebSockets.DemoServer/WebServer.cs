using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Threading;
using Ninja.WebSockets;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Buffers;
using System.Text;
using System.Runtime.InteropServices;
using Ninja.WebSockets.Common;
using ProtoBuf;
using ProtoBuf.Meta;
//using ProtoBuf;
//using ProtoBuf.Meta;

namespace WebSockets.DemoServer
{
    public class WebServer : IDisposable
    {
        private TcpListener _listener;
        private bool _isDisposed = false;
        ILogger _logger;
        private readonly IWebSocketServerFactory _webSocketServerFactory;
        private readonly ILoggerFactory _loggerFactory;
        private readonly HashSet<string> _supportedSubProtocols;
        // const int BUFFER_SIZE = 1 * 1024 * 1024 * 1024; // 1GB
        const int BUFFER_SIZE = 4 * 1024 * 1024; // 4MB
        private bool myIsPipelineImplementation;
        public WebServer(IWebSocketServerFactory webSocketServerFactory, ILoggerFactory loggerFactory,bool isPipelineImplementation,IList<string> supportedSubProtocols = null)
        {
            myIsPipelineImplementation = isPipelineImplementation;
            _logger = loggerFactory.CreateLogger<WebServer>();
            _webSocketServerFactory = webSocketServerFactory;
            _loggerFactory = loggerFactory;
            _supportedSubProtocols = new HashSet<string>(supportedSubProtocols ?? new string[0]);
        }

        private async Task ProcessTcpClient(TcpClient tcpClient)
        {
            await ProcessTcpClientAsync(tcpClient);
        }

        private string GetSubProtocol(IList<string> requestedSubProtocols)
        {
            foreach (string subProtocol in requestedSubProtocols)
            {
                // match the first sub protocol that we support (the client should pass the most preferable sub protocols first)
                if (_supportedSubProtocols.Contains(subProtocol))
                {
                    _logger.LogInformation($"Http header has requested sub protocol {subProtocol} which is supported");

                    return subProtocol;
                }
            }

            if (requestedSubProtocols.Count > 0)
            {
                _logger.LogWarning($"Http header has requested the following sub protocols: {string.Join(", ", requestedSubProtocols)}. There are no supported protocols configured that match.");
            }

            return null;
        }

        private async Task ProcessTcpClientAsync(TcpClient tcpClient)
        {
            CancellationTokenSource source = new CancellationTokenSource();

            try
            {
                if (_isDisposed)
                {
                    return;
                }

                // this worker thread stays alive until either of the following happens:
                // Client sends a close conection request OR
                // An unhandled exception is thrown OR
                // The server is disposed
                _logger.LogInformation("Server: Connection opened. Reading Http header from stream");

                // get a secure or insecure stream
                Stream stream = tcpClient.GetStream();
                WebSocketHttpContext context = await _webSocketServerFactory.ReadHttpHeaderFromStreamAsync(stream);
                if (context.IsWebSocketRequest)
                {
                    string subProtocol = GetSubProtocol(context.WebSocketRequestedProtocols);
                    var options = new WebSocketServerOptions() { KeepAliveInterval = TimeSpan.FromSeconds(30), SubProtocol = subProtocol };
                    _logger.LogInformation("Http header has requested an upgrade to Web Socket protocol. Negotiating Web Socket handshake");

                    WebSocket webSocket = await _webSocketServerFactory.AcceptWebSocketAsync(context, options);
                    var pipe = new Pipe();

                    _logger.LogInformation("Web Socket handshake response sent. Stream ready.");
                    Task writing = RespondToWebSocketRequestAsync(webSocket, source.Token, pipe.Writer);
                    Task reading = ReadPipeAsync(webSocket, pipe.Reader, source.Token);
                    await Task.WhenAll(reading, writing);
                }
                else
                {
                    _logger.LogInformation("Http header contains no web socket upgrade request. Ignoring");
                }

                _logger.LogInformation("Server: Connection closed");
            }
            catch (ObjectDisposedException)
            {
                // do nothing. This will be thrown if the Listener has been stopped
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            finally
            {
                try
                {
                    tcpClient.Client.Close();
                    tcpClient.Close();
                    source.Cancel();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to close TCP connection: {ex}");
                }
            }
        }

        public async Task RespondToWebSocketRequestAsync(WebSocket webSocket, CancellationToken token, PipeWriter pipeWriter)
        {
            if (!myIsPipelineImplementation)
            {
                ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[BUFFER_SIZE]);

                while (true)
                {
                    WebSocketReceiveResult result = await webSocket.ReceiveAsync(buffer, token);
                    using (var stream = new MemoryStream(buffer.Array, 0, result.Count))
                    {
                        var person = Serializer.Deserialize<Person>(stream);
                    }
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation($"Client initiated close. Status: {result.CloseStatus} Description: {result.CloseStatusDescription}");
                        break;
                    }

                    if (result.Count > BUFFER_SIZE)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig,
                            $"Web socket frame cannot exceed buffer size of {BUFFER_SIZE:#,##0} bytes. Send multiple frames instead.",
                            token);
                        break;
                    }

                    ArraySegment<byte> toSend = new ArraySegment<byte>(buffer.Array, buffer.Offset, result.Count);
                    await webSocket.SendAsync(toSend, WebSocketMessageType.Binary, true, token);
                }
            }
            else
            {
                const int minimumBufferSize = BUFFER_SIZE;

                while (true)
                {
                    try
                    {
                        Memory<byte> memory = pipeWriter.GetMemory(minimumBufferSize);
                        WebSocketReceiveResult result = await webSocket.ReceiveAsync(memory);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogInformation($"Client initiated close. Status: {result.CloseStatus} Description: {result.CloseStatusDescription}");
                            break;
                        }

                        if (result.Count > BUFFER_SIZE)
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig,
                                $"Web socket frame cannot exceed buffer size of {BUFFER_SIZE:#,##0} bytes. Send multiple frames instead.",
                                token);
                            break;
                        }
                        pipeWriter.Advance(result.Count);
                    }
                    catch (Exception ex)
                    {
                        break;
                    }
                    // Make the data available to the PipeReader
                    FlushResult flushResult = await pipeWriter.FlushAsync();

                    if (flushResult.IsCompleted)
                    {
                        break;
                    }
                }
                // Signal to the reader that we're done writing
                pipeWriter.Complete();
            }
        }
       
        public async Task Listen(int port)
        {
            try
            {
                IPAddress localAddress = IPAddress.Any;
                _listener = new TcpListener(localAddress, port);
                _listener.Start();
                _logger.LogInformation($"Server started listening on port {port} in pipe = {myIsPipelineImplementation}");
                while(true)
                {
                    TcpClient tcpClient = await _listener.AcceptTcpClientAsync();
                    ProcessTcpClient(tcpClient);
                }
            }
            catch (SocketException ex)
            {
                string message = string.Format("Error listening on port {0}. Make sure IIS or another application is not running and consuming your port.", port);
                throw new Exception(message, ex);
            }
        }

        private async Task ReadPipeAsync(WebSocket socket, PipeReader reader, CancellationToken token)
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync();

                ReadOnlySequence<byte> buffer = result.Buffer;
                await ProcessLine(socket, buffer, token);
                buffer = buffer.Slice(buffer.Length);

                //do
                //{
                //    // Find the EOL
                //    position = buffer.PositionOf((byte)'\n');

                //    if (position != null)
                //    {
                //        var line = buffer.Slice(0, position.Value);

                //        await ProcessLine(socket, line, token);

                //        // This is equivalent to position + 1
                //        var next = buffer.GetPosition(1, position.Value);

                //        // Skip what we've already processed including \n
                //        buffer = buffer.Slice(next);
                //    }
                //}
                //while (position != null);

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

        private async Task ProcessLine(WebSocket socket, ReadOnlySequence<byte> buffer, CancellationToken token)
        {
            try
            {
                var person = Deserialize(buffer);
           
            foreach (var segment in buffer)
            {
#if NETCOREAPP2_1
                await socket.SendAsync(segment.ToArray(), WebSocketMessageType.Binary, true, token);
#else
                await socket.SendAsync(segment.ToArray(), WebSocketMessageType.Binary, true, token);
                //byte[] newline = Encoding.ASCII.GetBytes(Environment.NewLine);
                //await socket.SendAsync(newline, WebSocketMessageType.Binary, true, token);
#endif
            }
             }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private Person Deserialize(ReadOnlySequence<byte> buffer)
        {
            var reader = ProtoReader.Create(out ProtoReader.State state, buffer, RuntimeTypeModel.Default, new SerializationContext());
            var data = new Person();

            int header = 0;
            while ((header = reader.ReadFieldHeader(ref state)) > 0)
            {
                switch (header)
                {
                    case 1:
                        data.Name = reader.ReadString(ref state);
                        break;
                    default:
                        reader.SkipField(ref state);
                        break;
                }
            }

            return data;
        }

        private Person Deserialize(ReadOnlyMemory<byte> buffer)
        {
            var reader = ProtoReader.Create(out ProtoReader.State state, buffer, RuntimeTypeModel.Default, new SerializationContext());
            var data = new Person();

            int header = 0;
            while ((header = reader.ReadFieldHeader(ref state)) > 0)
            {
                switch (header)
                {
                    case 1:
                        data.Name = reader.ReadString(ref state);
                        break;
                    default:
                        reader.SkipField(ref state);
                        break;
                }
            }

            return data;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                // safely attempt to shut down the listener
                try
                {
                    if (_listener != null)
                    {
                        if (_listener.Server != null)
                        {
                            _listener.Server.Close();
                        }

                        _listener.Stop();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError( ex.ToString());
                }
                
                _logger.LogInformation( "Web Server disposed");
            }
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
