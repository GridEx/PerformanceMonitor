using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using InteractiveDataDisplay.WPF;

using GridEx.PerformanceMonitor.Client;
using PerformanceMonitor.Config;
using PerformanceMonitor.Utils;
using PerformanceMonitor.Controls;

namespace GridEx.PerformanceMonitor
{
	public partial class MainWindow : Window, INotifyPropertyChanged
	{
		public int ConnectionCount { get => _client == null ? 0 : _client.ConnectionsCount; }

		public event PropertyChangedEventHandler PropertyChanged;

		public long MaxConnections
		{
			get => _maxConnections;
			set { _maxConnections = Math.Min(1024, Math.Max(1, value)); NotifyPropertyChanged("MaxConnections"); }
		}

		public long MaxOrdersPerSecond
		{
			get => _maxOrdersPerSecond;
			set { _maxOrdersPerSecond = value < (MaxConnections * 10) ? (MaxConnections * 10) : value; NotifyPropertyChanged("MaxOrdersPerSecond"); }
		}

		public int Frequency
		{
			get => _frequency;
			set { if (value > 0) { _frequency = value; _stepsPerMinute = 60 / _frequency; } }
		}

		public MainWindow()
		{
			OptionsConfig.Load(out _priceStrategy, out _volumeStrategy);

			if (_priceStrategy == null)
				_priceStrategy = new PriceVolumeStrategyRandom(PriceVolumeStrategyAbstract.BottomPrice, PriceVolumeStrategyAbstract.TopPrice);
			if (_volumeStrategy == null)
				_volumeStrategy = new PriceVolumeStrategyRandom(PriceVolumeStrategyAbstract.BottomVolume, PriceVolumeStrategyAbstract.TopVolume);

			Frequency = 1;

			InitializeComponent();

            ResetDatas();

            NotifyPropertyChanged("connectionCount");
		}

        private LineGraph CreateGraph(ref Plot plot, Brush brush, string tooltip)
		{
			var lineGraph = new LineGraph
			{
				Stroke = brush,
				Description = tooltip,
				StrokeThickness = 3
			};
			plot.Children.Add(lineGraph);
			return lineGraph;
        }

		private void UpdateAllPlots(long countOfIntervals, long minimumLatency)
		{
			_currentTpsGraph.Plot(_animatedX, _processedOrdersY);
            _averagePerformanceGraph.Plot(_animatedX, _averageY);
			_cancelledOrdersGraph.Plot(_animatedX, _cancelledOrdersY);
			_createdOrdersGraph.Plot(_animatedX, _createdOrdersY);
			_executedOrdersGraph.Plot(_animatedX, _executedOrdersY);
			_completedOrdersGraph.Plot(_animatedX, _completedOrdersY);
			_rejectedOrdersGraph.Plot(_animatedX, _rejectedOrdersY);
			_rejectedRequestsGraph.Plot(_animatedX, _rejectedRequestsY);
			_averageSendGraph.Plot(_animatedX, _averageSendY);
			_orderSendGraph.Plot(_animatedX, _ordersSendY);
			_latency90Graph.Plot(_animatedX, _latency90Y);
			_latency95Graph.Plot(_animatedX, _latency95Y);
			_latency99Graph.Plot(_animatedX, _latency99Y);
			latencyChart.BottomTitle = string.Format("Intervals: {0, -10}      Min. latency = {1} ms", countOfIntervals, minimumLatency);
		}

        void RepeairFieldOfView()
        {
            TPSchart.IsAutoFitEnabled = true;
            sendOrdersChart.IsAutoFitEnabled = true;
            cancelledOrdersChart.IsAutoFitEnabled = true;
            createdOrdersChart.IsAutoFitEnabled = true;
            executedOrdersChart.IsAutoFitEnabled = true;
            completedOrdersChart.IsAutoFitEnabled = true;
            rejectedOrdersChart.IsAutoFitEnabled = true;
            rejectedRequestsChart.IsAutoFitEnabled = true;
            latencyChart.IsAutoFitEnabled = true;
        }

