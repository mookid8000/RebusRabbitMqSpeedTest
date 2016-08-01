using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Logging;
using Rebus.RabbitMq;
#pragma warning disable 1998

namespace RebusRabbitMqSpeedTest
{
    class Program
    {
        /// <summary>
        /// UTF8-encoding some JSON wrapping these bad boys will be approx 2 kB
        /// </summary>
        static readonly string ApproxTwoKbOfData = new string('*', 2048);

        /// <summary>
        /// Just connect to local RabbitMQ
        /// </summary>
        const string ConnectionString = "amqp://localhost";

        /// <summary>
        /// Use this queue. WARNING: It will be PURGED before each run! (IOW you should probably be sure that you are not using a queue with this name)
        /// </summary>
        const string QueueName = "speedtest";

        /// <summary>
        /// How many messages to send/receive
        /// </summary>
        const int NumberOfMessages = 10000;

        /// <summary>
        /// How many threads to use while sending
        /// </summary>
        const int SendParallelism = 20;

        /// <summary>
        /// How many threads to use while receiving
        /// </summary>
        const int ReceiveParallelism = 20;

        static void Main()
        {
            Print("Cleaning up from previous runs....");

            using (var transport = new RabbitMqTransport(ConnectionString, QueueName, new NullLoggerFactory()))
            {
                transport.PurgeInputQueue();
            }

            using (var activator = new BuiltinHandlerActivator())
            {
                var allMessagesReceived = new ManualResetEvent(false);
                var receivedMessages = 0;
                activator.Handle<MessageWithString>(async message =>
                {
                    var newValue = Interlocked.Increment(ref receivedMessages);

                    if (newValue == NumberOfMessages)
                    {
                        allMessagesReceived.Set();
                    }
                });

                Print("Starting bus");

                Configure.With(activator)
                    .Logging(l => l.ColoredConsole(LogLevel.Warn))
                    .Transport(t =>
                    {
                        t.UseRabbitMq(ConnectionString, QueueName)
                            .Prefetch(50);
                    })
                    .Options(o =>
                    {
                        o.SetNumberOfWorkers(0); //< don't receive anything yet
                        o.SetMaxParallelism(ReceiveParallelism);
                    })
                    .Start();

                Print("Bus started");

                var bus = activator.Bus;

                Pause();

                TakeTime("Sending messages", NumberOfMessages, () =>
                {
                    var messagesToSend = Enumerable.Repeat(new MessageWithString(ApproxTwoKbOfData), NumberOfMessages);
                    var queue = new ConcurrentQueue<MessageWithString>(messagesToSend);

                    var sendThreads = Enumerable.Range(0, SendParallelism)
                        .Select(_ => new Thread(() =>
                        {
                            MessageWithString nextMessage;
                            while (queue.TryDequeue(out nextMessage))
                            {
                                bus.SendLocal(nextMessage).Wait();
                            }
                        }))
                        .ToList();

                    sendThreads.ForEach(t => t.Start());
                    sendThreads.ForEach(t => t.Join());
                });

                TakeTime("Received messages", NumberOfMessages, () =>
                {
                    bus.Advanced.Workers.SetNumberOfWorkers(ReceiveParallelism);

                    allMessagesReceived.WaitOne();
                });

                Print("Stopping bus");
            }

            Print("Bus stopped");
        }

        static void TakeTime(string description, int total, Action action)
        {
            Console.WriteLine($"Action '{description}' executing...");

            var stopwatch = Stopwatch.StartNew();

            action();

            var elapsed = stopwatch.Elapsed;
            var elapsedSeconds = elapsed.TotalSeconds;

            Console.WriteLine($"Action '{description}' took {elapsedSeconds} - that's {total/elapsedSeconds:0.#} /s");
        }

        static void Print(string text)
        {
            Console.WriteLine(text);
        }

        static void Pause()
        {
            Console.WriteLine("Press ENTER to continue...");
            Console.ReadLine();
        }

        class MessageWithString
        {
            public string Text { get; }

            public MessageWithString(string text)
            {
                Text = text;
            }
        }
    }
}
