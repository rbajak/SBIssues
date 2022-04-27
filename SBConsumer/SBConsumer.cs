using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using SBCommon;


namespace SBConsumer
{
    class SBConsumer
    {
        static string replyQueueName = "replies";

        static string qHighPrio = CommonUtils.queueName[Priority.High];
        static string qSessions = CommonUtils.queueName[Priority.Session];
        static string qLowPrio = CommonUtils.queueName[Priority.Low];

        static int _highTaskCounter = 0;
        static int _lowTaskCounter = 0;
        private static List<string> _sessionTaskCounter = new List<string>();

        static ServiceBusClient sbClient;
        static ServiceBusProcessor _processorHigh;
        static ServiceBusProcessor _processorLow;
        static ServiceBusSessionProcessor _processorSessions;

        static SemaphoreSlim _semPrint = new SemaphoreSlim(1);
        static SemaphoreSlim _semStatus = new SemaphoreSlim(1);


        static ServiceBusSender replier;

        static string consumerId = Guid.NewGuid().ToString();

        static async Task Main()
        {
            Console.WriteLine($"Consumer ID {consumerId}");
            Console.WriteLine($"Task limit: {Configuration.jobsLimit}");

            sbClient = new ServiceBusClient(Configuration.SBConnectionString);
            replier = sbClient.CreateSender(replyQueueName);

            var options = new ServiceBusProcessorOptions()
            {
                MaxConcurrentCalls = 1, //taskLimit - 1,
				AutoCompleteMessages = false,

				PrefetchCount = 0,
                MaxAutoLockRenewalDuration = new TimeSpan(0, Configuration.maxProcessingMin, Configuration.maxProcessingSec)
            };
            _processorHigh = sbClient.CreateProcessor(qHighPrio, options);
            _processorLow = sbClient.CreateProcessor(qLowPrio, options);

            var sessionsOptions = new ServiceBusSessionProcessorOptions()
            {
                MaxConcurrentCallsPerSession = 1,
                MaxConcurrentSessions = 1, //(int)Math.Ceiling((double)taskLimit / 2),
				AutoCompleteMessages = false,
				SessionIdleTimeout = new TimeSpan(0, 0, Configuration.sessionIdle),
                PrefetchCount = 0,
                MaxAutoLockRenewalDuration = new TimeSpan(0, Configuration.maxProcessingMin, Configuration.maxProcessingSec)
            };
            _processorSessions = sbClient.CreateSessionProcessor(qSessions, sessionsOptions);

            try
            {
                _processorHigh.ProcessMessageAsync += HighMessageHandler;
                _processorHigh.ProcessErrorAsync += ErrorHandler;

                _processorSessions.ProcessMessageAsync += SessionsMessageHandler;
                _processorSessions.ProcessErrorAsync += ErrorHandler;
                _processorSessions.SessionClosingAsync += SessionClosingHandler;

                _processorLow.ProcessMessageAsync += LowMessageHandler;
                _processorLow.ProcessErrorAsync += ErrorHandler;

                await _processorHigh.StartProcessingAsync();
                await _processorSessions.StartProcessingAsync();
                await _processorLow.StartProcessingAsync();

                var hCon = _processorHigh.MaxConcurrentCalls;
                var lCon = _processorLow.MaxConcurrentCalls;
                var sCon = _processorSessions.MaxConcurrentSessions;
                PrintConcurency();

                while (true){
                    await Task.Delay(1000);
                }
            }
            finally
            {
                await _processorHigh.DisposeAsync();
                await _processorLow.DisposeAsync();
                await _processorSessions.DisposeAsync();
                await sbClient.DisposeAsync();
            }
        }

        static async void Print(String msg, ConsoleColor color)
        {
            await _semPrint.WaitAsync();
            Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ResetColor();
            _semPrint.Release(1);
        }

