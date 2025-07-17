# SignalBus

A .NET 8 worker service that connects to a WebSocket to receive Signal messages and forwards them to an assistant webhook.

## Features

- **WebSocket Connection**: Connects to a Signal WebSocket to receive messages
- **Assistant Service**: Forwards received messages to a webhook with authorization
- **Automatic Reconnection**: Handles connection failures with automatic retry
- **Structured Logging**: Comprehensive logging throughout the application

## Configuration

The application requires the following environment variables:

### Required Environment Variables

- `SOCKET_URI`: The WebSocket URI to connect to for receiving Signal messages
- `WEBHOOK_URL`: The URL of the assistant webhook to send messages to
- `AUTH_TOKEN`: The authorization token for the webhook (sent as Bearer token)
- `SIGNAL_ENDPOINT`: The GET endpoint URL for sending messages via SignalService

### Example

```bash
SOCKET_URI=ws://localhost:8080/v1/receive
WEBHOOK_URL=https://api.example.com/assistant/webhook
AUTH_TOKEN=your-secret-token-here
SIGNAL_ENDPOINT=https://api.signal.example.com/send
```

## AssistantService

The `AssistantService` provides a method to send messages to an external webhook:

### Interface

```csharp
public interface IAssistantService
{
    Task<string> SendMessageAsync(string message, string userId);
}
```

### Usage

The service is automatically injected into the Worker and called when data messages are received:

```csharp
var assistantResponse = await _assistantService.SendMessageAsync(
    signalMessage.Envelope.DataMessage.Message, 
    userId);
```

### Request Format

The service sends a POST request with the following JSON payload:

```json
{
  "message": "The received message text",
  "userId": "The user identifier (source number or source)",
  "timestamp": 1642694400
}
```

### Authorization

The request includes an `Authorization` header with the Bearer token:

```
Authorization: Bearer your-secret-token-here
```

## SignalService

The `SignalService` provides a method to send messages to a GET endpoint:

### Interface

```csharp
public interface ISignalService
{
    Task<string> SendMessageAsync(string message);
}
```

### Usage

The service can be injected and used to send messages via GET request:

```csharp
var response = await _signalService.SendMessageAsync("Hello, world!");
```

### Request Format

The service sends a GET request with the message as a URL parameter:

```
GET https://api.signal.example.com/send?message=Hello%2C%20world%21
```

The message parameter is automatically URL-encoded to handle special characters safely.

### Configuration

The service requires the `SIGNAL_ENDPOINT` environment variable to be set to the target GET endpoint URL.

## Running the Application

1. Set the required environment variables
2. Build and run the application:

```bash
dotnet build
dotnet run
```

## Docker Support

The application includes Docker support. Build and run with:

```bash
docker build -t signalbus .
docker run -e SOCKET_URI=ws://localhost:8080/v1/receive \
           -e WEBHOOK_URL=https://api.example.com/assistant/webhook \
           -e AUTH_TOKEN=your-secret-token-here \
           -e SIGNAL_ENDPOINT=https://api.signal.example.com/send \
           signalbus
```

## Error Handling

- WebSocket connection failures trigger automatic reconnection after 5 seconds
- Assistant service failures are logged but don't interrupt message processing
- All errors include structured logging for debugging

## Dependencies

- .NET 8.0
- Microsoft.Extensions.Hosting
- Microsoft.Extensions.Http
- System.Text.Json (for JSON serialization)
