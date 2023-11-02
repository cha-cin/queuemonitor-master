using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor.Enum
{
    enum ActionType
    {
        Restart,
        Stop,
        Failover,
        Unsubscribe,
        RestartUnsubscribe,
        StopUnsubscribe
    }
}
