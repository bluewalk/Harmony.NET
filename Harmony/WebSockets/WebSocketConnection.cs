// -----------------------------------------------------------------------
// <copyright file="WebSocketConnection.cs" company="John Lynch">
//   This file is licensed under the MIT license.
//   Copyright (c) 2018 John Lynch
// </copyright>
// -----------------------------------------------------------------------

using Harmony.Events;
using WebSocketSharp;

namespace Harmony.WebSockets
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Harmony.Responses;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    using WebSocketSharp;

    /// <summary>
    ///     A handler for websocket connections
    /// </summary>
    internal abstract class WebSocketConnection : IDisposable
    {
        /// <summary>
        ///     A semaphore to ensure multiple messages aren't sent over the websocket at once
        /// </summary>
        private readonly SemaphoreSlim SendSemaphore = new SemaphoreSlim(1);

        /// <summary>
        ///     Gets the websocket to communicate over
        /// </summary>
        protected WebSocketSharp.WebSocket WebSocket { get; set; }

        public event EventHandler<StringResponseEventArgs> OnMessageReceived;

        private readonly System.Timers.Timer _tmrReconnect;
        private readonly System.Timers.Timer _tmrPing;

        protected WebSocketConnection()
        {
            _tmrReconnect = new System.Timers.Timer(15000);
            _tmrReconnect.Elapsed += (sender, args) =>
            {
                WebSocket.ConnectAsync();
                (sender as System.Timers.Timer)?.Stop();
            };

            _tmrPing = new System.Timers.Timer(30000);
            _tmrPing.Elapsed += (sender, args) =>
            {
                if (WebSocket.IsAlive)
                    WebSocket.Ping();
            };
        }

        /// <summary>
        ///     Connects to the WebSocket
        /// </summary>
        /// <param name="url">The url to connect to</param>
        /// <returns>When the WebSocket has connected</returns>
        public void Connect(string url)
        {
            WebSocket = new WebSocketSharp.WebSocket(url);
            WebSocket.OnMessage += (sender, args) =>
            {
                var messageObject = JObject.Parse(args.Data);
                var msg = messageObject.ContainsKey("cmd")
                    ? messageObject.ToObject<StringResponse>()
                    : messageObject.ToObject<IncomingMessage>().ToStringResponse();

                OnMessageReceived?.Invoke(this, new StringResponseEventArgs(msg));
            };
            WebSocket.OnOpen += (sender, args) => _tmrPing.Start();
            WebSocket.OnError += (sender, args) =>
            {
                _tmrPing.Stop();
                _tmrReconnect.Start();
            };
            WebSocket.OnClose += (sender, args) =>
            {
                _tmrPing.Stop();
                if (!args.WasClean)
                    _tmrReconnect.Start();
            };

            WebSocket.ConnectAsync();
        }

        /// <summary>
        ///     Disconnects from the WebSocket
        /// </summary>
        /// <returns>When the WebSocket has disconnected</returns>
        public void Disconnect() => WebSocket.CloseAsync(CloseStatusCode.Normal);

        /// <inheritdoc />
        /// <summary>
        ///     Disposes resources used by this <see cref="T:Harmony.HubConnection" />
        /// </summary>
        public virtual void Dispose() => WebSocket.CloseAsync(CloseStatusCode.Normal);

        /// <summary>
        ///     Sends a JSON message over the websocket
        /// </summary>
        /// <typeparam name="T">The type of the payload data being sent over the websocket</typeparam>
        /// <param name="data">The object to serialize and send over the websocket</param>
        /// <returns>When the message has been sent</returns>
        protected Task SendJsonMessage<T>(T data) => this.SendMessage(JsonConvert.SerializeObject(data));

        /// <summary>
        ///     Sends a string message over the websocket using UTF-8 encoding
        /// </summary>
        /// <param name="message">The message to send over the websocket</param>
        /// <returns>When the message has been sent</returns>
        protected async Task SendMessage(string message)
        {
            var messageBytes = Encoding.UTF8.GetBytes(message);

            // avoid sending multiple messages at once
            await this.SendSemaphore.WaitAsync();
            try
            {
                WebSocket.SendAsync(messageBytes, b => { });
            }
            finally
            {
                this.SendSemaphore.Release();
            }
        }
    }
}