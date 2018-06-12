using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GridEx.PerformanceMonitor.Client
{
    internal class Latency
    {
        public long Id;
        public long SendTime;
        public long OrderCreatedTime;

        public Latency(long id, long sendTime)
        {
            Id = id;
            SendTime = sendTime;
            OrderCreatedTime = 0;
        }
    }
}
