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

namespace GridEx.PerformanceMonitor
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public int connectionCount { get => client == null ? 0 : client.ConnectionsCount; }

        long _maxConnections = 8;
        public long maxConnections
        {
            get => _maxConnections;
            set { _maxConnections = value < 1 ? 1 : value; ; NotifyPropertyChanged("maxConnections"); }
        }
        long _maxOrdersPerSecond = 10000;
        public long maxOrdersPerSecond
        {
            get => _maxOrdersPerSecond;
            set { _maxOrdersPerSecond = value < maxConnections ? (maxConnections * 10) : value; NotifyPropertyChanged("maxOrdersPerSecond"); }
        }

        ManualResetEventSlim _exitWait = new ManualResetEventSlim(true);
        ManualResetEventSlim processesStartedEvent = new ManualResetEventSlim(false);

        MultiClientManager client;
        readonly DispatcherTimer tpsTimer = new DispatcherTimer();
        int stepsPerMinute;
        int _frequency;
        public int frequency
        {
            get => _frequency;
            set { if (value > 0) { _frequency = value; stepsPerMinute = 60 / _frequency; } }
        }
        int intervalInMinutes = 2;

        double[] animatedX;
        double[] processedOrdersY;
        double[] averageY;

        double[] cancelledOrdersY;
        double[] createdOrdersY;
        double[] executedOrdersY;
        double[] completedOrdersY;
        double[] rejectedOrdersY;
        double[] rejectedRequestsY;

        double[] ordersSendY;
        double[] averageSendY;
        double[] latency90Y;
        double[] latency95Y;
        double[] latency9999Y;

        LineGraph currentTpsGraph;
        LineGraph averagePerformanceGraph;
        LineGraph cancelledOrdersGraph;
        LineGraph createdOrdersGraph;
        LineGraph executedOrdersGraph;
        LineGraph completedOrdersGraph;
        LineGraph rejectedOrdersGraph;
        LineGraph rejectedRequestsGraph;
        LineGraph orderSendGraph;
        LineGraph averageSendGraph;
        LineGraph latency90Graph;
        LineGraph latency95Graph;
        LineGraph latency9999Graph;
        public MainWindow()
        {
            frequency = 1;

            InitializeComponent();

            ResetDatas();

            NotifyPropertyChanged("connectionCount");
        }

        LineGraph CreateGraph(ref Plot plot, Brush brush, string tooltip)
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

        void UpdateAllPlots(long countOfIntervals)
        {
            currentTpsGraph.Plot(animatedX, processedOrdersY);
            averagePerformanceGraph.Plot(animatedX, averageY);
            cancelledOrdersGraph.Plot(animatedX, cancelledOrdersY);
            createdOrdersGraph.Plot(animatedX, createdOrdersY);
            executedOrdersGraph.Plot(animatedX, executedOrdersY);
            completedOrdersGraph.Plot(animatedX, completedOrdersY);
            rejectedOrdersGraph.Plot(animatedX, rejectedOrdersY);
            rejectedRequestsGraph.Plot(animatedX, rejectedRequestsY);
            averageSendGraph.Plot(animatedX, averageSendY);
            orderSendGraph.Plot(animatedX, ordersSendY);
            latency90Graph.Plot(animatedX, latency90Y);
            latency95Graph.Plot(animatedX, latency95Y);
            latency9999Graph.Plot(animatedX, latency9999Y);
            latencyChart.BottomTitle = "Intervals: " + countOfIntervals.ToString();
        }

        void ResetDatas()
        {
            animatedX = new double[intervalInMinutes * stepsPerMinute];
            processedOrdersY = new double[animatedX.Length];
            averageY = new double[animatedX.Length];
            cancelledOrdersY = new double[animatedX.Length];
            createdOrdersY = new double[animatedX.Length];
            executedOrdersY = new double[animatedX.Length];
            completedOrdersY = new double[animatedX.Length];
            rejectedOrdersY = new double[animatedX.Length];
            rejectedRequestsY = new double[animatedX.Length];
            ordersSendY = new double[animatedX.Length];
            averageSendY = new double[animatedX.Length];
            latency90Y = new double[animatedX.Length];
            latency95Y = new double[animatedX.Length];
            latency9999Y = new double[animatedX.Length];

            for (int i = 0; i < animatedX.Length; i++)
            {
                animatedX[i] =  i / (float)frequency - (intervalInMinutes * 60);
                latency90Y[i] = latency95Y[i] = latency9999Y[i] =
                    averageSendY[i] = ordersSendY[i] =
                    cancelledOrdersY[i] = createdOrdersY[i] = executedOrdersY[i] = completedOrdersY[i] = rejectedOrdersY[i] = rejectedRequestsY[i] =
                    averageY[i] = processedOrdersY[i] = 0;
            }

            if (currentTpsGraph != null)
            {
                UpdateAllPlots(0);
            }
            else
            {
                latency90Graph = CreateGraph(ref latencyPlot, Brushes.Red, "90%");
                latency95Graph = CreateGraph(ref latencyPlot, Brushes.Green, "95%");
                latency9999Graph = CreateGraph(ref latencyPlot, Brushes.LightBlue, "99%");

                averageSendGraph = CreateGraph(ref sendOrdersPlot, Brushes.MediumBlue, "Average perf/min");
                orderSendGraph = CreateGraph(ref sendOrdersPlot, Brushes.DeepPink, "Orders send");

                averagePerformanceGraph = CreateGraph(ref TPSPlot, Brushes.Blue, "Average perf/min");
                currentTpsGraph = CreateGraph(ref TPSPlot, Brushes.DarkGreen, "Current TPS");

                cancelledOrdersGraph = CreateGraph(ref cancelledOrdersPlot, Brushes.DarkOliveGreen, "Canceled orders");

                createdOrdersGraph = CreateGraph(ref createdOrdersPlot, Brushes.SeaGreen, "Created orders");

                executedOrdersGraph = CreateGraph(ref executedOrdersPlot, Brushes.DarkOrange, "Executed orders");

                completedOrdersGraph = CreateGraph(ref completedOrdersPlot, Brushes.Brown, "Completed orders");

                rejectedOrdersGraph = CreateGraph(ref rejectedOrdersPlot, Brushes.Orchid, "Rejected orders");

                rejectedRequestsGraph = CreateGraph(ref rejectedRequestsPlot, Brushes.Red, "Rejected requests");
            }
        }

        private void startStopButton_Checked(object sender, RoutedEventArgs e)
        {
            optionsContainer.IsEnabled = false;
            stop = false;
            startStopButton.IsEnabled = false;
            startStopButton.Content = "Starting...";
            Dispatcher.Invoke(new Action(() =>
            {
                CreateSim();
                CreateDataCollectionThread();
            }), DispatcherPriority.Background);

        }
        bool stop;
        private void startStopButton_Unchecked(object sender, RoutedEventArgs e)
        {
            startStopButton.IsChecked = false;
            startStopButton.Content = "Stopping...";
            tbTotalOrders.Text += " - Last value (Stopped)";

            Dispatcher.Invoke(new Action(() =>
            {
                processesStartedEvent.Reset();
                stop = true;
                _exitWait.Wait(5000);
                if (!_exitWait.IsSet)
                {
                    client.ForceStop();
                    _exitWait.Set();
                }
                ResetDatas();
                NotifyPropertyChanged("connectionCount");
                startStopButton.Content = "Start";
                startStopButton.IsEnabled = true;
                optionsContainer.IsEnabled = true;
            }), DispatcherPriority.Background);
        }

        void CollectData()
        {
            double totalOrderSend = 0;
            double totalOrdersProcessed = 1;

            processesStartedEvent.Wait();

            Dispatcher.Invoke(new Action(() =>
            {
                startStopButton.Content = "Stop";
                startStopButton.IsEnabled = true;
            }));

            ManualResetEventSlim threadEvent = new ManualResetEventSlim(false);
            int stepsPassed = 0;
            bool needCalculatePassedSteps = true;

            int pauseDelay = 1000 / frequency;

            while (!stop)
            {
                threadEvent.Wait(pauseDelay);
                if (client == null)
                    continue;
                long processedOrders = client.ResetProcessedOrders();
                totalOrdersProcessed += processedOrders;

                for (int i = 0; i < processedOrdersY.Length - 1; i++)
                {
                    processedOrdersY[i] = processedOrdersY[i + 1];
                    averageY[i] = averageY[i + 1];
                    cancelledOrdersY[i] = cancelledOrdersY[i + 1];
                    createdOrdersY[i] = createdOrdersY[i + 1];
                    executedOrdersY[i] = executedOrdersY[i + 1];
                    completedOrdersY[i] = completedOrdersY[i + 1];
                    rejectedOrdersY[i] = rejectedOrdersY[i + 1];
                    rejectedRequestsY[i] = rejectedRequestsY[i + 1];
                    ordersSendY[i] = ordersSendY[i + 1];
                    averageSendY[i] = averageSendY[i + 1];
                    latency90Y[i] = latency90Y[i + 1];
                    latency95Y[i] = latency95Y[i + 1];
                    latency9999Y[i] = latency9999Y[i + 1];
                }

                processedOrdersY[processedOrdersY.Length - 1] = processedOrders;
                cancelledOrdersY[cancelledOrdersY.Length - 1] = client.ResetCancelledOrders();
                createdOrdersY[createdOrdersY.Length - 1] = client.ResetCreatedOrders();
                executedOrdersY[executedOrdersY.Length - 1] = client.ResetExecutedOrders();
                completedOrdersY[completedOrdersY.Length - 1] = client.ResetCompletedOrders();
                rejectedOrdersY[rejectedOrdersY.Length - 1] = client.ResetRejectedOrders();
                rejectedRequestsY[rejectedRequestsY.Length - 1] = client.ResetRejectedRequests();

                totalOrderSend += (ordersSendY[ordersSendY.Length - 1] = client.ResetSendOrders());

                if (needCalculatePassedSteps)
                {
                    stepsPassed++;
                    needCalculatePassedSteps = stepsPassed < stepsPerMinute;
                }
                averageY[averageY.Length - 1] = CalculateAveragePerformance(stepsPassed, ref processedOrdersY);
                averageSendY[averageSendY.Length - 1] = CalculateAveragePerformance(stepsPassed, ref ordersSendY);

                client.GetLatencyStatistic(out long countOfIntervals, out double latency90, out double latency95, out double latency9999);
                latency90Y[latency90Y.Length - 1] = latency90;
                latency95Y[latency95Y.Length - 1] = latency95;
                latency9999Y[latency9999Y.Length - 1] = latency9999;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    NotifyPropertyChanged("connectionCount");
                    UpdateAllPlots(countOfIntervals);

                    tbTotalOrders.Text = string.Format("Orders Proc|Ave|Send:{0,10} | {1,10} | {2,-10} ({3})", 
                        processedOrders, (long)averageY[averageY.Length - 1], (long)ordersSendY[ordersSendY.Length - 1], DateTime.Now);
                    tbTotalOrdersSend.Text = string.Format("Total  Proc|Send| % :{0,10} | {1,10} | {2:00.00}%",
                        totalOrdersProcessed, totalOrderSend, totalOrdersProcessed / totalOrderSend * 100);
                }), DispatcherPriority.ApplicationIdle);

                threadEvent.Reset();
            }
            client.onException -= Client_onException;
            client.Stop();

            threadEvent.Wait(5000);
            GC.Collect(2, GCCollectionMode.Forced);
        }

        void CreateDataCollectionThread()
        {
            dataCollectionThread = new Thread(new ThreadStart(CollectData));
            dataCollectionThread.SetApartmentState(ApartmentState.STA);
            dataCollectionThread.Start();
        }

        private void Simulation()
        {
            _exitWait.Reset();
            long limitOfUnansweredOrders = 0;
            Dispatcher.Invoke(new Action(() => { limitOfUnansweredOrders = maxOrdersPerSecondCheckBox.IsChecked == true ? maxOrdersPerSecond : 0; }));
            client = new MultiClientManager(_exitWait, processesStartedEvent);
            client.onException += Client_onException;
            client.Run(App.connectionConfig.IP, App.connectionConfig.Port, maxConnections, limitOfUnansweredOrders / maxConnections);
        }

        private void Client_onException(long userID, string message)
        {
            Dispatcher.Invoke(new Action(() =>
           {
               log.Text += string.Format("{0}\nUser ID: {1}\n{2}\n----------------------------------------------------------\n", DateTime.Now, userID, message);
           }));
        }

        Thread simThread;
        Thread dataCollectionThread;

        public event PropertyChangedEventHandler PropertyChanged;
        private void NotifyPropertyChanged(String propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        void CreateSim()
        {
            simThread = new Thread(new ThreadStart(Simulation))
            {
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };
            simThread.SetApartmentState(ApartmentState.MTA);
            simThread.Start();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            stop = true;
            _exitWait.Wait(5000);
            if (!_exitWait.IsSet && client != null)
                client.ForceStop();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        double CalculateAveragePerformance(int stepsPassed, ref double[] data)
        {
            double sum = 0;
            int countOfValues = Math.Min(stepsPerMinute, stepsPassed);
            for (int i = data.Length - 1; i >= data.Length - countOfValues; i--)
                sum += data[i];
            return sum / countOfValues;
        }
    }
}
