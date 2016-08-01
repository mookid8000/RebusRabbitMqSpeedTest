using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Logging;
using Rebus.RabbitMq;
#pragma warning disable 1998

namespace RebusRabbitMqSpeedTest
{
    class Program
    {
        static readonly string ApproxTwoKbOfData = new string('*', 2048);
        const string ConnectionString = "amqp://localhost";
        const string QueueName = "speedtest";
        const int NumberOfMessages = 10000;

        const int SendParallelism = 20;

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
                activator.Handle<string>(async str =>
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
                        o.SetNumberOfWorkers(0);
                        o.SetMaxParallelism(20);
                    })
                    .Start();

                Print("Bus started");

                var bus = activator.Bus;

                Pause();

                TakeTime("Sending messages", NumberOfMessages, () =>
                {
                    var messagesToSend = Enumerable.Repeat(ApproxTwoKbOfData, NumberOfMessages);
                    var queue = new ConcurrentQueue<string>(messagesToSend);

                    var sendThreads = Enumerable.Range(0, SendParallelism)
                        .Select(_ => new Thread(() =>
                        {
                            string nextMessage;
                            while (queue.TryDequeue(out nextMessage))
                            {
                                bus.SendLocal(nextMessage).Wait();
                            }
                        }))
                        .ToList();

                    sendThreads.ForEach(t => t.Start());
                    sendThreads.ForEach(t => t.Join());

                    //Task.Run(async () =>
                    //{
                    //    await Task.WhenAll(Enumerable.Range(0, NumberOfMessages)
                    //        .Select(i => bus.SendLocal(ApproxTwoKbOfData)));
                    //}).Wait();
                });

                TakeTime("Received messages", NumberOfMessages, () =>
                {
                    bus.Advanced.Workers.SetNumberOfWorkers(20);

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
    }
}
