using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;
using GridEx.API.Trading;
using PerformanceMonitor.Utils;
using GridEx.API.Trading.Requests;
using GridEx.API.Trading.Responses;
using GridEx.API.Trading;

namespace GridEx.PerformanceMonitor.Client
{
	public delegate void OnException(long userID, string message);

	internal class ClientHft
	{
		public ClientHft(
			long clientID, 
			ManualResetEventSlim enviromentExitWait, 
			ManualResetEventSlim processesStartedEvent,
			MultiClientManager clientManager)
		{
			ClientId = clientID;

			_enviromentExitWait = enviromentExitWait;
			_processesStartedEvent = processesStartedEvent;

			_clientManager = clientManager;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long[] GetLatencyTimesAndClear()
		{
            ConcurrentQueue<Latency> __latencyOrdersCreated = _latencyOrdersCreated;
            _latencyOrdersCreated = new ConcurrentQueue<Latency>();
            return __latencyOrdersCreated.Select(l => l.OrderCreatedTime - l.SendTime).ToArray();
        }

		public event OnException OnException;

		public readonly long ClientId;

		public bool IsCompleted
		{
			get => (_runHftSocketTask == null 
				|| _runHftSocketTask.IsCanceled 
				|| _runHftSocketTask.IsCompleted 
				|| _runHftSocketTask.IsFaulted) 
			&& (_runOrderRushTask == null 
				|| _runOrderRushTask.IsCanceled 
				|| _runOrderRushTask.IsCompleted 
				|| _runOrderRushTask.IsFaulted);
		}

		public bool IsConnected { get => (_hftSocket == null || _hftSocket.IsConnected); }

		public void SetPriceStrategy(PriceVolumeStrategyAbstract priceVolumeStrategy)
		{
			Interlocked.Exchange(ref _priceStrategy, (PriceVolumeStrategyAbstract)priceVolumeStrategy.Clone());
		}

		public void SetVolumeStrategy(PriceVolumeStrategyAbstract priceVolumeStrategy)
		{
			Interlocked.Exchange(ref _volumeStrategy, (PriceVolumeStrategyAbstract)priceVolumeStrategy.Clone());
		}

		public void Run(
			string hftServerAddress, 
			int hftServerPort, 
			ref CancellationTokenSource cancellationTokenSource, 
			ref ManualResetEventSlim canStartEvent, 
			ref Random random,
			PriceVolumeStrategyAbstract priceStrategy,
			PriceVolumeStrategyAbstract volumeStrategy,
			long limitOfUnansweredOrders = 0)
		{
			SetPriceStrategy(priceStrategy);
			SetVolumeStrategy(volumeStrategy);

			_receivedOrders = 0;
			_limitOfUnansweredOrders = limitOfUnansweredOrders;
			_latencyOrdersSended = new ConcurrentDictionary<long, Latency>(2, limitOfUnansweredOrders == 0 ? CountMaxChachedOrders : (int)limitOfUnansweredOrders);

			_random = random;

			_manualResetEvent = canStartEvent;
			_cancellationTokenSource = cancellationTokenSource;

			_hftSocket = new HftSocket();

			void RunHftSocket()
			{
				IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse(hftServerAddress).MapToIPv4(), hftServerPort);

				_hftSocket.OnException += (socket, exception) =>
				{
					OnException?.Invoke(ClientId, exception.Message);
					try
					{
						if (socket.IsConnected)
							socket.Disconnect();
					}
					catch { }

					OnException?.Invoke(ClientId, exception.ToString());
				};

				_hftSocket.OnRequestRejected += (socket, eventArgs) =>
				{
					SendMessageAboutErrorIfNeed("Req rej", eventArgs.RejectCode);

					Interlocked.Increment(ref _rejectedRequests);
					CalculateOrderProcessed(_hftSocket, 1);
				};

				_hftSocket.OnUserTokenAccepted += (socket, eventArgs) =>
				{
					
				};

				_hftSocket.OnUserTokenRejected += (socket, eventArgs) =>
				{
					SendMessageAboutErrorIfNeed($"UTok({ eventArgs.Token}) rej", eventArgs.RejectCode);
				};

				_hftSocket.OnAllOrdersCancelled += (socket, eventArgs) =>
				{
					var cancelledOrders = Interlocked.Add(ref _cancelledOrders, eventArgs.Amount);
					CalculateOrderProcessed(_hftSocket, eventArgs.Amount + 1);
				};

				_hftSocket.OnOrderCancelled += (socket, eventArgs) =>
				{
					var cancelledOrders = Interlocked.Increment(ref _cancelledOrders);
					CalculateOrderProcessed(_hftSocket, 1);
				};

				_hftSocket.OnOrderCreated += (socket, eventArgs) =>
				{
					long time = _latencyStopwatch.ElapsedMilliseconds;
					Interlocked.Increment(ref _createdOrders);
					if (_latencyOrdersSended.TryRemove(eventArgs.RequestId, out Latency l))
					{
						l.OrderCreatedTime = time;
						_latencyOrdersCreated.Enqueue(l);
                    }
				};

				_hftSocket.OnOrderRejected += (socket, eventArgs) =>
				{
					SendMessageAboutErrorIfNeed($"Or rej", eventArgs.RejectCode);

					if (_latencyOrdersSended.TryRemove(eventArgs.RequestId, out Latency l))
					{
						l.OrderCreatedTime = _latencyStopwatch.ElapsedMilliseconds;
                        _latencyOrdersCreated.Enqueue(l);
                    }
					var rejectedOrders = Interlocked.Increment(ref _rejectedOrders);
					CalculateOrderProcessed(_hftSocket, 1);
				};

				_hftSocket.OnConnectionTooSlow += (socket, eventArgs) =>
				{
					OnException?.Invoke(ClientId, "Connection is too slow. Connection was closed.");
				};

				_hftSocket.OnOrderExecuted += (socket, eventArgs) =>
				{
					var executedOrders = Interlocked.Increment(ref _executedOrders);
					if (eventArgs.IsCompleted)
					{
						var completedOrders = Interlocked.Increment(ref _completedOrders);
						CalculateOrderProcessed(_hftSocket, 1);
					}
				};

				_hftSocket.OnDisconnected += (socket) =>
				{
					_enviromentExitWait.Set();
				};


				try
				{
					_hftSocket.Connect(serverEndpoint);
					_hftSocket.Send(new UserToken(0, ClientId));
					_hftSocket.WaitResponses(_cancellationTokenSource.Token);
					_hftSocket.Disconnect();
				}
				catch
				{
					try
					{
						_hftSocket.Dispose();
					}
					catch { }
				}
				finally
				{
					_enviromentExitWait.Set();
				}
			}

			_runHftSocketTask = Task.Factory.StartNew(
				() => RunHftSocket(),
				TaskCreationOptions.LongRunning);

			var maxBatchSize = (int)Math.Min(20, _limitOfUnansweredOrders > 0 ? _limitOfUnansweredOrders : 10);

			_runOrderRushTask = Task.Factory.StartNew(
				() => OrderRushLoop(_hftSocket, maxBatchSize),
				TaskCreationOptions.LongRunning);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long ReadProcessedOrderValue()
		{
			return Interlocked.Read(ref _processedOrders);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long ResetProcessedOrders()
		{
			return Interlocked.Exchange(ref _processedOrders, 0L);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long ResetCancelledOrders()
		{
			return Interlocked.Exchange(ref _cancelledOrders, 0L);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long ResetCreatedOrders()
		{
			return Interlocked.Exchange(ref _createdOrders, 0L);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long ResetExecutedOrders()
		{
			return Interlocked.Exchange(ref _executedOrders, 0L);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long ResetCompletedOrders()
		{
			return Interlocked.Exchange(ref _completedOrders, 0L);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long ResetRejectedOrders()
		{
			return Interlocked.Exchange(ref _rejectedOrders, 0L);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long ResetRejectedRequests()
		{
			return Interlocked.Exchange(ref _rejectedRequests, 0L);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public long ResetSendOrders()
		{
			return Interlocked.Exchange(ref sendOrders, 0L);
		}

		public void ForceStop()
		{
			try
			{
				_hftSocket.Disconnect();
			}
			catch { }

			ClearCache();
		}

		public void ClearCache()
		{
            _latencyOrdersSended.Clear();
            _latencyOrdersSended = new ConcurrentDictionary<long, Latency>();
            _latencyOrdersCreated = new ConcurrentQueue<Latency>();
		}

		private void CalculateOrderProcessed(HftSocket hftSocket, long orders)
		{
			Interlocked.Add(ref _processedOrders, orders);
			Interlocked.Add(ref _receivedOrders, orders);
		}

		private void OrderRushLoop(HftSocket socket, int maxBatchSize)
		{
			_manualResetEvent.Wait();
			_processesStartedEvent.Set();

			maxBatchSize = Math.Max(1, maxBatchSize);

			var minBatchSize = Math.Max(maxBatchSize - 10, 1);
			var random = new Random(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0));
			var batchSize = random.Next(minBatchSize, maxBatchSize);

			double _processedOrdersDouble = _processedOrders;
			var batchCounter = 0;
			var requestId = 0L;
			_latencyStopwatch.Start();
			SpinWait spinWait = new SpinWait();
			while (!_cancellationTokenSource.IsCancellationRequested && socket.IsConnected)
			{
				if (batchCounter < batchSize)
				{
					if (_limitOfUnansweredOrders != 0 && (requestId - Interlocked.Read(ref _receivedOrders)) >= _limitOfUnansweredOrders)
					{
						spinWait.SpinOnce();
						continue;
					}

					var orderType = batchCounter % 2L == 0L ? RequestTypeCode.SellLimitOrder : RequestTypeCode.BuyLimitOrder;
					var price = _priceStrategy.ProduceValue();
					var volume = _volumeStrategy.ProduceValue();

					if (orderType == RequestTypeCode.BuyLimitOrder)
					{
						socket.Send(new BuyLimitOrder(requestId, price, volume));
					}
					else
					{
						socket.Send(new SellLimitOrder(requestId, price, volume));
					}
					++batchCounter;

					if (_latencyOrdersSended.Count < CountMaxChachedOrders)
						_latencyOrdersSended.TryAdd(requestId, new Latency(requestId, _latencyStopwatch.ElapsedMilliseconds));

					requestId++;
				}
				else
				{
					batchSize = random.Next(minBatchSize, maxBatchSize);

					socket.Send(new CancelAllOrders(requestId++));

					batchCounter = 0;
				}
				Interlocked.Increment(ref sendOrders);
			}

			if (socket.IsConnected)
			{
				socket.Send(new CancelAllOrders(requestId++));
				Interlocked.Increment(ref sendOrders);
			}
			_latencyStopwatch.Stop();
			ClearCache();
		}

		private void IncreaseSameErrors()
		{
			Interlocked.Increment(ref _sameErrors);
			if (_sameErrors >= 1000)
			{
				OnException?.Invoke(ClientId, $"{Interlocked.Exchange(ref _sameErrors, 0)} same");
			}
		}

		private void SendMessageAboutErrorIfNeed(string message, RejectReasonCode rejectReasonCode)
		{
			if (_lastErrorType != rejectReasonCode)
			{
				var sameErrorsCount = Interlocked.Exchange(ref _sameErrors, 0);
				_lastErrorType = rejectReasonCode;
				OnException?.Invoke(ClientId,
					sameErrorsCount != 0
					? $"{message} (+{sameErrorsCount}): {rejectReasonCode.ToString()}"
					: $"{message}: {rejectReasonCode.ToString()}");
			}
			else
			{
				IncreaseSameErrors();
			}
		}

		private Random _random;
		private CancellationTokenSource _cancellationTokenSource;
		private ManualResetEventSlim _manualResetEvent = new ManualResetEventSlim();

		private long _cancelledOrders = 0;
		private long _createdOrders = 0;
		private long _executedOrders = 0;
		private long _completedOrders = 0;
		private long _rejectedOrders = 0;
		private long _rejectedRequests = 0;
		private long _processedOrders = 0;
		private long sendOrders = 0;

		private Task _runHftSocketTask;
		private Task _runOrderRushTask;

		private HftSocket _hftSocket;

		private ManualResetEventSlim _enviromentExitWait;
		private ManualResetEventSlim _processesStartedEvent;

		private long _limitOfUnansweredOrders;
		private long _receivedOrders;

		private const int CountMaxChachedOrders = 1000000;
		private ConcurrentDictionary<long, Latency> _latencyOrdersSended = new ConcurrentDictionary<long, Latency>(2, CountMaxChachedOrders);
		private ConcurrentQueue<Latency> _latencyOrdersCreated = new ConcurrentQueue<Latency>();
		private MultiClientManager _clientManager;
		private Stopwatch _latencyStopwatch = new Stopwatch();

		private PriceVolumeStrategyAbstract _priceStrategy;
		private PriceVolumeStrategyAbstract _volumeStrategy;

		private RejectReasonCode _lastErrorType = RejectReasonCode.Ok;
		private long _sameErrors = 0;
	}
}