		private void ResetDatas()
		{
            if (_currentTpsGraph == null)
            {
                _latency90Graph = CreateGraph(ref latencyPlot, Brushes.Red, "90%");
                _latency95Graph = CreateGraph(ref latencyPlot, Brushes.Green, "95%");
                _latency99Graph = CreateGraph(ref latencyPlot, Brushes.LightBlue, "99%");

                _averageSendGraph = CreateGraph(ref sendOrdersPlot, Brushes.MediumBlue, "Average perf/min");
                _orderSendGraph = CreateGraph(ref sendOrdersPlot, Brushes.DeepPink, "Orders send");

                _averagePerformanceGraph = CreateGraph(ref TPSPlot, Brushes.Blue, "Average perf/min");
                _currentTpsGraph = CreateGraph(ref TPSPlot, Brushes.DarkGreen, "Current TPS");

                _cancelledOrdersGraph = CreateGraph(ref cancelledOrdersPlot, Brushes.DarkOliveGreen, "Canceled orders");

                _createdOrdersGraph = CreateGraph(ref createdOrdersPlot, Brushes.SeaGreen, "Created orders");

                _executedOrdersGraph = CreateGraph(ref executedOrdersPlot, Brushes.DarkOrange, "Executed orders");

                _completedOrdersGraph = CreateGraph(ref completedOrdersPlot, Brushes.Brown, "Completed orders");

                _rejectedOrdersGraph = CreateGraph(ref rejectedOrdersPlot, Brushes.Orchid, "Rejected orders");

                _rejectedRequestsGraph = CreateGraph(ref rejectedRequestsPlot, Brushes.Red, "Rejected requests");
            }

            _animatedX = new double[_intervalInMinutes * _stepsPerMinute];
			_processedOrdersY = new double[_animatedX.Length];
			_averageY = new double[_animatedX.Length];
			_cancelledOrdersY = new double[_animatedX.Length];
			_createdOrdersY = new double[_animatedX.Length];
			_executedOrdersY = new double[_animatedX.Length];
			_completedOrdersY = new double[_animatedX.Length];
			_rejectedOrdersY = new double[_animatedX.Length];
			_rejectedRequestsY = new double[_animatedX.Length];
			_ordersSendY = new double[_animatedX.Length];
			_averageSendY = new double[_animatedX.Length];
			_latency90Y = new double[_animatedX.Length];
			_latency95Y = new double[_animatedX.Length];
			_latency99Y = new double[_animatedX.Length];

			for (int i = 0; i < _animatedX.Length; i++)
			{
				_animatedX[i] = i / (float)Frequency - (_intervalInMinutes * 60);
				_latency90Y[i] = _latency95Y[i] = _latency99Y[i] =
					_averageSendY[i] = _ordersSendY[i] =
					_cancelledOrdersY[i] = _createdOrdersY[i] = _executedOrdersY[i] = _completedOrdersY[i] = _rejectedOrdersY[i] = _rejectedRequestsY[i] =
					_averageY[i] = _processedOrdersY[i] = 0;
			}

            UpdateAllPlots(0, 0);
            RepeairFieldOfView();
        }
        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            IPWindow iPWindow = new IPWindow(ref App.connectionConfig) { Owner = this };
            iPWindow.ShowDialog();
        }

		private void startStopButton_Checked(object sender, RoutedEventArgs e)
		{
            RepeairFieldOfView();
            optionsContainer.IsEnabled = false;
			_stop = false;
			startStopButton.IsEnabled = false;
			startStopButton.Content = "Starting...";
			Dispatcher.Invoke(new Action(() =>
			{
				CreateSim();
				CreateDataCollectionThread();
			}), DispatcherPriority.Background);

		}

		private void startStopButton_Unchecked(object sender, RoutedEventArgs e)
		{
            _stop = true;

            startStopButton.IsEnabled = false;
			startStopButton.Content = "Stopping...";
            
            Dispatcher.BeginInvoke(new Action(() =>
			{
				processesStartedEvent.Reset();
				
				_exitWait.Wait(5000);
				if (!_exitWait.IsSet)
				{
                    _client.OnException -= Client_OnException;
                    _client.ForceStop();
					_exitWait.Set();
				}
				ResetDatas();
				NotifyPropertyChanged("connectionCount");
				startStopButton.Content = "Start";
				startStopButton.IsEnabled = true;
				optionsContainer.IsEnabled = true;
			}), DispatcherPriority.Background);
		}

