# Distributed Queues w/ gRPC

This is a gRPC service which uses distributed memory caching internally to support client-side queuing for small queue sizes.

The service definition is specific to Microsoft Bot Framework activities. In order to dequeue messages from a conversation, the client must request a lock for that queue.

To get started with a client, copy the protobuf from `src\GrpcCache.Service\Protos\cache.proto` to your project.

For more information on how to setup a gRPC client -> https://docs.microsoft.com/en-us/aspnet/core/grpc/client?view=aspnetcore-5.0