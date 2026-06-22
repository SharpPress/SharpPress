namespace SharpPress.Events
{
    /// <summary>
    /// Event published when a WebSocket client disconnects.
    /// </summary>
    public class WebSocketClientDisconnectedEvent : BaseEvent
    {
        public string ClientId { get; }

        public WebSocketClientDisconnectedEvent(string clientId)
        {
            ClientId = clientId;
        }
    }
}
