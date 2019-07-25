# Ninja WebSockets sample with System.IO.Pipelines and Protobuf ReadOnlySequenceReader 

This is a sample written using Ninja WebSocket library that allows you to make WebSocket connections as a client or to respond to WebSocket requests as a server.

## How to execute the example?
### Command line Options

Ninja.WebSockets.DemoServer.exe [pipe|pb|load] [MessageSize]
WebSockets.DemoClient.exe uri numThreads numItemsPerThread minNumBytesPerMessage maxNumBytesPerMessage

### Example Test with System.IO.Pipelines
Ninja.WebSockets.DemoServer.exe pipe 1024
WebSockets.DemoClient.exe ws://localhost:27416/echo 5 1000 1024 1024


### Example Test with Streams
Ninja.WebSockets.DemoServer.exe 1024
WebSockets.DemoClient.exe ws://localhost:27416/echo 5 1000 1024 1024

### Example Load Test
Ninja.WebSockets.DemoServer.exe load
WebSockets.DemoClient.exe load


## Observations - Performance Readings

| Message Size|Time takesn in Stream (ms)|Time takesn in Pipeline (ms)|
|-------------|------|----|
| 32 |22|23|
|128 |26|26|
|512 |23|26|
|1024|26|29|
|2048|25|28|
|4096|26|27|

Time is in milliseconds - Time taken for sending test bytes (created as per the inputted maxNumberOfBytesPerMessage) to the server 100 times and receiving them back.

Here I could see Stream performed better in comparison to Pipelines when we have ProtoBuf deserialization also in place in the server.