        static async Task HandleMsg(ServiceBusReceivedMessage rMsg)
        {
            ServiceBusMessage msg = new ServiceBusMessage($"{rMsg.Body.ToString()}");
            foreach (var prop in rMsg.ApplicationProperties)
            {
                msg.ApplicationProperties[prop.Key] = prop.Value;
            }
            msg.SessionId = rMsg.ReplyTo;
            msg.ApplicationProperties["To"] = rMsg.To;
            Random r = new Random();
            var min = r.Next(Configuration.minProcessingMin, Math.Max(Configuration.maxProcessingMin -1, Configuration.minProcessingMin));
            var sec = r.Next(Configuration.minProcessingSec, Math.Max(Configuration.maxProcessingSec -1, Configuration.minProcessingSec));
            var sleep = new TimeSpan(0, min, sec);
            var now = DateTime.Now;
            var end = now.Add(sleep);
            await Task.Delay(sleep);

            await replier.SendMessageAsync(msg);
        }

        static async Task HighMessageHandler(ProcessMessageEventArgs args)
        {
            Print($"=> Received {Priority.High} with {args.Message.Body}.{Environment.NewLine}Delivered {args.Message.DeliveryCount} times", CommonUtils.colorDict[Priority.High]);
           
            foreach (var prop in args.Message.ApplicationProperties)
            {
                Print($"Prop: {prop.Key} - {prop.Value}", CommonUtils.colorDict[Priority.High]);
            }

            await AddMsgAsync(Priority.High);
            await HandleMsg(args.Message);
            await args.CompleteMessageAsync(args.Message);

            Print($"                                        <= Replied {Priority.High} with {args.Message.Body}", CommonUtils.colorDict[Priority.High]);
            await RemoveMsgAsync(Priority.High);
        }
        static async Task LowMessageHandler(ProcessMessageEventArgs args)
        {
            Print($"=> Received {Priority.Low} with {args.Message.Body}.{Environment.NewLine}Delivered {args.Message.DeliveryCount} times", CommonUtils.colorDict[Priority.Low]);
            await AddMsgAsync(Priority.Low);
            await HandleMsg(args.Message);
            if (args.Message.LockedUntil < DateTime.Now)
            {
                await args.DeferMessageAsync(args.Message);
            }
            else
            {
                await args.CompleteMessageAsync(args.Message);
            }
            Print($"                                        <= Replied {Priority.Low} with {args.Message.Body}", CommonUtils.colorDict[Priority.Low]);
            await RemoveMsgAsync(Priority.Low);
        }
        static async Task SessionsMessageHandler(ProcessSessionMessageEventArgs args)
        {
            Print($"=> Received {Priority.Session} with {args.Message.Body} {args.Message.SessionId}.{Environment.NewLine}Delivered {args.Message.DeliveryCount} times and expires at {args.Message.ExpiresAt}", CommonUtils.colorDict[Priority.Session]);
            if(DateTime.UtcNow >= args.Message.ExpiresAt)
            {
                Print("Message dropped as it is expired", CommonUtils.colorDict[Priority.Session]);
                return;
            }
            string body = args.Message.Body.ToString();
            if (body.Split(' ')[0] == "start")
            {
                await AddMsgAsync(Priority.Session, args.Message.SessionId);
            }

            await HandleMsg(args.Message);
            await args.CompleteMessageAsync(args.Message);

            Print($"                                        <= Replied {Priority.Session} with {args.Message.Body} {args.Message.SessionId}", CommonUtils.colorDict[Priority.Session]);

            if (body.Split(' ')[0] == "stop")
            {
                args.ReleaseSession();
            }
        }
        static async Task SessionClosingHandler(ProcessSessionEventArgs args)
        {
            await RemoveMsgAsync(Priority.Session, args.SessionId);
            Console.WriteLine($"Session closed {args.SessionId}");
        }
        static void PrintConcurency()
        {
            var hCon = _processorHigh.MaxConcurrentCalls;
            var lCon = _processorLow.MaxConcurrentCalls;
            var sCon = _processorSessions.MaxConcurrentSessions;
            Print($"Concurency High {hCon} - Session {sCon} - Low {lCon} - total {hCon + sCon + lCon}", ConsoleColor.Blue);
        }

        static void PrintStatus()
        {
            var total = _highTaskCounter + _sessionTaskCounter.Count + _lowTaskCounter;
            Print($"Status. High {_highTaskCounter} - Session {_sessionTaskCounter.Count} - Low {_lowTaskCounter}-{_processorLow.IsProcessing} - total {total}", ConsoleColor.Cyan);
        }

