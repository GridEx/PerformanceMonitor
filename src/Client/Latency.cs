using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GridEx.PerformanceMonitor.Client
{
    class Latency
    {
        public readonly long id;
        public readonly long sendTime;
        public long orderCreatedTime;

        public Latency(long id, long sendTime)
        {
            this.id = id;
            this.sendTime = sendTime;
            orderCreatedTime = 0;
        }
    }
}
