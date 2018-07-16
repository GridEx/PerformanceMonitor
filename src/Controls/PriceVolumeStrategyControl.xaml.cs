using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using PerformanceMonitor.Utils;

namespace PerformanceMonitor.Controls
{
	/// <summary>
	/// Interaction logic for PriceVolumeStrategyControl.xaml
	/// </summary>
	public partial class PriceVolumeStrategyControl : UserControl, INotifyPropertyChanged
	{
		public PriceVolumeStrategyControl()
		{
			//this.Loaded += PriceVolumeStrategyControl_Loaded;
			InitializeComponent();
		}

		//private void PriceVolumeStrategyControl_Loaded(object sender, RoutedEventArgs e)
		//{
		//	ItsPriceControl = ItsPriceControl;
		//	this.Loaded -= PriceVolumeStrategyControl_Loaded;
		//}

		public Boolean ItsPriceControl
		{
			get { return (Boolean)this.GetValue(ItsPriceControlProperty); }
			set
			{
				this.SetValue(ItsPriceControlProperty, value);
				if (value)
				{
					container.Header = "Price";
					Maximum = _top = PriceVolumeStrategyAbstract.TopPrice;
					Minimum = _bottom = PriceVolumeStrategyAbstract.BottomPrice;
				}
				else
				{
					container.Header = "Volume";
					Maximum = _top = PriceVolumeStrategyAbstract.TopVolume;
					Minimum = _bottom = PriceVolumeStrategyAbstract.BottomVolume;
				}

				Period = _topPeriod = PriceVolumeStrategyAbstract.TopPeriod;
				_bottomPeriod = PriceVolumeStrategyAbstract.BottomPeriod;

				PhaseShift = _maxPhaseShift = PriceVolumeStrategyAbstract.MaxPhaseShift;
				_minPhaseShift = PriceVolumeStrategyAbstract.MinPhaseShift;

			}
		}
		public static readonly DependencyProperty ItsPriceControlProperty = DependencyProperty.Register(
		  "ItsPriceControl", typeof(Boolean), typeof(PriceVolumeStrategyControl), new PropertyMetadata(false));

		public void SetState(ref PriceVolumeStrategyAbstract strategy, bool itsPriceControl)
		{
			this.ItsPriceControl = itsPriceControl;

			var priceVolumeStrategySinus = strategy as PriceVolumeStrategySinus;
			if (priceVolumeStrategySinus != null)
			{
				sinRadioBtn.IsChecked = true;
				Period = priceVolumeStrategySinus.Period;
				PhaseShift = priceVolumeStrategySinus.PhaseShift;
			}
			else
			{
				randomBtn.IsChecked = true;
			}

			Maximum = strategy.Maximum;
			Minimum = strategy.Minimum;
		}

		public PriceVolumeStrategyAbstract GetStrategy()
		{
			if (sinRadioBtn.IsChecked == true)
			{
				return new PriceVolumeStrategySinus(Period, Minimum, Maximum, PhaseShift);
			}
			else
			{
				return new PriceVolumeStrategyRandom(Minimum, Maximum);
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;
		public double Maximum
		{
			get => _maximum;
			set
			{
				_maximum = (value > _top || value < _bottom)
					? _top
					: (value < Minimum ? Minimum : value);

				_maximum = double.Parse(_maximum.ToString("F8").TrimEnd('0'));
				NotifyPropertyChanged("Maximum");
			}
		}

		public double Minimum
		{
			get => _minimum;
			set
			{
				_minimum = (value > _top || value < _bottom)
					? _bottom
					: (value > Maximum ? Maximum : value);

				_minimum = double.Parse(_minimum.ToString("F8").TrimEnd('0'));
				NotifyPropertyChanged("Minimum");
			}
		}

		public uint Period
		{
			get => _period;
			set
			{
				_period = value > _topPeriod
					? _topPeriod
						: (value < _bottomPeriod
						? _bottomPeriod
						: value);
				NotifyPropertyChanged("Period");
			}
		}

		public double PhaseShift
		{
			get => _phaseShift;
			set
			{
				_phaseShift = value > _maxPhaseShift
					? _maxPhaseShift
						: (value < _minPhaseShift
						? _minPhaseShift
						: value);
				NotifyPropertyChanged("PhaseShift");
			}
		}

		private void NotifyPropertyChanged(String propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private double _maximum;
		private double _minimum;
		private uint _period;
		private double _phaseShift;

		private double _top;
		private double _bottom;
		private uint _topPeriod;
		private uint _bottomPeriod;
		private double _minPhaseShift;
		private double _maxPhaseShift;
	}
}
