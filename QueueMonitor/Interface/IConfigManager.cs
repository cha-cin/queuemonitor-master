using QueueMonitor.Model;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor.Interface
{
    public interface IConfigManager
    {
        List<string> GetMonitorServiceList();

        ConfigInfo GetConfigInfoBySvc(string serviceName);
    }
}