        static async Task AddMsgAsync(Priority prio, string sessionId = "")
        {
            await _semStatus.WaitAsync();
            try
            {
                switch (prio)
                {
                    case Priority.High:
                        _highTaskCounter += 1;
                        break;
                    case Priority.Low:
                        _lowTaskCounter += 1;
                        break;
                    case Priority.Session:
                        _sessionTaskCounter.Add(sessionId);
                        break;
                    default:
                        return;
                }
                PrintStatus();
                var total = _highTaskCounter + _sessionTaskCounter.Count + _lowTaskCounter;

                switch (prio)
                {
                    case Priority.High:
                        _processorLow.UpdateConcurrency(Math.Max(1, _processorLow.MaxConcurrentCalls - 1));
                        _processorHigh.UpdateConcurrency(Math.Min(Configuration.jobsLimit - 1, _processorHigh.MaxConcurrentCalls + 1));
                        break;
                    case Priority.Low:
                        if (total < Configuration.jobsLimit)
                            _processorLow.UpdateConcurrency(Math.Min(Configuration.jobsLimit - 1, _processorLow.MaxConcurrentCalls + 1));
                        break;
                    case Priority.Session:
                        _processorLow.UpdateConcurrency(Math.Max(1, _processorLow.MaxConcurrentCalls - 1));
                        _processorHigh.UpdateConcurrency(Math.Max(1, _processorHigh.MaxConcurrentCalls - 1));
                        _processorSessions.UpdateConcurrency(Math.Min(Configuration.jobsLimit, _processorSessions.MaxConcurrentSessions + 1), 1);
                        break;

                }

                PrintConcurency();
            }
            finally
            {
                _semStatus.Release();
            }
        }

        static async Task RemoveMsgAsync(Priority prio, string session = null)
        {
            await _semStatus.WaitAsync();
            try
            {
                switch (prio)
                {
                    case Priority.High:
                        _highTaskCounter -= 1;
                        break;
                    case Priority.Low:
                        _lowTaskCounter -= 1;
                        break;
                    case Priority.Session:
                        {
                            if (String.IsNullOrEmpty(session))
                                return;

                            if (!_sessionTaskCounter.Remove(session))
                            {
                                Console.WriteLine($"!!!! Session {session} was already removed or not handled by this node.");
                                return;
                            }
                            break;
                        }
                    default:
                        return;
                }
                PrintStatus();

                var total = _highTaskCounter + _sessionTaskCounter.Count + _lowTaskCounter;

                if (total >= Configuration.jobsLimit)
                {
                    if (_processorLow.MaxConcurrentCalls > 1)
                        _processorLow.UpdateConcurrency(1);

                    if (_lowTaskCounter == 0 && _processorLow.IsProcessing)
                        await _processorLow.StopProcessingAsync();
                }
                else if (total < Configuration.jobsLimit)
                {
                    if (_lowTaskCounter < _processorLow.MaxConcurrentCalls)
                        _processorLow.UpdateConcurrency(CalculateNewTaskLimit(_lowTaskCounter));

                    if (_highTaskCounter < _processorHigh.MaxConcurrentCalls)
                        _processorHigh.UpdateConcurrency(CalculateNewTaskLimit(_highTaskCounter));

                    if (_sessionTaskCounter.Count < _processorSessions.MaxConcurrentSessions)
                        _processorSessions.UpdateConcurrency(CalculateNewTaskLimit(_sessionTaskCounter.Count), 1);

                    if (!_processorLow.IsProcessing)
                        await _processorLow.StartProcessingAsync();
                }

                PrintConcurency();
            }
            finally
            {
                _semStatus.Release();
            }
        }

        static int CalculateNewTaskLimit(int currentCounter)
        {
            return Math.Max(1, Math.Min(currentCounter + 1, Configuration.jobsLimit - 1));
        }

        static Task ErrorHandler(ProcessErrorEventArgs args)
        {
            Console.WriteLine(args.Exception.ToString());
            return Task.CompletedTask;
        }


    }
}