		private void CollectData()
		{
			double totalOrderSend = 0;
			double totalOrdersProcessed = 1;

			processesStartedEvent.Wait(20000);

            if (ConnectionCount == 0)
            {
                Dispatcher.Invoke(new Action(() =>
                {
                    _client.OnException -= Client_OnException;
                    _client.Stop();

                    startStopButton.IsChecked = false;

                    MessageBox.Show(this, 
                        "Couldn't connect to server.\nPlease check destination IP and your network connection.", 
                        "Connection problem!", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Warning);
                }));
            }
            else
            {
                Dispatcher.Invoke(new Action(() =>
                {
                    startStopButton.Content = "Stop";
                    startStopButton.IsEnabled = true;
                }));

                var threadEvent = new ManualResetEventSlim(false);
                var stepsPassed = 0;
                var needCalculatePassedSteps = true;

                int pauseDelay = 1000 / Frequency;

                while (!_stop)
                {
                    if (_client == null)
                    {
                        continue;
                    }

                    var processedOrders = _client.ResetProcessedOrders();
                    totalOrdersProcessed += processedOrders;

                    for (int i = 0; i < _processedOrdersY.Length - 1; i++)
                    {
                        _processedOrdersY[i] = _processedOrdersY[i + 1];
                        _averageY[i] = _averageY[i + 1];
                        _cancelledOrdersY[i] = _cancelledOrdersY[i + 1];
                        _createdOrdersY[i] = _createdOrdersY[i + 1];
                        _executedOrdersY[i] = _executedOrdersY[i + 1];
                        _completedOrdersY[i] = _completedOrdersY[i + 1];
                        _rejectedOrdersY[i] = _rejectedOrdersY[i + 1];
                        _rejectedRequestsY[i] = _rejectedRequestsY[i + 1];
                        _ordersSendY[i] = _ordersSendY[i + 1];
                        _averageSendY[i] = _averageSendY[i + 1];
                        _latency90Y[i] = _latency90Y[i + 1];
                        _latency95Y[i] = _latency95Y[i + 1];
                        _latency99Y[i] = _latency99Y[i + 1];
                    }

                    _processedOrdersY[_processedOrdersY.Length - 1] = processedOrders;
                    _cancelledOrdersY[_cancelledOrdersY.Length - 1] = _client.ResetCancelledOrders();
                    _createdOrdersY[_createdOrdersY.Length - 1] = _client.ResetCreatedOrders();
                    _executedOrdersY[_executedOrdersY.Length - 1] = _client.ResetExecutedOrders();
                    _completedOrdersY[_completedOrdersY.Length - 1] = _client.ResetCompletedOrders();
                    _rejectedOrdersY[_rejectedOrdersY.Length - 1] = _client.ResetRejectedOrders();
                    _rejectedRequestsY[_rejectedRequestsY.Length - 1] = _client.ResetRejectedRequests();

                    totalOrderSend += (_ordersSendY[_ordersSendY.Length - 1] = _client.ResetSendOrders());

                    if (needCalculatePassedSteps)
                    {
                        stepsPassed++;
                        needCalculatePassedSteps = stepsPassed < _stepsPerMinute;
                    }
                    _averageY[_averageY.Length - 1] = CalculateAveragePerformance(stepsPassed, ref _processedOrdersY);
                    _averageSendY[_averageSendY.Length - 1] = CalculateAveragePerformance(stepsPassed, ref _ordersSendY);

                    _client.GetLatencyStatistic(out long countOfIntervals,
						out double latency90, out double latency95, out double latency9999,
						out long minimumLatency);
                    _latency90Y[_latency90Y.Length - 1] = latency90;
                    _latency95Y[_latency95Y.Length - 1] = latency95;
                    _latency99Y[_latency99Y.Length - 1] = latency9999;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        NotifyPropertyChanged("connectionCount");
                        UpdateAllPlots(countOfIntervals, minimumLatency);

                        tbTotalOrders.Text = string.Format("Orders Proc|Ave|Send:{0,10} | {1,10} | {2,-10} ({3})",
                            processedOrders, (long)_averageY[_averageY.Length - 1], (long)_ordersSendY[_ordersSendY.Length - 1], DateTime.Now);
                        tbTotalOrdersSend.Text = string.Format("Total  Proc|Send| % :{0,10} | {1,10} | {2:00.00}%",
                            totalOrdersProcessed, totalOrderSend, totalOrderSend == 0 ? 0 : (totalOrdersProcessed / totalOrderSend * 100));
                    }), DispatcherPriority.Normal);

