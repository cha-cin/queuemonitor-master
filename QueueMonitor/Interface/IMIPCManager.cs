using Micron.Messaging.Mipc;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor.Interface
{
    interface IMIPCManager
    {
        Hashtable GetRegisteredNamesBySubject(string mipcAddress);

        MTMessage GetMIPCInfoByService(string subject, string hostname, string pid);

        MTMessage UnsubscribeViaSupportSVC(string subject, string hostname, string pid);

        MTMessage SendCommand(string msg, string sourcePath, string desPath, string replyPath);

        MTMessage ReceiveCommand(MTMessage mtMessage);
    }
}