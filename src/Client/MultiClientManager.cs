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
using PerformanceMonitor.Utils;

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
        public void GetLatencyStatistic(out long totalIntervals, out double latency90, out double latency95, out double latency99, out long minimumLatency)
        {
            latency90 = latency95 = latency99 = minimumLatency = 0;
            long[] intervals =  _clients.SelectMany(client => client.GetLatencyTimesAndClear()).ToArray();
            totalIntervals = intervals.LongLength;
            if (intervals.Any())
            {
                Array.Sort(intervals);
				minimumLatency = intervals[0];
				latency90 = intervals[(int)((totalIntervals - 1) * 0.90)];
                latency95 = intervals[(int)((totalIntervals - 1) * 0.95)];
                latency99 = intervals[(int)((totalIntervals - 1) * 0.99)];
            }
        }
        
        public event OnException OnException;

        public int ConnectionsCount { get => _clients.Where(client => client.IsConnected).Count(); }
        
        public void Run(string hftServerAddress, int hftServerPort, long publisherCount,
			PriceVolumeStrategyAbstract priceStrategy,
			PriceVolumeStrategyAbstract volumeStrategy,
			long limitOfOrdersPerSecond = 0)
        {
			Random random = new Random(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0));
			_priceStrategy = priceStrategy ?? new PriceVolumeStrategyRandom(random, PriceVolumeStrategyAbstract.BottomPrice, PriceVolumeStrategyAbstract.TopPrice);
			_volumeStrategy = volumeStrategy ?? new PriceVolumeStrategyRandom(random, PriceVolumeStrategyAbstract.BottomVolume, PriceVolumeStrategyAbstract.TopVolume);

			var userIdStart = DateTime.Now.ToFileTimeUtc();
			for (var i = 0; i < publisherCount; ++i)
            {
				var userId = userIdStart + i;
				ManualResetEventSlim clientStartedEvent = new ManualResetEventSlim();
                _clientsStartedEvents.Add(clientStartedEvent);
                ManualResetEventSlim clientFinishedEvent = new ManualResetEventSlim();
                _clientsFinishedEvents.Add(clientFinishedEvent);
                ClientHft client = new ClientHft(userId, clientFinishedEvent, clientStartedEvent, this);
                client.OnException += ClientOnException;
                _clients.Add(client);
            }
            
            ManualResetEventSlim canStartEvent = new ManualResetEventSlim();

			try
			{
				for (int i = 0; i < _clients.Count; i++)
				{
					_clients[i].Run(hftServerAddress, hftServerPort, ref _cancellationTokenSource, ref canStartEvent, ref random,
						CloneStrategy(ref _priceStrategy, i + 1),
						CloneStrategy(ref _volumeStrategy, i + 1),
						limitOfOrdersPerSecond);
				}
			}
			catch
			{
				canStartEvent.Set();
				_clientsStartedEvents.ForEach(ev => ev.Set());
				_processesStartedEvent.Set();
				_manualResetEvent.Set();
				_simulationEnded.Set();
				_enviromentExitWait.Set();

				return;
			}

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
            _enviromentExitWait.Set();
            _simulationEnded.Set();
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
            ClientHft[] __clients = _clients.ToArray();
            if (!__clients.Any() || __clients.All(t => t.IsCompleted) || __clients.All(c => c.IsConnected) || _clientsFinishedEvents.All(ev => ev.IsSet))
			{
				_enviromentExitWait.Set();
				_taskCheckTimer.Dispose();
				_simulationEnded.Set();
			}
		}

		public void SetPriceStrategy(PriceVolumeStrategyAbstract priceVolumeStrategy)
		{
			_clients.ForEach(client => client.SetPriceStrategy(priceVolumeStrategy));
		}

		public void SetVolumeStrategy(PriceVolumeStrategyAbstract priceVolumeStrategy)
		{
			_clients.ForEach(client => client.SetVolumeStrategy(priceVolumeStrategy));
		}

		private PriceVolumeStrategyAbstract CloneStrategy(ref PriceVolumeStrategyAbstract strategy, int strategyNumber)
		{
			PriceVolumeStrategySinus sinusStrategy = strategy as PriceVolumeStrategySinus;
			if (sinusStrategy != null)
			{
				return new PriceVolumeStrategySinus(sinusStrategy.Period, sinusStrategy.Minimum, sinusStrategy.Maximum,
					sinusStrategy.PhaseShift * strategyNumber);
			}
			else
			{
				return (PriceVolumeStrategyAbstract)strategy.Clone();
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

		private PriceVolumeStrategyAbstract _priceStrategy;
		private PriceVolumeStrategyAbstract _volumeStrategy;
	}
}