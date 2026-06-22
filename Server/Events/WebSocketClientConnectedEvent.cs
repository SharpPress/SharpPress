namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a WebSocket client connects.
    /// </summary>
    public class WebSocketClientConnectedEvent : BaseEvent
    {
        public string ClientId { get; }

        public WebSocketClientConnectedEvent(string clientId)
        {
            ClientId = clientId;
        }
    }
}
