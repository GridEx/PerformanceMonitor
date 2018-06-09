using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GridEx.PerformanceMonitor.Config;

namespace GridEx.PerformanceMonitor
{
    public partial class App : Application
    {
        public static ConnectionConfig connectionConfig;

        static App()
        {
            connectionConfig = new ConnectionConfig("config.xml");
        }
    }
}
