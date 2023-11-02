using QueueMonitor.Configuration;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor
{
	class Program
	{
		private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
		static void Main(string[] args)
		{
			try
			{   
                new QueueMonitor().Start();
			}
			catch (Exception ex)
			{
				var msg = "Fatal error : " + ex.Message + ". Source:  " + ex.Source + ". StackTrace: " + ex.StackTrace + ". TargetSite:  " + ex.TargetSite;
                log.Fatal(msg);
                MailMessage mailObj = new MailMessage(ConfigurationManager.AppSettings["NotifyFrom"], ConfigurationManager.AppSettings["NotifyTo"], "QueueMonitor crashed with fatal error! Please check log..", msg);
				mailObj.IsBodyHtml = true;
				SmtpClient SMTPServer = new SmtpClient("exchange.micron.com");
				SMTPServer.DeliveryMethod = SmtpDeliveryMethod.Network;
				SMTPServer.Send(mailObj);
			}
		}
	}
}
