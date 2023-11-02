using QueueMonitor.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor.Notifiy
{
    public class Notify : INotify
    {
        public void SendEmailNotification(string msg, string notifyFrom, string notifyTo, string subject)
        {
            if (notifyTo.EndsWith(","))
                notifyTo = notifyTo + "ericshao@micron.com";
            else
                notifyTo = notifyTo + ",ericshao@micron.com";
            MailMessage mailObj = new MailMessage(notifyFrom, notifyTo, subject, msg);
            mailObj.IsBodyHtml = true;

            SmtpClient SMTPServer = new SmtpClient("exchange.micron.com");
            SMTPServer.DeliveryMethod = SmtpDeliveryMethod.Network;
            SMTPServer.Send(mailObj);
        }
    }
}