using Microsoft.Extensions.Logging;
using Ninja.WebSockets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace WebSockets.DemoServer
{
    class Program
    {
        static ILogger _logger;
        static ILoggerFactory _loggerFactory;
        static IWebSocketServerFactory _webSocketServerFactory;
        static bool myIsPipelineImplementation = false;

        static void Main(string[] args)
        {
            if(args.Length == 1)
            {
                myIsPipelineImplementation = args[0] == "pipe";
            }
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddConsole(LogLevel.Trace);
            _logger = _loggerFactory.CreateLogger<Program>();
            _webSocketServerFactory = new WebSocketServerFactory();
            Task task = StartWebServer();
            task.Wait();
        }

        static async Task StartWebServer()
        {
            try
            {
                int port = 27416;
                IList<string> supportedSubProtocols = new string[] { "chatV1", "chatV2", "chatV3" };
                    string mode = myIsPipelineImplementation == true ? "pipe" : "stream";
                using (WebServer server = new WebServer(_webSocketServerFactory, _loggerFactory, myIsPipelineImplementation, supportedSubProtocols))
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
