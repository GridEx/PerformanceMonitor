using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using PerformanceMonitor.Utils;

namespace PerformanceMonitor.Config
{
	class OptionsConfig
	{
		public static void Load(out PriceVolumeStrategyAbstract priceStrategy, out PriceVolumeStrategyAbstract volumeStrategy)
		{
			priceStrategy = null;
			volumeStrategy = null;

			if (File.Exists(_filename))
			{
				try
				{
					XDocument options = XDocument.Load(_filename);
					var root = options.Element("Options");
					priceStrategy = LoadStrategy(root, "PriceStrategy");
					volumeStrategy = LoadStrategy(root, "VolumeStrategy");
				}
				catch(Exception ex)
				{

				}
			}
		}

		public static void Save(PriceVolumeStrategyAbstract priceStrategy, PriceVolumeStrategyAbstract volumeStrategy)
		{
			try
			{
				XElement root = new XElement("Options");
				if (priceStrategy != null)
					root.Add(new XElement("PriceStrategy", priceStrategy.ConvertToXElement()));
				if (volumeStrategy != null)
					root.Add(new XElement("VolumeStrategy", volumeStrategy.ConvertToXElement()));
				root.Save(_filename);
			}
			catch (Exception ex)
			{

			}
		}

		static readonly string _filename = "Options.xml";

		private static PriceVolumeStrategyAbstract CheckStrategy(PriceVolumeStrategyAbstract strategy, bool itsForPrice)
		{
			if (strategy == null)
			{
				return null;
			}

			double min = itsForPrice ? PriceVolumeStrategyAbstract.BottomPrice : PriceVolumeStrategyAbstract.BottomVolume;
			double max = itsForPrice ? PriceVolumeStrategyAbstract.TopPrice : PriceVolumeStrategyAbstract.TopVolume;

			min = Math.Min(strategy.Maximum, Math.Max(strategy.Minimum, min));
			max = Math.Max(strategy.Minimum, Math.Min(strategy.Maximum, max));

			var strategySinus = strategy as PriceVolumeStrategySinus;
			if (strategySinus != null)
			{
				uint period = Math.Min(PriceVolumeStrategyAbstract.BottomPeriod,
					Math.Max(strategySinus.Period, PriceVolumeStrategyAbstract.TopPeriod));

				double phaseShift = Math.Min(PriceVolumeStrategyAbstract.MinPhaseShift, 
					Math.Max(strategySinus.PhaseShift, PriceVolumeStrategyAbstract.MaxPhaseShift));

				return new PriceVolumeStrategySinus(period, min, max, phaseShift);
			}
			return new PriceVolumeStrategyRandom(new Random(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0)), min, max);
		}

		private static PriceVolumeStrategyAbstract LoadStrategy(XElement root, string strategyName)
		{
			XElement xElement = root?.Element(strategyName);
			if (xElement != null && xElement.HasElements)
			{
				XElement strategyXElement = xElement.Element("Strategy");
				if (strategyXElement?.Attribute("Period") != null)
				{
					return new PriceVolumeStrategySinus(strategyXElement);
				}
				else
				{
					return new PriceVolumeStrategyRandom(strategyXElement);
				}
			}
			return null;
		}
	}
}