                    threadEvent.Reset();
                    threadEvent.Wait(pauseDelay);
                }
                _client.OnException -= Client_OnException;
                _client.Stop();
            }
			GC.Collect(2, GCCollectionMode.Forced);
		}

		private void CreateDataCollectionThread()
		{
			_dataCollectionThread = new Thread(new ThreadStart(CollectData));
			_dataCollectionThread.SetApartmentState(ApartmentState.STA);
			_dataCollectionThread.Start();
		}

		private void Simulation()
		{
			_exitWait.Reset();
			long limitOfUnansweredOrders = 0;
			Dispatcher.Invoke(new Action(() => { limitOfUnansweredOrders = maxOrdersPerSecondCheckBox.IsChecked == true ? MaxOrdersPerSecond : 0; }));
			_client = new MultiClientManager(_exitWait, processesStartedEvent);
			_client.OnException += Client_OnException;
			_client.Run(App.connectionConfig.IP.MapToIPv4().ToString(), App.connectionConfig.Port, MaxConnections,
				_priceStrategy, _volumeStrategy,
				limitOfUnansweredOrders / MaxConnections);
		}

		private void Client_OnException(long userID, string message)
		{
			Dispatcher.BeginInvoke(
				new Action(() =>
			   {
				   if (log.Text.Length > 10_000_000)
					   log.Text = string.Empty;
				   log.Text += string.Format("{0:T}|{1}: {2}\n", DateTime.Now, userID, message);
			   }));
		}

		private void NotifyPropertyChanged(String propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private void CreateSim()
		{
			_simThread = new Thread(new ThreadStart(Simulation))
			{
				Priority = ThreadPriority.Highest,
				IsBackground = true
			};
			_simThread.SetApartmentState(ApartmentState.MTA);
			_simThread.Start();
		}

		private void Window_Closed(object sender, EventArgs e)
		{
			_stop = true;
			_exitWait.Wait(5000);
			if (!_exitWait.IsSet && _client != null)
				_client.ForceStop();

			OptionsConfig.Save(_priceStrategy, _volumeStrategy);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private double CalculateAveragePerformance(int stepsPassed, ref double[] data)
		{
			double sum = 0;
			int countOfValues = Math.Min(_stepsPerMinute, stepsPassed);
			for (int i = data.Length - 1; i >= data.Length - countOfValues; i--)
				sum += data[i];
			return sum / countOfValues;
		}

		private void PricesVolumesOtions_Click(object sender, RoutedEventArgs e)
		{
			if (_priceAndVolumeWindow == null)
			{
				_priceAndVolumeWindow = new PriceAndVolumeWindow(_priceStrategy, _volumeStrategy)
				{
					Owner = this,
					ShowActivated = true
				};
				_priceAndVolumeWindow.priceAndValueChanged += _priceAndVolumeWindow_priceAndValueChanged;
				_priceAndVolumeWindow.Closed += _priceAndVolumeWindow_Closed;
				_priceAndVolumeWindow.Show();
			}
			else
				_priceAndVolumeWindow.Activate();
		}

		private void _priceAndVolumeWindow_Closed(object sender, EventArgs e)
		{
			_priceAndVolumeWindow.priceAndValueChanged -= _priceAndVolumeWindow_priceAndValueChanged;
			_priceAndVolumeWindow.Closed -= _priceAndVolumeWindow_Closed;
			_priceAndVolumeWindow = null;
		}

		private void _priceAndVolumeWindow_priceAndValueChanged(PriceVolumeStrategyAbstract priceStrategy, PriceVolumeStrategyAbstract volumeStrategy)
		{
			_priceStrategy = priceStrategy;
			_volumeStrategy = volumeStrategy;
			if (_client != null)
			{
				_client.SetPriceStrategy(_priceStrategy);
				_client.SetVolumeStrategy(_volumeStrategy);
			}
		}

		private long _maxConnections = 8;
		private long _maxOrdersPerSecond = 10000;

		private Thread _simThread;
		private Thread _dataCollectionThread;

		private readonly ManualResetEventSlim _exitWait = new ManualResetEventSlim(true);
		private readonly ManualResetEventSlim processesStartedEvent = new ManualResetEventSlim(false);

		private MultiClientManager _client;
		private readonly DispatcherTimer _tpsTimer = new DispatcherTimer();
		private int _stepsPerMinute;
		private int _frequency;
		private bool _stop;

		private int _intervalInMinutes = 2;

		private double[] _animatedX;
		private double[] _processedOrdersY;
		private double[] _averageY;

		private double[] _cancelledOrdersY;
		private double[] _createdOrdersY;
		private double[] _executedOrdersY;
		private double[] _completedOrdersY;
		private double[] _rejectedOrdersY;
		private double[] _rejectedRequestsY;

		private double[] _ordersSendY;
		private double[] _averageSendY;
		private double[] _latency90Y;
		private double[] _latency95Y;
		private double[] _latency99Y;

		private LineGraph _currentTpsGraph;
		private LineGraph _averagePerformanceGraph;
		private LineGraph _cancelledOrdersGraph;
		private LineGraph _createdOrdersGraph;
		private LineGraph _executedOrdersGraph;
		private LineGraph _completedOrdersGraph;
		private LineGraph _rejectedOrdersGraph;
		private LineGraph _rejectedRequestsGraph;
		private LineGraph _orderSendGraph;
		private LineGraph _averageSendGraph;
		private LineGraph _latency90Graph;
		private LineGraph _latency95Graph;
		private LineGraph _latency99Graph;

		PriceVolumeStrategyAbstract _priceStrategy;
		PriceVolumeStrategyAbstract _volumeStrategy;
		PriceAndVolumeWindow _priceAndVolumeWindow;
	}
}