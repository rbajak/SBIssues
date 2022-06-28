using Azure.Messaging.ServiceBus;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using SBCommon;
using System.Threading.Tasks;

namespace SBClientDll
{
    class SBConnector
    {
        private ServiceBusClient _client = null;
        private String _senderId = Guid.NewGuid().ToString();
        private CancellationTokenSource cts = null;

        public SBConnector()
        {
            _client = new ServiceBusClient(Configuration.SBConnectionString);
            this.cts = new CancellationTokenSource();
        }

        public async Task<int> MakeCall(Priority prio, string body, string sessionId = "")
        {
            ServiceBusSender sender = null;
            ServiceBusReceiver receiver = _client.AcceptSessionAsync("replies", _senderId).GetAwaiter().GetResult();
            try
            {
                TimeSpan maxWait = new TimeSpan(0, Configuration.maxProcessingMin + 1, 0);
                ServiceBusMessage msg = new ServiceBusMessage($"{body} {DateTime.Now}");
                msg.ReplyTo = _senderId;
                msg.TimeToLive = maxWait;
                if (!String.IsNullOrWhiteSpace(sessionId))
                {
                    msg.SessionId = sessionId;
                }

                sender = _client.CreateSender(CommonUtils.queueName[prio]);

                sender.SendMessageAsync(msg).GetAwaiter().GetResult();

                cts = new CancellationTokenSource();
                var start = DateTime.Now;
                while ((DateTime.Now - start) < maxWait)
                {
                    ServiceBusReceivedMessage message = receiver.ReceiveMessageAsync(maxWaitTime: maxWait, cancellationToken: cts.Token).Result;
                    if (message != null)
                    {
                        await receiver.CompleteMessageAsync(message);
                        Console.WriteLine($"Reply {message.ApplicationProperties["To"]} {message.Body.ToString()}");

                        return 0; //No other processing done
                    }
                }
                return -1;
            }
            finally
            {
                await receiver.DisposeAsync();
            }
        }
    }
}
