using System;
using System.Globalization;
using System.Windows.Data;

namespace PerformanceMonitor.Converters
{
	class CutDoubleConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (!(value is double))
			{
				return Binding.DoNothing;
			}

			var doubleVal = (double)value;

			return doubleVal.ToString("F8", _culture).TrimEnd('0');
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			var strVal = value as string;

			if (strVal != null && double.TryParse(strVal, NumberStyles.Any, _culture, out var val))
			{
				return double.Parse(val.ToString("F8").TrimEnd('0'));
			}
			return Binding.DoNothing;
		}

		private CultureInfo _culture = CultureInfo.GetCultureInfo("en-US");
	}
}
