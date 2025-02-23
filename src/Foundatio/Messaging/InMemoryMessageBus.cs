﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Foundatio.Queues;
using Foundatio.Utility;
using Microsoft.Extensions.Logging;

namespace Foundatio.Messaging {
    public class InMemoryMessageBus : MessageBusBase<InMemoryMessageBusOptions> {
        private readonly ConcurrentDictionary<string, long> _messageCounts = new ConcurrentDictionary<string, long>();
        private long _messagesSent;

        public InMemoryMessageBus() : this(o => o) {}

        public InMemoryMessageBus(InMemoryMessageBusOptions options) : base(options) { }

        public InMemoryMessageBus(Builder<InMemoryMessageBusOptionsBuilder, InMemoryMessageBusOptions> config)
            : this(config(new InMemoryMessageBusOptionsBuilder()).Build()) { }

        public long MessagesSent => _messagesSent;

        public long GetMessagesSent(Type messageType) {
            return _messageCounts.TryGetValue(GetMappedMessageType(messageType), out var count) ? count : 0;
        }

        public long GetMessagesSent<T>() {
            return _messageCounts.TryGetValue(GetMappedMessageType(typeof(T)), out var count) ? count : 0;
        }

        public void ResetMessagesSent() {
            Interlocked.Exchange(ref _messagesSent, 0);
            _messageCounts.Clear();
        }

        protected override async Task PublishImplAsync(string messageType, object message, TimeSpan? delay, MessageOptions options, CancellationToken cancellationToken) {
            Interlocked.Increment(ref _messagesSent);
            _messageCounts.AddOrUpdate(messageType, t => 1, (t, c) => c + 1);
            var mappedType = GetMappedMessageType(messageType);

            if (_subscribers.IsEmpty)
                return;

            bool isTraceLogLevelEnabled = _logger.IsEnabled(LogLevel.Trace);
            if (delay.HasValue && delay.Value > TimeSpan.Zero) {
                if (isTraceLogLevelEnabled)
                    _logger.LogTrace("Schedule delayed message: {MessageType} ({Delay}ms)", messageType, delay.Value.TotalMilliseconds);
                SendDelayedMessage(mappedType, message, delay.Value);
                return;
            }
            
            byte[] body = SerializeMessageBody(messageType, message);
            var messageData = new Message(() => DeserializeMessageBody(messageType, body)) {
                Type = messageType,
                ClrType = mappedType,
                Data = body
            };

            try {
                await SendMessageToSubscribersAsync(messageData).AnyContext();
            } catch (Exception ex) {
                // swallow exceptions from subscriber handlers for the in memory bus
                _logger.LogWarning(ex, "Error sending message to subscribers: {ErrorMessage}", ex.Message);
            }
        }
    }
}