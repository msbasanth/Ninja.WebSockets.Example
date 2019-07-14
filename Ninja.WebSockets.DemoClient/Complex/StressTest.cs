using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Ninja.WebSockets;
using Ninja.WebSockets.Common;
using ProtoBuf;

namespace WebSockets.DemoClient.Complex
{
    class StressTest
    {
        private readonly int _seed;
        private readonly Uri _uri;
        private readonly int _numItems;
        private readonly int _minNumBytesPerMessage;
        private readonly int _maxNumBytesPerMessage;
        WebSocket _webSocket;
        CancellationToken _token;
        byte[][] _expectedValues;
        private readonly IWebSocketClientFactory _clientFactory;
        private ArraySegment<byte> myTestBytes;
        private int receivedMessages = 0;
        private static IDictionary<int, int> messageSizeMap = new Dictionary<int, int>() { { 32, 30 }, { 128, 126 }, { 512, 509}, { 1024, 1021}, { 2048, 2045}, { 4096, 4093}, { 8192, 8189}, { 10000, 9997},
                { 100000, 99996}, {1000000, 999996}, { 10000000, 9999995 } };

        public StressTest(int seed, Uri uri, int numItems, int minNumBytesPerMessage, int maxBytesPerMessage)
        {
            myTestBytes = GetPersonBytes(messageSizeMap[maxBytesPerMessage]);
            _seed = seed;
            _uri = uri;
            _numItems = numItems;
            _minNumBytesPerMessage = minNumBytesPerMessage ;
            _maxNumBytesPerMessage = maxBytesPerMessage;
            _clientFactory = new WebSocketClientFactory();
        }
        
        private ArraySegment<byte> GetPersonBytes(int count)
        {
            var person = new Person
            {
                Name = new string('a', count)
            };
            byte[] streamArray;
            using (var stream = new MemoryStream())
            {
                Serializer.Serialize(stream, person);
                stream.Position = 0;
                streamArray = stream.ToArray();
                return new ArraySegment<byte>(streamArray);
            }
        }
        Stopwatch stopwatch;

        public async Task Run()
        {
            // NOTE: if the service is so busy that it cannot respond to a PING within the KeepAliveInterval interval the websocket connection will be closed
            // To run extreme tests it is best to set the KeepAliveInterval to TimeSpan.Zero to disable ping pong
            WebSocketClientOptions options = new WebSocketClientOptions() { NoDelay = true, KeepAliveInterval = TimeSpan.FromSeconds(2), SecWebSocketProtocol = "chatV2, chatV1" };
            using (_webSocket = await _clientFactory.ConnectAsync(_uri, options))
            {
                var source = new CancellationTokenSource();
                _token = source.Token;
                Task recTask = Task.Run(ReceiveLoop);
                stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < Constants.NoOfIterations; i++)
                {
                    var buffer = myTestBytes.Array.Clone() as byte[];
                    await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, source.Token);
                }
                recTask.Wait();

                /* Random rand = new Random(_seed);
                 _expectedValues = new byte[50][];
                 for (int i = 0; i < _expectedValues.Length; i++)
                 {
                     int numBytes = rand.Next(_minNumBytesPerMessage, _maxNumBytesPerMessage);
                     byte[] bytes = new byte[numBytes];
                     rand.NextBytes(bytes);
                     _expectedValues[i] = bytes;
                 }

                 Task recTask = Task.Run(ReceiveLoop);
                 byte[] sendBuffer = new byte[_maxNumBytesPerMessage];
                 for (int i = 0; i < _numItems; i++)
                 {
                     int index = i % _expectedValues.Length;
                     byte[] bytes = _expectedValues[index];
                     Buffer.BlockCopy(bytes, 0, sendBuffer, 0, bytes.Length);
                     ArraySegment<byte> buffer = new ArraySegment<byte>(sendBuffer, 0, bytes.Length);
                     await _webSocket.SendAsync(myPersonBytesMap[_maxNumBytesPerMessage], WebSocketMessageType.Binary, true, source.Token);
                 }

                 await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, source.Token);
                 recTask.Wait();*/
            }
        }

        int count = 0;

        private static bool AreEqual(byte[] actual, byte[] expected, int countActual)
        {
            if (countActual != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < countActual; i++)
            {
                if (actual[i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }


        private async Task ReceiveLoop()
        {
            // the recArray should be large enough to at least receive control frames like Ping and Close frames (with payload)
            const int MIN_BUFFER_SIZE = 510;
            int size = _maxNumBytesPerMessage < MIN_BUFFER_SIZE ? MIN_BUFFER_SIZE : _maxNumBytesPerMessage;
            var recArray = new byte[size];
            var recBuffer = new ArraySegment<byte>(recArray);

            try
            {
                int i = 0;
                while (true)
                {
                    WebSocketReceiveResult result = await _webSocket.ReceiveAsync(recBuffer, _token);
                    if (result.EndOfMessage)
                    {
                        receivedMessages++;
                    }
                    if (receivedMessages == Constants.NoOfIterations)
                    {
                        Console.WriteLine($"Completed in {stopwatch.Elapsed.TotalMilliseconds:#,##0.00} ms");
                    }
                    if (!result.EndOfMessage)
                    {
                        throw new Exception("Multi frame messages not supported");
                    }

                    if (result.MessageType == WebSocketMessageType.Close || _token.IsCancellationRequested)
                    {
                        Console.WriteLine("Closed");
                        return;
                    }

                    if (result.Count == 0)
                    {
                        Console.WriteLine("Count = 0");
                        await _webSocket.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData, "Zero bytes in payload", _token);
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }

            //byte[] valueActual = recBuffer.Array;
            //int index = i % _expectedValues.Length;
            //i++;
            //byte[] valueExpected = _expectedValues[index];

            //if (!AreEqual(valueActual, valueExpected, result.Count))
            //{
            //    await _webSocket.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData, "Value actual does not equal value expected", _token);
            //    throw new Exception($"Expected: {valueExpected.Length} bytes Actual: {result.Count} bytes. Contents different.");
            //}
        }
    }
}
