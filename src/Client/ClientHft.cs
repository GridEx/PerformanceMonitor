
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
using System.Collections.Concurrent;

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
			long[] returndValues = _latencyOrdersCreated.Select(l => l.OrderCreatedTime - l.SendTime).ToArray();
			_latencyOrdersCreated = new ConcurrentBag<Latency>();
			return returndValues;
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

		public void Run(
			string hftServerAddress, 
			int hftServerPort, 
			ref CancellationTokenSource cancellationTokenSource, 
			ref ManualResetEventSlim canStartEvent, 
			ref Random random,
			long limitOfUnansweredOrders = 0)
		{
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
					Interlocked.Increment(ref _rejectedRequests);
					CalculateOrderProcessed(_hftSocket, 1);
				};

				_hftSocket.OnUserTokenAccepted += (socket, eventArgs) =>
				{

				};

				_hftSocket.OnUserTokenRejected += (socket, eventArgs) =>
				{

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

				_hftSocket.OnMarketInfo += (socket, eventArgs) =>
				{

				};

				_hftSocket.OnOrderCreated += (socket, eventArgs) =>
				{
					long time = _latencyStopwatch.ElapsedMilliseconds;
					Interlocked.Increment(ref _createdOrders);
					if (_latencyOrdersSended.TryRemove(eventArgs.RequestId, out Latency l))
					{
						l.OrderCreatedTime = time;
						_latencyOrdersCreated.Add(l);
					}
				};

				_hftSocket.OnOrderRejected += (socket, eventArgs) =>
				{
					if (_latencyOrdersSended.TryRemove(eventArgs.RequestId, out Latency l))
					{
						l.OrderCreatedTime = _latencyStopwatch.ElapsedMilliseconds;
						_latencyOrdersCreated.Add(l);
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

			var batchSize = _random.Next(10, 20);

			_runOrderRushTask = Task.Factory.StartNew(
				() => OrderRushLoop(_hftSocket, batchSize),
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
			_latencyOrdersCreated = new ConcurrentBag<Latency>();
		}

		private void CalculateOrderProcessed(HftSocket hftSocket, long orders)
		{
			Interlocked.Add(ref _processedOrders, orders);
			Interlocked.Add(ref _receivedOrders, orders);
		}

		private void OrderRushLoop(HftSocket socket, int batchSize)
		{
			_manualResetEvent.Wait();
			_processesStartedEvent.Set();

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
					var price = _random.Next(10000000, 10020001) * 0.00000001;
					var volume = _random.Next(10000, 100001) * 0.000001;

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
		private ConcurrentBag<Latency> _latencyOrdersCreated = new ConcurrentBag<Latency>();
		private MultiClientManager _clientManager;
		private Stopwatch _latencyStopwatch = new Stopwatch();
	}
}