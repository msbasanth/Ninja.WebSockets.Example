using Microsoft.Extensions.Logging;
using Ninja.WebSockets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WebSockets.DemoServer
{
    class Program
    {
        static ILogger _logger;
        static ILoggerFactory _loggerFactory;
        static IWebSocketServerFactory _webSocketServerFactory;

        static void Main(string[] args)
        {
            bool isPipelineImplementation = false;
            bool isProtobufSerializationEnabled = false;
            bool isLoadTest = false;
            if (args.Contains("pipe"))
            {
                isPipelineImplementation = true;
            }
            if(args.Contains("pb"))
            {
                isProtobufSerializationEnabled = true;
            }
            if (args.Contains("load"))
            {
                isLoadTest = true;
            }
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddConsole(LogLevel.Trace);
            _logger = _loggerFactory.CreateLogger<Program>();
            _webSocketServerFactory = new WebSocketServerFactory();
            Console.WriteLine("Pipeline enabled: {0}, Protobuf serialization enabled: {1}, Load test: {2}", isPipelineImplementation, isProtobufSerializationEnabled, isLoadTest);
            Task task = StartWebServer(isPipelineImplementation, isProtobufSerializationEnabled, isLoadTest);
            task.Wait();
        }

        static async Task StartWebServer(bool isPipelineMode, bool protoBufEnabled, bool isLoadTest)
        {
            try
            {
                int port = 27416;
                IList<string> supportedSubProtocols = new string[] { "chatV1", "chatV2", "chatV3" };
                    string mode = isPipelineMode == true ? "pipe" : "stream";
                using (WebServer server = new WebServer(_webSocketServerFactory, _loggerFactory, isPipelineMode, protoBufEnabled, isLoadTest, supportedSubProtocols))
                {
                    await server.Listen(port);
                    _logger.LogInformation($"Listening on port {port} in '{ mode}' mode");
                    _logger.LogInformation("Press any key to quit");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                Console.ReadKey();
            }
        }
    }
}
