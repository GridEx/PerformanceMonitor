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
    internal class MultiClientManager
    {
		public MultiClientManager(ManualResetEventSlim enviromentExitWait, ManualResetEventSlim processesStartedEvent)
		{
			_enviromentExitWait = enviromentExitWait;
			_processesStartedEvent = processesStartedEvent;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetLatencyStatistic(out long totalIntervals, out double latency90, out double latency95, out double latency99)
        {
            latency90 = latency95 = latency99 = 0;
            long[] intervals =  _clients.SelectMany(client => client.GetLatencyTimesAndClear()).ToArray();
            totalIntervals = intervals.LongLength;
            if (intervals.Any())
            {
                Array.Sort(intervals);
                latency90 = intervals[(int)((totalIntervals - 1) * 0.90)];
                latency95 = intervals[(int)((totalIntervals - 1) * 0.95)];
                latency99 = intervals[(int)((totalIntervals - 1) * 0.99)];
            }
        }
        
        public event OnException OnException;

        public int ConnectionsCount { get => _clients.Where(client => client.IsConnected).Count(); }
        
        public void Run(string hftServerAddress, int hftServerPort, long publisherCount, long limitOfOrdersPerSecond = 0)
        {
            for (var i = 0; i < publisherCount; ++i)
            {
                var userId = DateTime.Now.ToFileTimeUtc() + i;
                ManualResetEventSlim clientStartedEvent = new ManualResetEventSlim();
                _clientsStartedEvents.Add(clientStartedEvent);
                ManualResetEventSlim clientFinishedEvent = new ManualResetEventSlim();
                _clientsFinishedEvents.Add(clientFinishedEvent);
                ClientHft client = new ClientHft(userId, clientFinishedEvent, clientStartedEvent, this);
                client.OnException += ClientOnException;
                _clients.Add(client);
            }
            Random random = new Random(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0));
            ManualResetEventSlim canStartEvent = new ManualResetEventSlim();
            _clients.ForEach(client => client.Run(hftServerAddress, hftServerPort, ref _cancellationTokenSource, ref canStartEvent, ref random, limitOfOrdersPerSecond));

            Thread.Sleep(5000);

            canStartEvent.Set();

            _clientsStartedEvents.ForEach(ev => ev.Wait());

            _processesStartedEvent.Set();
            _manualResetEvent.Set();

            _taskCheckTimer = new Timer(TimerElapsed, new object(), 0, 5000);

            _simulationEnded.Wait();
            _enviromentExitWait.Set();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadProcessedOrderValue()
        {
            return _clients.Sum(client => client.ReadProcessedOrderValue());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetProcessedOrders()
        {
            return _clients.Sum(client => client.ResetProcessedOrders());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetCancelledOrders()
        {
            return _clients.Sum(client => client.ResetCancelledOrders());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetCreatedOrders()
        {
            return _clients.Sum(client => client.ResetCreatedOrders());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetExecutedOrders()
        {
            return _clients.Sum(client => client.ResetExecutedOrders());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetCompletedOrders()
        {
            return _clients.Sum(client => client.ResetCompletedOrders());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetRejectedOrders()
        {
            return _clients.Sum(client => client.ResetRejectedOrders());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ResetRejectedRequests()
        {
            return _clients.Sum(client => client.ResetRejectedRequests());
        }

        public long ResetSendOrders()
        {
            return _clients.Sum(client => client.ResetSendOrders());
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
			foreach (var client in _clients)
			{
				client.OnException -= ClientOnException;
			}
            _clients.Clear();
        }

        public void ForceStop()
        {
            _cancellationTokenSource.Cancel();
            foreach (var client in _clients)
            {
                client.OnException -= ClientOnException;
                client.ForceStop();
            }
            _clients.Clear();
        }

        public void ClearCache()
        {
            _clients.ForEach(client => client.ClearCache());
        }

		private void ClientOnException(long userID, string message)
		{
			OnException?.Invoke(userID, message);
		}

		private void TimerElapsed(object state)
		{
			if (!_clients.Any() || _clients.All(t => t.IsCompleted) || ConnectionsCount == 0 || _clientsFinishedEvents.All(ev => ev.IsSet))
			{
				_enviromentExitWait.Set();
				_taskCheckTimer.Dispose();
				_simulationEnded.Set();
			}
		}

		private readonly List<ManualResetEventSlim> _clientsStartedEvents = new List<ManualResetEventSlim>();
		private readonly List<ManualResetEventSlim> _clientsFinishedEvents = new List<ManualResetEventSlim>();
		private readonly Stopwatch _latencyStopwatch = new Stopwatch();
		private readonly List<ClientHft> _clients = new List<ClientHft>();
		private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
		private readonly ManualResetEvent _manualResetEvent = new ManualResetEvent(false);
		private readonly ManualResetEventSlim _simulationEnded = new ManualResetEventSlim();

		private ManualResetEventSlim _enviromentExitWait;
		private ManualResetEventSlim _processesStartedEvent;

		private Timer _taskCheckTimer;
	}
}
