using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace GridEx.PerformanceMonitor.Config
{
    public class ConnectionConfig
    {
        public readonly string IP = null;
        public readonly int Port = 1;

        public ConnectionConfig(string configFile)
        {
            if (File.Exists(configFile))
                try
                {
                    XDocument config = XDocument.Load(configFile);
                    XElement root = config.Element("Config");

                    IP = root?.Attribute("IP")?.Value;
                    int.TryParse(root?.Attribute("Port")?.Value, out Port);
                }
                catch { }
        }
    }
}
