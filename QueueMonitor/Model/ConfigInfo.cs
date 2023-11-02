using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor.Model
{
    public class ConfigInfo
    {
        public string Service { get; set; }

        public string Subject { get; set; }

        public string Action { get; set; }

        public int MaxQueue { get; set; }

        public int DSSMaxQueue { get; set; }

        public int MinRequiredSvc { get; set; }

        public int Percentage { get; set; }
    }
}