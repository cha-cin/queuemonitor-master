using System;
using System.Collections;
using Micron.Messaging;
using Micron.Messaging.Mipc;
using QueueMonitor.Interface;
using System.Threading;
using System.Diagnostics;

namespace QueueMonitor.MIPCHandler
{
	public class MIPCManager : IMIPCManager
	{
		public MIPCLite _mipcLite;
		private string _stieName;
		public MIPCManager()
		{
			_stieName = Environment.GetEnvironmentVariable("SITE_NAME");
			_mipcLite = new MIPCLite(_stieName);
		}

		public Hashtable GetRegisteredNamesBySubject(string mipcAddress)
		{
			MIPC _loMipc = new MIPC();
			Hashtable ht = new Hashtable();
			try { ht = _loMipc.GetRegisteredNames(mipcAddress); }
			catch (Exception ex)
			{
				ht = new Hashtable();
				ht.Add("Error", ex.Message);
			}
			return ht;
		}

		public MTMessage SendCommand(string msg, string sourcePath, string desPath, string replyPath)
		{
			var mtSendMsg = new MTMessage(sourcePath, desPath, replyPath, msg);
			_mipcLite.SendAsync(mtSendMsg);
			return mtSendMsg;
		}

		public MTMessage ReceiveCommand(MTMessage mtMessage)
		{
			var mtReceiveMsg = new MTMessage();
			mtReceiveMsg = _mipcLite.SendReceive(mtMessage, 30); //Timeout : 30sec
			return mtReceiveMsg;
		}

		public MTMessage GetMIPCInfoByService(string subject, string hostname, string pid)
		{
            try
            {
                var mtSendMsg = new MTMessage("", subject, "", "<DS_START_PAGE/>");
                //var mtSendMsg = new MTMessage("", subject, "", "<mipcSvcInfo/>");
                _mipcLite.SendAsync(mtSendMsg);
                var mtReceiveMsg = _mipcLite.SendReceive(mtSendMsg, 30);
                return mtReceiveMsg;
            }
            catch (Exception e)
            {
                return null;
            }
		}

		public MTMessage UnsubscribeViaSupportSVC(string subject, string host, string pid)
		{
			var msgBody = string.Format(@"<mipcSvcUnRegister><Address>{0}</Address></mipcSvcUnRegister>", subject);
			var address = string.Format(@"/{0}/MTI/MFG/OPENAUTO/SUPPORTSVC/{1}/{2}", _stieName, host, pid);

			var mtSendMsg = new MTMessage("", address, "", msgBody);
			_mipcLite.SendAsync(mtSendMsg);
			var mtReceiveMsg = _mipcLite.SendReceive(mtSendMsg, 30);
			return mtReceiveMsg;
		}
	}
}
