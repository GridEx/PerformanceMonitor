using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

using PerformanceMonitor.Utils;

namespace PerformanceMonitor.Controls
{
	/// <summary>
	/// Interaction logic for Window1.xaml
	/// </summary>
	public partial class PriceAndVolumeWindow : Window
	{
		public PriceAndVolumeWindow(PriceVolumeStrategyAbstract priceStrategy, 
			PriceVolumeStrategyAbstract volumeStrategy)
		{
			InitializeComponent();
			PriceControl.SetState(ref priceStrategy, true);
			VolumeControl.SetState(ref volumeStrategy, false);
		}

		private void ButtonApply_Click(object sender, RoutedEventArgs e)
		{
			priceAndValueChanged?.Invoke(PriceControl.GetStrategy(), VolumeControl.GetStrategy());
			Close();
		}

		private void ButtonCancel_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		public event PriceAndValueChanged priceAndValueChanged;
	}

	public delegate void PriceAndValueChanged(PriceVolumeStrategyAbstract priceStrategy, PriceVolumeStrategyAbstract volumeStrategy);
}
