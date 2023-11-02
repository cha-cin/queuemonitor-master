using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor.Model
{
    public class MIPCInfo
    {
        public string Hostname { get; set; }

        public string PID { get; set; }

        public int ReceiveQueueSize { get; set; }
    }
}
