
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

    class ClientHft
    {
        const int countMaxChachedOrders = 1000000;
        ConcurrentDictionary<long, Latency> latencyOrdersSended = new ConcurrentDictionary<long, Latency>(2, countMaxChachedOrders);
        ConcurrentBag<Latency> latencyOrdersCreated = new ConcurrentBag<Latency>();

        Stopwatch latencyStopwatch = new Stopwatch();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long[] GetLatencyTimesAndClear()
        {
            long[] returndValues = latencyOrdersCreated.Select(l => l.orderCreatedTime - l.sendTime).ToArray();
            latencyOrdersCreated = new ConcurrentBag<Latency>();
            return returndValues;
        }

        public event OnException onException;

        Random _random;
        CancellationTokenSource _cancellationTokenSource;
        ManualResetEventSlim _manualResetEvent = new ManualResetEventSlim();

        long _cancelledOrders = 0;
        long _createdOrders = 0;
        long _executedOrders = 0;
        long _completedOrders = 0;
        long _rejectedOrders = 0;
        long _rejectedRequests = 0;
        long _processedOrders = 0;
        long sendOrders = 0;

        Task runHftSocketTask;
        Task runOrderRushTask;

        HftSocket hftSocket;

        ManualResetEventSlim _enviromentExitWait;
        ManualResetEventSlim _processesStartedEvent;

        long limitOfUnansweredOrders;
        long receivedOrders;

        public readonly long clientID;

        public bool IsCompleted { get => (runHftSocketTask == null || runHftSocketTask.IsCanceled || runHftSocketTask.IsCompleted || runHftSocketTask.IsFaulted) &&
                (runOrderRushTask == null || runOrderRushTask.IsCanceled || runOrderRushTask.IsCompleted || runOrderRushTask.IsFaulted); }

        public bool IsConnected { get => (hftSocket == null || hftSocket.IsConnected); }
        MultiClientManager clientManager;
        public ClientHft(long clientID, ManualResetEventSlim enviromentExitWait, ManualResetEventSlim processesStartedEvent,
            MultiClientManager clientManager)
        {
            this.clientID = clientID;
            
            _enviromentExitWait = enviromentExitWait;
            _processesStartedEvent = processesStartedEvent;

            this.clientManager = clientManager;
        }

        public void Run(string hftServerAddress, int hftServerPort, ref CancellationTokenSource cancellationTokenSource, ref ManualResetEventSlim canStartEvent, ref Random random,
            long limitOfUnansweredOrders = 0)
        {
            this.receivedOrders = 0;
            this.limitOfUnansweredOrders = limitOfUnansweredOrders;
            latencyOrdersSended = new ConcurrentDictionary<long, Latency>(2, limitOfUnansweredOrders == 0 ? countMaxChachedOrders : (int)limitOfUnansweredOrders);

            this._random = random; 

            _manualResetEvent = canStartEvent;
            _cancellationTokenSource = cancellationTokenSource;

            hftSocket = new HftSocket();

            void RunHftSocket()
            {
                IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse(hftServerAddress).MapToIPv4(), hftServerPort);

                hftSocket.OnException += (socket, exception) =>
                {
                    try
                    {
                        if (socket.IsConnected)
                            socket.Disconnect();
                    }
                    catch { }

                    onException?.Invoke(clientID, exception.ToString());
                };

                hftSocket.OnRequestRejected += (socket, eventArgs) =>
                {
                    Interlocked.Increment(ref _rejectedRequests);
                    CalculateOrderProcessed(hftSocket, 1);
                };

                hftSocket.OnUserTokenAccepted += (socket, eventArgs) =>
                {
                    
                };

                hftSocket.OnUserTokenRejected += (socket, eventArgs) =>
                {

                };

                hftSocket.OnAllOrdersCancelled += (socket, eventArgs) =>
                {
                    var cancelledOrders = Interlocked.Add(ref _cancelledOrders, eventArgs.Amount);
                    CalculateOrderProcessed(hftSocket, eventArgs.Amount + 1);
                };

                hftSocket.OnOrderCancelled += (socket, eventArgs) =>
                {
                    var cancelledOrders = Interlocked.Increment(ref _cancelledOrders);
                    CalculateOrderProcessed(hftSocket, 1);
                };

                hftSocket.OnMarketInfo += (socket, eventArgs) =>
                {

                };

                hftSocket.OnOrderCreated += (socket, eventArgs) =>
                {
                    long time = latencyStopwatch.ElapsedMilliseconds;
                    Interlocked.Increment(ref _createdOrders);
                    if (latencyOrdersSended.TryRemove(eventArgs.RequestId, out Latency l))
                    {
                        l.orderCreatedTime = time;
                        latencyOrdersCreated.Add(l);
                    }
                };

                hftSocket.OnOrderRejected += (socket, eventArgs) =>
                {
                    if (latencyOrdersSended.TryRemove(eventArgs.RequestId, out Latency l))
                    {
                        l.orderCreatedTime = latencyStopwatch.ElapsedMilliseconds;
                        latencyOrdersCreated.Add(l);
                    }
                    var rejectedOrders = Interlocked.Increment(ref _rejectedOrders);
                    CalculateOrderProcessed(hftSocket, 1);
                };

                hftSocket.OnConnectionTooSlow += (socket, eventArgs) =>
                {
                    onException?.Invoke(clientID, "Connection is too slow. Connection was closed.");
                };

                hftSocket.OnOrderExecuted += (socket, eventArgs) =>
                {
                    var executedOrders = Interlocked.Increment(ref _executedOrders);
                    if (eventArgs.IsCompleted)
                    {
                        var completedOrders = Interlocked.Increment(ref _completedOrders);
                        CalculateOrderProcessed(hftSocket, 1);
                    }
                };

                hftSocket.OnDisconnected += (socket) =>
                {
                    _enviromentExitWait.Set();
                };


                try
                {
                    hftSocket.Connect(serverEndpoint);
                    hftSocket.Send(new UserToken(0, clientID));
                    hftSocket.WaitResponses(_cancellationTokenSource.Token);
                    hftSocket.Disconnect();
                }
                catch
                {
                    try
                    {
                        hftSocket.Dispose();
                    }
                    catch { }
                }
                finally
                {
                    _enviromentExitWait.Set();
                }
            }

            runHftSocketTask = Task.Factory.StartNew(
                () => RunHftSocket(),
                TaskCreationOptions.LongRunning);

            var batchSize = _random.Next(10, 20);

            runOrderRushTask = Task.Factory.StartNew(
                () => OrderRushLoop(hftSocket, batchSize),
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

        private void CalculateOrderProcessed(HftSocket hftSocket, long orders)
        {
            Interlocked.Add(ref _processedOrders, orders);
            Interlocked.Add(ref receivedOrders, orders);
        }

        private void OrderRushLoop(HftSocket socket, int batchSize)
        {
            _manualResetEvent.Wait();
            _processesStartedEvent.Set();

            double _processedOrdersDouble = _processedOrders;
            var batchCounter = 0;
            var requestId = 0L;
            latencyStopwatch.Start();
            SpinWait spinWait = new SpinWait();
            while (!_cancellationTokenSource.IsCancellationRequested && socket.IsConnected)
            {
                if (batchCounter < batchSize)
                {
                    if (limitOfUnansweredOrders != 0 && (requestId - Interlocked.Read(ref receivedOrders)) >= limitOfUnansweredOrders)
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

                    if (latencyOrdersSended.Count < countMaxChachedOrders)
                        latencyOrdersSended.TryAdd(requestId, new Latency(requestId, latencyStopwatch.ElapsedMilliseconds));

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
            latencyStopwatch.Stop();
            ClearCache();
        }

        public void ForceStop()
        {
            try
            {
                hftSocket.Disconnect();
            }
            catch { }

            ClearCache();
        }

        public void ClearCache()
        {
            latencyOrdersSended.Clear();
            latencyOrdersCreated= new ConcurrentBag<Latency>();
        }
    }
}                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   