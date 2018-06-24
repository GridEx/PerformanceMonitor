using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PerformanceMonitor.Utils
{
	public class PriceVolumeStrategyRandom : PriceVolumeStrategyAbstract
	{
		public PriceVolumeStrategyRandom(XElement sourse) : base(sourse)
		{
			_randomWithSeed = new Random(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0));
			_delta = Math.Abs(Maximum - Minimum);
		}

		public PriceVolumeStrategyRandom(Random randomWithSeed, double minimum, double maximum) 
			: base(minimum, maximum)
		{
			_randomWithSeed = randomWithSeed ?? new Random((int)DateTime.Now.Ticks);
			_delta = Math.Abs(Maximum - Minimum);
		}

		public override double ProduceValue()
		{
			return _randomWithSeed.NextDouble() * _delta + Minimum;
		}

		public override object Clone()
		{
			return new PriceVolumeStrategyRandom(_randomWithSeed, Minimum, Maximum);
		}

		private Random _randomWithSeed;
		private double _delta;
	}

	public class PriceVolumeStrategySinus : PriceVolumeStrategyAbstract, IDisposable
	{
		public PriceVolumeStrategySinus(XElement sourse) : base(sourse)
		{
			
		}

		public PriceVolumeStrategySinus(uint period, double minimum, double maximum, double phaseShift)
			: base(minimum, maximum)
		{
			Period = period < BottomPeriod ? BottomPeriod
				: (period > TopPeriod ? TopPeriod : period);

			_delta = Math.Abs(Maximum - Minimum) / 2;

			tick = _2PI / Period;

			PhaseShift = (phaseShift < MinPhaseShift ? MinPhaseShift
				: (phaseShift > MaxPhaseShift ? MaxPhaseShift : phaseShift)) % 2;
		}

		public override double ProduceValue()
		{
			if (_timer == null)
			{
				_timer = new Stopwatch();
				_timer.Start();
			}
			else if (!_timer.IsRunning)
			{
				_timer.Start();
			}

			return (1 + Math.Sin(_timer.ElapsedMilliseconds % Period * tick + _phaseShift2Pi)) * _delta + Minimum;
		}

		public void Dispose()
		{
			if (_timer != null)
			{
				if (_timer.IsRunning)
				{
					_timer.Stop();
				}
				_timer = null;
			}
		}

		public override object Clone()
		{
			return new PriceVolumeStrategySinus(Period, Minimum, Maximum, PhaseShift);
		}

		public override XElement ConvertToXElement()
		{
			XElement newState = base.ConvertToXElement();
			newState.Add(new XAttribute("Period", Period),
				new XAttribute("PhaseShift", PhaseShift));
			return newState;
		}

		public uint Period { get; protected set; }
		public double PhaseShift
		{
			get => _phaseShift;
			protected set
			{
				_phaseShift = value;
				_phaseShift2Pi = _phaseShift % 2 * Math.PI;
			}
		}

		protected override void Load(XElement sourse)
		{
			base.Load(sourse);

			Period = LoadUintInvariantCulture(sourse, "Period", BottomPeriod, TopPeriod);
			PhaseShift = LoadDoubleInvariantCulture(sourse, "PhaseShift", MinPhaseShift, MaxPhaseShift);
		}

		private const double _2PI = Math.PI * 2;
		private double _delta;
		private Stopwatch _timer;
		private double tick;
		private double _phaseShift;
		private double _phaseShift2Pi;
	}

	public abstract class PriceVolumeStrategyAbstract : ICloneable, IXElement
	{
		public PriceVolumeStrategyAbstract(XElement sourse)
		{
			Load(sourse);
		}

		protected PriceVolumeStrategyAbstract(double minimum, double maximum)
		{
			this.Minimum = Math.Min(minimum, maximum);
			this.Maximum = Math.Max(minimum, maximum);
		}

		public abstract double ProduceValue();
		public abstract object Clone();
		public virtual XElement ConvertToXElement()
		{
			return new XElement("Strategy",
				new XAttribute("Maximum", Maximum),
				new XAttribute("Minimum", Minimum));
		}

		public static readonly double TopPrice = 0.0002;
		public static readonly double BottomPrice = 0.00000001;
		public static readonly double TopVolume = 0.1;
		public static readonly double BottomVolume = 0.0000001;
		public static readonly uint TopPeriod = 120000;
		public static readonly uint BottomPeriod = 1000;
		public static readonly double MinPhaseShift = 0;
		public static readonly double MaxPhaseShift = 2;

		public double Minimum { get; protected set; }
		public double Maximum { get; protected set; }

		protected virtual void Load(XElement sourse)
		{
			Minimum = LoadDoubleInvariantCulture(sourse, "Minimum", BottomPrice, BottomVolume);
			Maximum = LoadDoubleInvariantCulture(sourse, "Maximum", TopPrice, TopVolume);
		}

		protected double LoadDoubleInvariantCulture(XElement owner, string attributeName, double minimumValue, double maximumValue)
		{
			if (!double.TryParse(owner?.Attribute(attributeName)?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
			{
				value = Math.Max(minimumValue, maximumValue);
			}
			return value;
		 }

		protected uint LoadUintInvariantCulture(XElement owner, string attributeName, uint minimumValue, uint maximumValue)
		{
			if (!uint.TryParse(owner?.Attribute(attributeName)?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out uint value))
			{
				value = Math.Max(minimumValue, maximumValue);
			}
			return value;
		}
	}
}
