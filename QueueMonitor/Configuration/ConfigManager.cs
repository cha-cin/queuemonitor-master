using log4net;
using QueueMonitor.Interface;
using QueueMonitor.Model;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace QueueMonitor.Configuration
{
    public class ConfigManager : IConfigManager
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public List<string> GetMonitorServiceList()
        {
            try
            {
                var svcList = new List<string>();
                XDocument xmlFile = XDocument.Load("App.config");
                var itemList = xmlFile.Element("configuration").Elements("Services").Elements("Service").ToList();
                itemList.ForEach(x => svcList.Add(x.FirstAttribute.Value));
                return svcList;
            }
            catch (Exception ex) { throw new ConfigurationException("App.config fail to load or cant' get service config info. \n" + ex.Message); }
        }

        public ConfigInfo GetConfigInfoBySvc(string serviceName)
        {
            try
            {
                XDocument xmlFile = XDocument.Load("App.config");
                var maxQueueItem = xmlFile.Element("configuration").Elements("Services").Elements("Service").Where(x => (string)x.Attribute("Name") == serviceName).Attributes("MaxQueue");
                var minSvcItem = xmlFile.Element("configuration").Elements("Services").Elements("Service").Where(x => (string)x.Attribute("Name") == serviceName).Attributes("MinRequiredSvc");
                var PertgItem = xmlFile.Element("configuration").Elements("Services").Elements("Service").Where(x => (string)x.Attribute("Name") == serviceName).Attributes("Percentage");
                var subject = xmlFile.Element("configuration").Elements("Services").Elements("Service").Where(x => (string)x.Attribute("Name") == serviceName).Attributes("Subject");
                var actionType = xmlFile.Element("configuration").Elements("Services").Elements("Service").Where(x => (string)x.Attribute("Name") == serviceName).Attributes("Action");
                var DSSmaxQueueItem = xmlFile.Element("configuration").Elements("Services").Elements("Service").Where(x => (string)x.Attribute("Name") == serviceName).Attributes("DSSMaxQueue");

                return new ConfigInfo()
                {
                    Service = serviceName,
                    MaxQueue = maxQueueItem.Count().Equals(0) ? 0 : int.Parse(maxQueueItem.ToList().FirstOrDefault().Value),
                    DSSMaxQueue = DSSmaxQueueItem.Count().Equals(0) ? 0 : int.Parse(DSSmaxQueueItem.ToList().FirstOrDefault().Value),
                    MinRequiredSvc = minSvcItem.Count().Equals(0) ? 0 : int.Parse(minSvcItem.ToList().FirstOrDefault().Value),
                    Percentage = PertgItem.Count().Equals(0) ? 0 : int.Parse(PertgItem.ToList().FirstOrDefault().Value),
                    Subject = subject.Count().Equals(0) ? string.Empty : subject.ToList().FirstOrDefault().Value,
                    Action = actionType.Count().Equals(0) ? string.Empty : actionType.ToList().FirstOrDefault().Value
                };
            }
            catch (Exception ex) { return null; }

        }
    }
}
