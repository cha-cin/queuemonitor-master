using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor.Model
{
    public class ProcmanInfo
    {
        public string ContainerName { get; set; }

        public string HostName { get; set; }

        public string ProcessName { get; set; }
        
        public string Pid { get; set; }
    }
}
