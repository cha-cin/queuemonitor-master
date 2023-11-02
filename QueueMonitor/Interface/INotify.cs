using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor.Interface
{
    interface INotify
    {
        void SendEmailNotification(string msg, string notifyFrom, string notifyTo, string subject);
    }
}
