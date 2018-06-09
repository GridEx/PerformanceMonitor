using GridEx.API;
using GridEx.API.Requests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace GridEx.PerformanceMonitor.Client
{
    class MultiClientManager
    {
        Stopwatch latencyStopwatch = new Stopwatch();

        List<ClientHft> clients = new List<ClientHft>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetLatencyStatistic(out long totalIntervals, out double latency90, out double latency95, out double latency9999)
        {
            latency90 = latency95 = latency9999 = 0;
            long[] intervals =  clients.SelectMany(client => client.GetLatencyTimesAndClear()).ToArray();
            totalIntervals = intervals.LongLength;
            if (intervals.Any())
            {
                Array.Sort(intervals);
                latency90 = intervals[(int)((totalIntervals - 1) * 0.90)];
                latency95 = intervals[(int)((totalIntervals - 1) * 0.95)];
                latency9999 = intervals[(int)((totalIntervals - 1) * 0.99)];
            }
        }
        
        public event OnException onException;

        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        ManualResetEvent _manualResetEvent = new ManualResetEvent(false);

        ManualResetEventSlim _enviromentExitWait;
        ManualResetEventSlim _processesStartedEvent;

        ManualResetEventSlim _simulationEnded = new ManualResetEventSlim();

        Timer TaskCheckTimer;

        public int ConnectionsCount { get => clients.Where(client => client.IsConnected).Count(); }
        
        public MultiClientManager(ManualResetEventSlim enviromentExitWait, ManualResetEventSlim processesStartedEvent)
        {
            _enviromentExitWait = enviromentExitWait;
            _processesStartedEvent = processesStartedEvent;
        }

        void TimerElapsed(object state)
        {
            if (!clients.Any() || clients.All(t => t.IsCompleted) || ConnectionsCount == 0 || clientsFinishedEvents.All(ev => ev.IsSet))
            {
                _enviromentExitWait.Set();
                TaskCheckTimer.Dispose();
                _simulationEnded.Set();
            }
        }

        List<ManualResetEventSlim> clientsStartedEvents = new List<ManualResetEventSlim>();
        List<ManualResetEventSlim> clientsFinishedEvents = new List<ManualResetEventSlim>();

        public void Run(string hftServerAddress, int hftServerPort, long publisherCount, long limitOfOrdersPerSecond = 0)
        {
            for (var i = 0; i < publisherCount; ++i)
            {
                var userId = DateTime.Now.ToFileTimeUtc() + i;
                ManualResetEventSlim clientStartedEvent = new ManualResetEventSlim();
                clientsStartedEvents.Add(clientStartedEvent);
                ManualResetEventSlim clientFinishedEvent = new ManualResetEventSlim();
                clientsFinishedEvents.Add(clientFinishedEvent);
                ClientHft client = new ClientHft(userId, clientFinishedEvent, clientStartedEvent, this);
                client.onException += Client_onException;
                clients.Add(client);
            }
            Random random = new Random(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0));
            ManualResetEventSlim canStartEvent = new ManualResetEventSlim();
            clients.ForEach(client => client.Run(hftServerAddress, hftServerPort, ref _cancellationTokenSource, ref canStartEvent, ref random, limitOfOrdersPerSecond));

            Thread.Sleep(5000);

            canStartEvent.Set();

            clientsStartedEvents.ForEach(ev => ev.Wait());

            _processesStartedEvent.Set();
            _manualResetEvent.Set();

            TaskCheckTimer = new Timer(TimerElapsed, new object(), 0, 5000);

            _simulationEnded.Wait();
            _enviromentExitWait.Set();
        }

        private void Client_onException(long userID, string message)
        {
            onException?.Invoke(userID, message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadProcessedOrderValue()
        {
            return clients.Sum(client => client.ReadProcessedOrderValue());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetProcessedOrders()
        {
            return clients.Sum(client => client.ResetProcessedOrders());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetCancelledOrders()
        {
            return clients.Sum(client => client.ResetCancelledOrders());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetCreatedOrders()
        {
            return clients.Sum(client => client.ResetCreatedOrders());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetExecutedOrders()
        {
            return clients.Sum(client => client.ResetExecutedOrders());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetCompletedOrders()
        {
            return clients.Sum(client => client.ResetCompletedOrders());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetRejectedOrders()
        {
            return clients.Sum(client => client.ResetRejectedOrders());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetRejectedRequests()
        {
            return clients.Sum(client => client.ResetRejectedRequests());
        }
        public long ResetSendOrders()
        {
            return clients.Sum(client => client.ResetSendOrders());
        }
        public void Stop()
        {
            _cancellationTokenSource.Cancel();
            foreach (var client in clients)
                client.onException -= Client_onException;
            clients.Clear();
        }

        public void ForceStop()
        {
            _cancellationTokenSource.Cancel();
            foreach (var client in clients)
            {
                client.onException -= Client_onException;
                client.ForceStop();
            }
            clients.Clear();
        }

        public void ClearCache()
        {
            clients.ForEach(client => client.ClearCache());
        }
    }
}
