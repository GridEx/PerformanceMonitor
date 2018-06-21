using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace GridEx.PerformanceMonitor.Config
{
    public class ConnectionConfig
    {
        public IPAddress IP { get => _iP; set => _iP = value; }
        public int Port { get => _port; set => _port = value; }

        public ConnectionConfig(string configFile)
        {
            configFilename = configFile ?? "Config.xml";
            if (!File.Exists(configFile))
            {
                IP = new IPAddress(new byte[] { 127, 0, 0, 1 });
                Port = 1234;
            }
            else
            {
                try
                {
                    XDocument config = XDocument.Load(configFile);
                    XElement root = config.Element("Config");

                    if (!IPAddress.TryParse(root?.Attribute("IP")?.Value, out _iP))
                    {
                        IP = new IPAddress(new byte[] { 127, 0, 0, 1 });
                    }

                    if (!int.TryParse(root?.Attribute("Port")?.Value, out _port))
                    {
                        Port = 1;
                    }
                }
                catch { }
            }
        }

        public void Save()
        {
            new XElement("Config",
                new XAttribute("IP", IP.MapToIPv4().ToString()),
                new XAttribute("Port", Port.ToString()))
            .Save(configFilename ?? "Config.xml");
        }

        string configFilename;
        private IPAddress _iP;
        private int _port;
    }
}