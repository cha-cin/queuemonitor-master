using Autofac;
using Micron.FSS.Procman.Controllers;
using Micron.FSS.Procman.Objects.App;
using Micron.FSS.Procman.Objects.Process;
using Micron.Messaging.Mipc;
using QueueMonitor.Configuration;
using QueueMonitor.DAT;
using QueueMonitor.Enum;
using QueueMonitor.FileHandler;
using QueueMonitor.Interface;
using QueueMonitor.MIPCHandler;
using QueueMonitor.Model;
using QueueMonitor.Notifiy;
using QueueMonitor.Procman;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web;


[assembly: log4net.Config.XmlConfigurator(Watch = true)]
namespace QueueMonitor
{
    public class QueueMonitor
	{
		#region Create Instance
		private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
		private IConfigManager _configManager;
		private IDATManager _datManager;
		private IFileManager _fileManager;
		private IMIPCManager _mipcManager;
		private INotify _notify;
		private IProcmanManager _procmanManager;
		#endregion

		#region Init Variable
		private string _monitorTime;
		private string _debugMode;
		private string _siteName;
		private string _userName;
		private string _notifyFrom;
		private string _notifyTo;
		private string _mailSubject;
		private string _procContainer;
		#endregion

		#region Init
		public QueueMonitor()
		{
			_siteName = Environment.GetEnvironmentVariable("SITE_NAME");
            _userName = ConfigurationManager.AppSettings["Username"];
			_notifyFrom = ConfigurationManager.AppSettings["NotifyFrom"];
			_notifyTo = ConfigurationManager.AppSettings["NotifyTo"];
			_mailSubject = ConfigurationManager.AppSettings["MailSubject"];
			_procContainer = ConfigurationManager.AppSettings["PromanContainer"];
			_monitorTime = ConfigurationManager.AppSettings["MonitorTime"];
			_debugMode = ConfigurationManager.AppSettings["DebugMode"];
			log.Info(string.Format(@"Start to initialize... getting the parameter SITE_NAME : {0}, Username : {1}, NotifyFrom : {2} NotifyTo : {3}, MailSubject : {4}, PromanContainer : {5}, MonitorTime : {6}, DebugMode : {7}",
									_siteName, _userName, _notifyFrom, _notifyTo, _mailSubject, _procContainer, _monitorTime, _debugMode));
			Init();
			log.Info("Finishing...");
		}

		public void Init()
		{
			var builder = new ContainerBuilder();
			builder.RegisterType<ConfigManager>().As<IConfigManager>();
			builder.RegisterType<DATManager>().As<IDATManager>();
			builder.RegisterType<FileManager>().As<IFileManager>();
			builder.RegisterType<MIPCManager>().As<IMIPCManager>();
			builder.RegisterType<Notify>().As<INotify>();
			builder.RegisterType<ProcmanManager>().As<IProcmanManager>();

			var container = builder.Build();
			_configManager = container.Resolve<IConfigManager>();
			_datManager = container.Resolve<IDATManager>();
			_fileManager = container.Resolve<IFileManager>();
			_mipcManager = container.Resolve<IMIPCManager>();
			_notify = container.Resolve<INotify>();
			_procmanManager = container.Resolve<IProcmanManager>();
        }
		#endregion

		public void Start()
		{
            if (!CheckMIPCstatsFileExist()) { log.Error("Current Directory path is : " + CurrentDirectorPath()); throw new Exception("MIPCstats.exe does not exist! MIPCstats.exe should in RootPath directory. Please check the RootPath setting in config."); }
			var svcList = RetrieveConfigInformation();
			var monitorSvc = DataValidation(svcList);
            QueueMonitorCheck(monitorSvc);
            //check unregister service that did not restart.
            if (File.Exists("C:\\logs\\un.txt")){
                List<string> readText = File.ReadAllLines("C:\\logs\\un.txt").ToList();

                for (int i = 0; i < readText.Count; i++)
                {
                    string reply = sendMIPCMessage("<PING/>", readText[i].Substring(9, readText[i].Length - 9));
                    if (reply.StartsWith("ERROR"))     //remove from the file
                        readText.RemoveAt(i);
                    else
                        _notify.SendEmailNotification("Hi:<p>   Queue Monitor unrigister the service: " + readText[i].Substring(0, 8) + " but it detect the service did not restart. Please check!! <p>    Thanks.<p><p>    If it is false alarm, please delete the record in C:\\Logs\\un.txt", _notifyFrom, _notifyTo, _mailSubject + " Please check the service " + readText[i].Substring(0, 8));
                }

                File.WriteAllLines("C:\\logs\\un.txt", readText.ToArray());
            }

        }

        public bool CheckRegisteredNamesBySubject(string mipcAddress)
        {
            MIPC _loMipc = new MIPC();
            try {
                _loMipc.GetRegisteredNames(mipcAddress);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private List<string> RetrieveConfigInformation()
		{
			var info = _configManager.GetMonitorServiceList();
			if (info != null)
			{
				var count = info.FindAll(x => !x.Contains("Default")).Count;
				log.Info(string.Format("There are {0} services are under monitoring, service name are :", count));
				info.ForEach(x => log.Info(x + ", "));
				return info;
			}
			throw new ArgumentException("Incorrrect setup in App.config so it can't successfully retrieve service information.");
		}

		private List<ConfigInfo> DataValidation(List<string> serviceList)
		{
			log.Info("It's going to validate config value...");
			var infoList = new List<ConfigInfo>();
			var def = serviceList.Find(x => x.Contains("Default"));
			var defSetup = _configManager.GetConfigInfoBySvc(def);
			if (defSetup.MaxQueue < 0 || defSetup.Percentage < 0 || string.IsNullOrEmpty(defSetup.Action))
			{
				log.Error("Default vaule set up in the config which is not allowed, please check App.config.");
				throw new ArgumentException("MaxQueue, Percentage or Action default setup in the config is less than 0 or empty.");
			}

			serviceList = serviceList.FindAll(x => !x.Contains("Default"));
			foreach (var svc in serviceList)
			{
				log.Info("checking " + svc + "...");
				var info = _configManager.GetConfigInfoBySvc(svc);
				if (info == null)
				{
					log.Error(string.Format("{0} serivce config can not be correctly retrieved, this service will be ingored for further check. Removing this service...", svc));
					serviceList.Remove(svc); continue;
				}
				if (string.IsNullOrEmpty(info.Subject))
				{
					log.Error(svc + " has empty subject in the config, this service will be ingored for further check. Removing this service...");
					serviceList.Remove(svc); continue;
				}
				else { log.Info("Subject : " + info.Subject); }

				if (string.IsNullOrEmpty(info.Action))
				{
					log.Error(string.Format(@"{0} does not set action type, it uses default action {1}", svc, defSetup.Action));
					info.Action = defSetup.Action;
				}
				else { log.Info("Action Type : " + info.Action); }

				if (info.MaxQueue.Equals(0))
				{
					info.MaxQueue = defSetup.MaxQueue;
					log.Info(string.Format(@"{0} does not set MaxQueue, it uses default MaxQueue {1}", svc, defSetup.MaxQueue));
				}
				else { log.Info("MaxQueue : " + info.MaxQueue); }

                if (info.DSSMaxQueue.Equals(0))
                {
                    info.DSSMaxQueue = defSetup.DSSMaxQueue;
                    log.Info(string.Format(@"{0} does not set DSSMaxQueue, it uses default DSSMaxQueue {1}", svc, defSetup.DSSMaxQueue));
                }
                else { log.Info("DSSMaxQueue : " + info.DSSMaxQueue); }

                if (info.Percentage.Equals(0))
				{
					log.Info(string.Format(@"{0} does not set Percetage, it use default Percetage {1}", svc, defSetup.Percentage));
					info.Percentage = defSetup.Percentage;
				}
				else { log.Info("Percentage : " + info.Percentage); }
				infoList.Add(info);
			}
			return infoList;
		}

		private void QueueMonitorCheck(List<ConfigInfo> monitorSvc)
		{
			log.Info("It's going to do queue check...");
            foreach (var svc in monitorSvc)
            {
                var procmanSvc = new List<ProcmanInfo>();
                //var sitesubject = "/" + _sitename + svc.subject;
                //var siteSubject = "/" + "TAICHUNG_BE" + "/MTI/MFG/CTSOperationServer/TEST/MESSAGES";
                var siteSubject = "/" + _siteName + svc.Subject;
                log.Info("checking service " + svc.Service);
                if (svc.Service.Equals("MESSRVb") || svc.Service.Equals("MESController"))  
                {
                    try
                    {
                        MESCAndMESSRVQueueCheck(svc.Service, siteSubject, svc.MaxQueue,svc.MinRequiredSvc,svc.Percentage); continue;
                    }
                    catch(Exception e)
                    {
                        log.Error("MESCAndMESSRVQueueCheck fail. " + e.Message); continue;
                    }
                }
				else if (svc.Service.Equals("PUBLIC_MESSRVb"))
                {
                    procmanSvc = GetPublicMESSRVProcmanInfo(svc.Service);
                }
				else
				{
					procmanSvc = GetProcmanInfo(svc.Service); 
					if (procmanSvc == null)
					{
						var msg = string.Format(@"{0} set up to monitor queue but it can't query procman info, please check the service name is correctly set up in App.config", svc.Service);
						_notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " " + siteSubject);
						log.Error(msg);
						continue;
					}
				}
				log.Info("Procman totally have " + procmanSvc.ToList().Count + " services set up to Monitor!");
				procmanSvc.ForEach(x => log.Info(x.ContainerName + " " + x.HostName + " " + x.ProcessName));
                var mipcInfoList = ExecuteMIPCStatsToGetMIPCInfo(svc.Service, siteSubject);
                if (mipcInfoList == null || mipcInfoList.Count.Equals(0)) { log.Error("Script can't read mipc info from output file, will pass this check."); continue; } //there is one service alive at least

                var path = Directory.GetParent(Assembly.GetExecutingAssembly().Location).Parent.Parent.FullName + @"\OutputMAMOperationsServer_TEST.txt";
                List<string> recordold = new List<string>();
                using (StreamReader sr = new StreamReader(path))
                {
                    string reader;
                    while ((reader = sr.ReadLine()) != null)
                    {
                        if (reader.IndexOf("Unique Name") >= 0)
                        {
                            recordold.Add(reader);
                        }
                    }
                }

                var info = _mipcManager.GetRegisteredNamesBySubject(siteSubject);

                int flag = 0;
				foreach (var key in info.Keys)
				{
                    if (flag == 1)
                        break;
                    //log.Info(siteSubject + " has registered " + (string[])info[key] + " services on MIPC.");
                    var svcSubject = key.ToString();
					var actionType = ValidateActionType(svc.Action);
					var regSvcList = (string[])info[key];
					log.Info(siteSubject + " has registered " + regSvcList.Length + " services on MIPC.");
                     foreach (var regSvc in regSvcList)
					{
                        string strRegSvc = regSvc;
                        int last = strRegSvc.LastIndexOf("/");
                        strRegSvc = strRegSvc.Substring(last, strRegSvc.Length - last);
                        var hostname = strRegSvc.Split('_')[1];
						var pid = strRegSvc.Split('_')[2];

                        //DSS Queue start
                        if (svc.Service.ToUpper() == "LOTINFO" || svc.Service.ToUpper() == "LOTTRACK")
                        {
                            int DSSqueueSize = 0;
                            log.Info("Only LotInfo and LotTrack need to check DSS queue size.");
                            DSSqueueSize = GetBaservQueue(hostname, pid);
                            log.Info("DSS service : " + svc.Service + " hostname : " + hostname + " pid : " + pid + " queueSize : " + DSSqueueSize);

                            if (DSSqueueSize > svc.DSSMaxQueue)
                            {
                                log.Info(string.Format("Service DSS queue is over threshold, DSS queue size now is {0}, DSSMaxQueues is set to {1}.", DSSqueueSize, svc.DSSMaxQueue));
                                if (_debugMode.Equals("N"))
                                {
                                    float minRequiredNum = procmanSvc.Count * (float)(svc.Percentage / 100.00);
                                    var procman = procmanSvc.Find(x => x.HostName.ToUpper().Contains(hostname.ToUpper()) && x.Pid.Equals(pid));
                                    if (procman != null)
                                    {
                                        IEnumerable<ProcmanApp> apps = AppController.Singleton.GetApps(procman.ContainerName, procman.HostName, FilterType.EXACT_MATCH, procman.ProcessName, _userName);
                                        List<ProcmanApp> appList = new List<ProcmanApp>(apps);
                                        {
                                            foreach (var app in appList)
                                            {
                                                int currentSvcNum = ((string[])_mipcManager.GetRegisteredNamesBySubject(siteSubject)[key]).Count();
                                                if ((svc.Percentage > 0 && currentSvcNum > minRequiredNum) || (svc.Percentage.Equals(0) && svc.MinRequiredSvc > 0 && currentSvcNum > svc.MinRequiredSvc))
                                                {
                                                    string msg = "Service DSS queue is over threshold. DSS service: " + svc.Service + " hostname: " + hostname + " pid: " + pid + " queueSize: " + DSSqueueSize + ". DSS queue high, we unregister the service.";
                                                    log.Info(msg);
                                                    Unsubscribe(app.Processes[0], svcSubject, hostname, pid, svc.Service.ToUpper());
                                                    _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " " + svcSubject + "/" + hostname + "/" + pid);
                                                }
                                                else
                                                {
                                                    var msg = "DSS Queue size is greater than DSSMaxQueues, but it reached minimum active service count, so QueueMonitor only do restart.";
                                                    RestartService(app.Processes[0], svcSubject, hostname, pid);
                                                    _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " " + svcSubject + "/" + hostname + "/" + pid);
                                                    log.Error(msg);
                                                }
                                            }

                                        }

                                    }

                                }
                                else
                                {
                                    string msg = "Service DSS queue is over threshold. DSS service: " + svc.Service + " hostname: " + hostname + " pid: " + pid + " queueSize: " + DSSqueueSize + "Debug mode is Y so we don't do any action.";
                                    log.Info(msg);
                                    _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " " + svcSubject + "/" + hostname + "/" + pid);
                                }

                            }          
                        }
                        //DSS Queue end

                        var mipcInfo = mipcInfoList.Find(x => x.PID.Equals(pid) && x.Hostname.Equals(hostname)) ?? new MIPCInfo();
						var queueSize = mipcInfo.ReceiveQueueSize;
						log.Info("MIPC service : " + svc.Service + " hostname : " + hostname + " pid : " + pid + " queueSize : " + queueSize);

                        if (queueSize > svc.MaxQueue)
						{
							float minRequiredNum = procmanSvc.Count * (float)(svc.Percentage / 100.00);
							log.Info(string.Format("Service MIPC queue is over threshold, queue size now is {0}, MaxQueues is set to {1}. And minimume required service count is : {2}", queueSize, svc.MaxQueue, minRequiredNum));
							var procman = procmanSvc.Find(x => x.HostName.ToUpper().Contains(hostname.ToUpper()) && x.Pid.Equals(pid));
                            if (procman != null)
                            {
                                IEnumerable<ProcmanApp> apps = AppController.Singleton.GetApps(procman.ContainerName, procman.HostName, FilterType.EXACT_MATCH, procman.ProcessName, _userName);
                                List<ProcmanApp> appList = new List<ProcmanApp>(apps);
                                foreach (var app in appList)
                                {
                                    int currentSvcNum = ((string[])_mipcManager.GetRegisteredNamesBySubject(siteSubject)[key]).Count();
                                    //Priority : Percentage > MinRequiredSvc
                                    if ((svc.Percentage > 0 && currentSvcNum > minRequiredNum) || (svc.Percentage.Equals(0) && svc.MinRequiredSvc > 0 && currentSvcNum > svc.MinRequiredSvc))
                                    {
                                        log.Info("This service is allowed to do action since it does not reach minimum required count.");
                                        if (PreCheck(app.Processes[0].AppProcessName, hostname) && !_debugMode.Equals("N")) //Debug mode control. If 'Y' can't do action.
                                        {
                                            switch (actionType)
                                            {
                                                case ActionType.Stop:
                                                    StopService(app.Processes[0], svcSubject, hostname, pid);
                                                    break;
                                                case ActionType.Restart:
                                                    RestartService(app.Processes[0], svcSubject, hostname, pid);
                                                    break;
                                                case ActionType.Failover:
                                                    FailoverService(app, svcSubject, hostname, pid);
                                                    break;
                                                case ActionType.StopUnsubscribe:
                                                    StopUnsubscribe(app.Processes[0], svcSubject, hostname, pid);
                                                    break;
                                                case ActionType.Unsubscribe:
                                                    Unsubscribe(app.Processes[0], svcSubject, hostname, pid, svc.Service);
                                                    System.Threading.Thread.Sleep(12000);
                                                    RestartService(app.Processes[0], svcSubject, hostname, pid);
                                                    var workingDir = CurrentDirectorPath();
                                                    var outputfile = CreatFile(workingDir, "Check_Service_alive", svcSubject);

                                                    var path2 = Directory.GetParent(Assembly.GetExecutingAssembly().Location).Parent.Parent.FullName + @"\OutputCheck_Service_alive.txt";
                                                    List<string> recordnew = new List<string>();
                                                    using (StreamReader sr = new StreamReader(path2))
                                                    {
                                                        string reader;
                                                        while ((reader = sr.ReadLine()) != null)
                                                        {
                                                            Console.WriteLine(reader);
                                                            if (reader.IndexOf("Unique Name") >= 0)
                                                            {
                                                                recordnew.Add(reader);
                                                            }
                                                        }
                                                    }

                                                    if (recordnew.Count != recordold.Count)
                                                    {
                                                        Console.WriteLine("restart problem");
                                                        flag = 1;
                                                    }

                                                    string newone = "";
                                                    string oldone = "";
                                                    foreach (var i in recordnew)
                                                    {
                                                        if (i.IndexOf(hostname) >= 0)
                                                        {
                                                            newone = i;
                                                        }    
                                                    }
                                                    foreach (var i in recordold)
                                                    {
                                                        if (i.IndexOf(hostname) >= 0)
                                                        {
                                                            oldone = i;
                                                        }
                                                    }
                                                    if (string.Equals(newone, oldone) is true)
                                                    {
                                                        Console.WriteLine("The same pid");
                                                        flag = 1;
                                                    }

                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            var msg = "But service has been " + actionType + " within " + _monitorTime + " minutes, it would not do any action this time!";
                                            _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " " + svcSubject + "/" + hostname + "/" + pid);
                                            log.Error(msg);
                                        }
                                    }
                                    else
                                    {
                                        //var msg = "baserv Queue size is greater than max queue, but it reached minimum active service count, so QueueMonitor only do restart.";
                                        var msg = "MIPC Queue size is greater than max queue, but it reached minimum active service count, so QueueMonitor do notthing.";
                                        //appList.ForEach(x => RestartService(x.Processes[0], svcSubject, hostname, pid));
                                        //_notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " " + svcSubject + "/" + hostname + "/" + pid);
                                        log.Error(msg);
                                    }
                                }
                                
                            }
						}
					}
				}
			}
		}

		private void MESCAndMESSRVQueueCheck(string service, string subject, int maxQueue, int minSvc, int percentage)
		{
			var inputfile = string.Format(@"Input{0}.txt", service);
			var outputfile = string.Format(@"Output{0}.txt", service);
			var workingDir = CurrentDirectorPath();
			var procmanSvc = GetPublicMESSRVProcmanInfo(service);
            
            if (procmanSvc != null)
            {
                if (service == "MESController")
                {
                    var procmanInsSvc = GetMSCInstProcmanInfo(service);
                    using (StreamWriter output = File.CreateText(outputfile)) { output.Close(); log.Info("Create output file : " + outputfile); }
                    using (StreamWriter input = File.CreateText(inputfile))
                    {
                        log.Info("Create input file : " + inputfile);
                        foreach (var proc in procmanInsSvc)
                        {
                            var instance = proc.ProcessName.Split('_').Last();
                            var insInfo = subject + instance;
                            input.WriteLine(insInfo);
                            log.Info(insInfo);
                        }
                        input.Close();
                    }
                }
                else
                {
                    using (StreamWriter output = File.CreateText(outputfile)) { output.Close(); log.Info("Create output file : " + outputfile); }
                    using (StreamWriter input = File.CreateText(inputfile))
                    {
                        log.Info("Create input file : " + inputfile);
                        foreach (var proc in procmanSvc)
                        {
                            var instance = proc.ProcessName.Split('_').Last();
                            var insInfo = subject + instance;
                            input.WriteLine(insInfo);
                            log.Info(insInfo);
                        }
                        input.Close();
                    }
                }
                DateTime startTime = DateTime.Now;
                MIPCstats(workingDir, inputfile, outputfile);
                DateTime stopTime = DateTime.Now;
                TimeSpan duration = stopTime - startTime;
                log.Info("Overall execution time to get mipc states [" + duration.TotalMilliseconds + "]");
                Thread.Sleep(3000);

                var itemList = ReadFile(workingDir + "\\" + outputfile);
                var mipcInfoList = itemList.OrderBy(x => x.Hostname ).ToList();
                foreach (var mipc in mipcInfoList)
                {
                    var process = procmanSvc.Find(x => x.HostName.ToUpper().Contains(mipc.Hostname.ToUpper()) && x.Pid.Equals(mipc.PID));
                        if (process == null) { log.Error("It does not get mipc information for " + mipc.Hostname + " with PID : " + mipc.PID + " , passing this check!"); continue; }
                    else { log.Info("MIPC service : " + process.ProcessName + " hostname : " + process.HostName + " pid : " + mipc.PID + " queueSize : " + mipc.ReceiveQueueSize); }

                    var queueSize = mipc.ReceiveQueueSize;
                    if (queueSize > maxQueue)
                    {
                        log.Info(string.Format("{0} MIPC queue is over threshold, queue size now is {1}, MaxQueues is set to {2}.", process.ProcessName, queueSize, maxQueue));
                        var instance = process.ProcessName.Split('_').Last();
                        var svcGroup = GetInstanceInfo(instance,service);

                        if (service == "MESController")
                        {
                            foreach (var svc in svcGroup)
                            {

                                float minRequiredNum = procmanSvc.Count * (float)(percentage / 100.00);
                                log.Info(string.Format("Service MIPC queue is over threshold, queue size now is {0}, MaxQueues is set to {1}. And minimume required service count is : {2}", queueSize, maxQueue, minRequiredNum));
                                //var procman = procmanSvc.Find(x => x.HostName.ToUpper().Contains("120"));
                                var procman = procmanSvc.Find(x => x.HostName.ToUpper().Contains(process.HostName.ToUpper()) && x.Pid.Equals(process.Pid));
                                if (procman != null)
                                {
                                    IEnumerable<ProcmanApp> apps = AppController.Singleton.GetApps(procman.ContainerName, procman.HostName, FilterType.EXACT_MATCH, procman.ProcessName, _userName);
                                    List<ProcmanApp> appList = new List<ProcmanApp>(apps);
                                    {
                                        foreach (var app in appList)
                                        {
                                            string msg = "Service MIPC queue is over threshold. MIPC service: " + service + " hostname: " + process.HostName + " pid: " + process.Pid + " queueSize: " + queueSize + ". MIPC queue high, we restart the service.";
                                            log.Info(msg);
                                            if (_debugMode.Equals("Y"))
                                            {
                                                msg = "Debug Mode equal Y so we do not do any action. "+ msg;
                                                _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " " + subject + process.ProcessName.Split('_').Last() + "/" + process.HostName + "/" + process.Pid);
                                            }
                                            else
                                            {
                                                RestartService(app.Processes[0], process.ProcessName, process.HostName, process.Pid);
                                                _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " " + subject + process.ProcessName.Split('_').Last() + "/" + process.HostName + "/" + process.Pid);
                                            }
                                        }
                                    }
                                }
                            }
                        //var procman = procmanSvc.Find(x => x.HostName.ToUpper().Contains(HostName.ToUpper()) && x.Pid.Equals(Pid));
                        }
                        else
                        {
                            foreach (var svc in svcGroup)
                            {
                                IEnumerable<ProcmanApp> apps = AppController.Singleton.GetApps(svc.ContainerName, svc.HostName, FilterType.EXACT_MATCH, svc.ProcessName, _userName);
                                List<ProcmanApp> appList = new List<ProcmanApp>(apps);
                                foreach (var app in appList)
                                {
                                    if (PreCheck(app.Processes[0].AppProcessName, mipc.Hostname) && !_debugMode.Equals("Y"))
                                    {
                                        var backup = app.Processes.Find(x => x.ProcStatus.Equals("Stopped"));
                                        if (backup == null)
                                        {
                                            var msg = string.Format(@"{0} queue is over threashold but the backup service state is not Stopped, to prevent it's not runnable (ex:Aborted) so we won't failover.", app.Processes[0].AppProcessName);
                                            log.Fatal(msg);
                                            _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " Fail to failover " + app.AppContainerName + "/" + backup.HostHostName + "/" + backup.AppProcessName);
                                            continue;
                                        }

                                        AppController.Singleton.FailoverApp(app, _userName, 15000);  //need to enable but now it's testing so we disable this
                                        var status = ServiceStatusCheck(app.AppContainerName, backup.AppProcessName, backup.HostHostName);
                                        if (!status.Trim().Equals("Running"))
                                        {
                                            var msg = string.Format(@"Fail to failover : {0} {1}, it's going to failover back!", app.AppContainerName, app.AppProcessName);
                                            log.Fatal(msg);
                                            IEnumerable<ProcmanApp> bkApps = AppController.Singleton.GetApps(backup.AppContainerName, backup.HostHostName, FilterType.EXACT_MATCH, backup.AppProcessName, _userName);
                                            List<ProcmanApp> bkList = new List<ProcmanApp>(bkApps);
                                            foreach (var bk in bkList) { AppController.Singleton.FailoverApp(app, _userName, 15000); }  //need to enable but now it's testing so we disable this
                                            _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " Fail to failover " + app.AppContainerName + "/" + backup.HostHostName + "/" + backup.AppProcessName);
                                        }
                                        else
                                        {
                                            var msg = string.Format(@"Successfully failover {0} {1} {2}", app.AppContainerName, app.Processes[0].HostHostName, app.Processes[0].AppProcessName);
                                            log.Info(msg);
                                            _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + " Successfully to failover " + app.AppContainerName + "/" + backup.HostHostName + "/" + backup.AppProcessName);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
		}

		public List<MIPCInfo> ExecuteMIPCStatsToGetMIPCInfo(string service, string subject)
		{
            var workingDir = CurrentDirectorPath();
			var outputfile = CreatFile(workingDir, service, subject);
			Thread.Sleep(3000);
			var mipcInfo = ReadFile(workingDir + "\\" + outputfile);
			return mipcInfo;
		}

		private int LoadMessageQueue(string msgContents)
		{
			ConfigXmlDocument xml = new ConfigXmlDocument();
			xml.LoadXml(HttpUtility.HtmlDecode(msgContents).Trim());
			var msgQueue = int.Parse(xml.GetElementsByTagName("MessagesQueued")[0].InnerText);
			//log.Info("DSS Message queue : " + msgQueue);
			return msgQueue;
		}

        private ActionType ValidateActionType(string type)
		{
			var value = type.ToUpper();
			switch (value)
			{
				case "STOP":
					return ActionType.Stop;
				case "FAILOVER":
					return ActionType.Failover;
				case "STOPUNSUBSCRIBE":
					return ActionType.StopUnsubscribe;
                case "UNSUBSCRIBE":
					return ActionType.Unsubscribe;
                default:
					return ActionType.Restart;
			}
		}

		private string ServiceStatusCheck(string containerName, string processName, string hostname)
		{
			string str = string.Format(@"
        								set transaction isolation level read uncommitted
        								SELECT status
        								FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        								WHERE monitor = 'Y' AND startup <> 'disable'AND group_name = '{0}'
        								AND pro.process_name LIKE '%{1}%' AND host_name = '{2}'", containerName, processName, hostname);

			if (_debugMode.Equals("Y")) { PrintSql(str); }
			var conn = _datManager.GetProcmanConnection();
			SqlCommand cmd = new SqlCommand(str, conn);
			conn.Open();
			using (var reader = cmd.ExecuteReader())
			{
				var status = string.Empty;
				var infoList = new List<ProcmanInfo>();
				while (reader.Read())
				{
					return reader["status"].ToString();
				}
			}
			return string.Empty;
		}

		private int GetBaservQueue(string hostname, string pid)
		{
            try
            {
                var querySubject = "/" + _siteName + "/MTI/MFG/OPENAUTO/SUPPORTSVC/" + hostname + "/" + pid;
                log.Info("Retrieve DSS/MIPC queue from subject : " + querySubject);
                var mipcInfo = (MTMessage)_mipcManager.GetMIPCInfoByService(querySubject, hostname, pid);
                if (mipcInfo == null) { log.Error(string.Format("It can't get queue information from {0} with pid : {1}\nIgnore this service, continue checking next one...", hostname, pid)); }
                return LoadMessageQueue(mipcInfo.MsgContents);
            }
            catch
            {
                log.Error(string.Format("It can't get queue information from {0} with pid : {1}\nIgnore this service, continue checking next one...", hostname, pid)); 
                return 0;
            }
		}

        private bool IsPoolProcessStateAllFree(string hostname, string pid)
        {
            try
            {
                var querySubject = "/" + _siteName + "/MTI/MFG/OPENAUTO/SUPPORTSVC/" + hostname + "/" + pid;
                log.Info("Retrieve pool process state from subject : " + querySubject);
                var mipcInfo = sendMIPCMessage("<DS_STATE_TABLE/>",querySubject);
                if (mipcInfo.Contains("PENDING") || mipcInfo.Contains("BUSY"))
                    return false;
                else
                    return true;
            }
            catch
            {
                log.Error(string.Format("It can't get pool process state information from {0} with pid : {1}.\n", hostname, pid));
                return false;
            }
        }

        private string sendMIPCMessage(string message, string dest)
        {
            MIPC mipc = new MIPC();
            MTMessage sendMessage = new MTMessage();
            try
            {
                sendMessage.MsgContents = message;
                sendMessage.Destination = dest;
                MTMessage recvMessage = mipc.SendReceive(sendMessage, 20); // SelectionEngine's timeout is 30, ExecutionEngine must in below for the exception can be returned to source.
                mipc.Dispose();
                log.Info("MIPC Dispose. " + DateTime.Now + "\n");
                return recvMessage.MsgContents;
            }
            catch (Exception error)
            {
                log.Error("MIPC Dispose due to Error:" + error.Message);

                return ("ERROR: " + error.Message);
            }
        }



        #region File Handle
        private string CreatFile(string workingDir, string service, string subject)
		{
			var inputfile = string.Format(@"Input{0}.txt", service);
			var outputfile = string.Format(@"Output{0}.txt", service);
			using (StreamWriter input = File.CreateText(inputfile))
			{
                if (service == "MESController")
                {
                    var procmanSvc = GetMSCInstProcmanInfo(service);
                    if (procmanSvc != null)
                    {

                            log.Info("Create input file : " + inputfile);
                            foreach (var proc in procmanSvc)
                            {
                                var instance = proc.ProcessName.Split('_').Last();
                                var insInfo = subject + instance;
                                input.WriteLine(insInfo);
                                log.Info(insInfo);
                            }
                            input.Close();
                    }
                }
                else
                {
                    input.WriteLine(subject);
                    log.Info("Create input file : " + inputfile);
                    input.Close();
                }
			}
			using (StreamWriter sw = File.CreateText(outputfile)) { log.Info("Create output file : " + outputfile); }
			MIPCstats(workingDir, inputfile, outputfile);
			return outputfile;
		}

		private void MIPCstats(string workingDir, string inputfile, string outputfile)
		{
			DateTime startTime = DateTime.Now;
			var processStartInfo = new ProcessStartInfo();
			processStartInfo.WorkingDirectory = workingDir;
			processStartInfo.FileName = "MIPCstats.exe";
			processStartInfo.Arguments = string.Format(@"{0} {1}", inputfile, outputfile);
			var proc = Process.Start(processStartInfo);
			DateTime stopTime = DateTime.Now;
			TimeSpan duration = stopTime - startTime;
			log.Info("Overall execution time to get mipc states [" + duration.TotalMilliseconds + "]");
		}

		private List<MIPCInfo> ReadFile(string filepath)
		{
			try
			{
				using (StreamReader file = new StreamReader(filepath))
				{
					string line;
					var mipcList = new List<MIPCInfo>();
					var mipc = new MIPCInfo();
					while ((line = file.ReadLine()) != null)
					{
						if (line.Contains("Invalid API Parameter Count"))
						{
							mipcList.Add(mipc);
							mipc = new MIPCInfo();
						}
						if (line.Contains("Hostname:"))
						{
							mipc.Hostname = line.Split(':')[1].Trim();
						}
						if (line.Contains("Unique Name:"))
						{
                            int last = line.LastIndexOf("/");
                            string strLine = line.Substring(last, line.Length - last);
                            mipc.PID = strLine.Split('_')[2];
						}
						if (line.Contains("Receive Queue Size"))
						{
							mipc.ReceiveQueueSize = int.Parse(line.Split(':')[1].Trim());
						}
					}

					var data = mipcList.FindAll(x => !string.IsNullOrEmpty(x.Hostname));
					return data;
				}
			}
			catch (Exception ex)
			{
				if (ex.Message.Contains("is being used by another process")) { Thread.Sleep(3000); return ReadFile(filepath); }
				else { return null; }
			}
		}

		private string CurrentDirectorPath()
		{
			return Directory.GetCurrentDirectory();
		}

		private bool CheckMIPCstatsFileExist()
		{
			//Directory.SetCurrentDirectory(Directory.GetCurrentDirectory() + "../../../");
			Directory.SetCurrentDirectory(ConfigurationManager.AppSettings["RootPath"]);
			if (!File.Exists("MIPCstats.exe"))
            {
                log.Error("MIPCstats.exe does not exist");
                Console.WriteLine("MIPCstats.exe does not exist");
                return false;
            }
			log.Info("Successfully load MIPCstats.exe");
			return true;
		}

		#endregion

		#region Restart/Stop/Failover
		private void StopService(ProcmanProcess procman, string mipcSubject, string hostname, string pid)
		{
            try
            {
                AppController.Singleton.StopProcess(procman, _userName, 15000);  //need to enable but now it's testing so we disable this
                var msg = "Successfully stop service " + mipcSubject + "/" + hostname + "/" + pid;
                var mailSubject = _mailSubject + mipcSubject + "/" + hostname + "/" + pid;
                _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, mailSubject);
                log.Info(msg);
            }
            catch (Exception e)
            {
                var mailSubject = _mailSubject + mipcSubject + "/" + hostname + "/" + pid;
                var errmsg = string.Format(@"Could not stop service {0}/{1}/{2}.{3}", mipcSubject, hostname, pid, e.Message);
                _notify.SendEmailNotification(errmsg, _notifyFrom, _notifyTo, mailSubject);
                log.Error(errmsg);
            }

        }

		private void RestartService(ProcmanProcess procman, string mipcSubject, string hostname, string pid)
		{
            try
            {
                AppController.Singleton.RestartProcess(procman, _userName, 15000);  //need to enable but now it's testing so we disable this
                var msg = string.Format(@"Successfully restart service {0}/{1}/{2}", mipcSubject, hostname, pid);
                var mailSubject = _mailSubject + mipcSubject + "/" + hostname + "/" + pid;
                _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, mailSubject);
                log.Info(msg);
            }
            catch (Exception e)
            {
                var errmsg = string.Format(@"Could not restart service {0}/{1}/{2}.{3}", mipcSubject, hostname, pid, e.Message);
                var mailSubject = _mailSubject + mipcSubject + "/" + hostname + "/" + pid;
                _notify.SendEmailNotification(errmsg, _notifyFrom, _notifyTo, mailSubject);
                log.Error(errmsg);
            }
            
		}

		private void FailoverService(ProcmanApp app, string mipcSubject, string hostname, string pid)
		{
            try
            {
                AppController.Singleton.FailoverApp(app, _userName, 15000); //need to enable but now it's testing so we disable this
                var msg = "Successfully failover service " + mipcSubject + "/" + hostname + "/" + pid;
                var mailSubject = _mailSubject + mipcSubject + "/" + hostname + "/" + pid;
                _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, mailSubject);
                log.Info(msg);
            }
            catch (Exception e)
            {
                var errmsg = string.Format(@"Could not failover service {0}/{1}/{2}.{3}", mipcSubject, hostname, pid, e.Message);
                //errmsg+= string.Format(@" We try to start service {0}/{1}/{2}.", mipcSubject, hostname, pid);
                //AppController.Singleton.StartProcess(procman, _userName, 15000);
                var mailSubject = _mailSubject + mipcSubject + "/" + hostname + "/" + pid;
                _notify.SendEmailNotification(errmsg, _notifyFrom, _notifyTo, mailSubject);
                log.Error(errmsg);
            }

        }

		private void StopUnsubscribe(ProcmanProcess procman, string mipcSubject, string hostname, string pid)
		{

			var msg = string.Empty;
            try
            {

                var receiveMsg = _mipcManager.UnsubscribeViaSupportSVC(mipcSubject, hostname, pid);
                msg = mipcSubject + "/" + hostname + "/" + pid + " .Unsubscribe Via Support SVC.";
                /*
                bool result = CheckRegisteredNamesBySubject(mipcSubject);
                if (result==true)
                {
                    msg += string.Format(@"Successfully unsubscribe on MIPC with {0}! ", mipcSubject);
                    log.Info(msg);

                }
                else
                {
                    msg = "Error occurs on Unsubscribe MIPC! Restart service instead of stopping it!\nError info : " + receiveMsg.MsgContents;
                    log.Error(msg);
                    AppController.Singleton.RestartProcess(procman, _userName, 15000);  //need to enable but now it's testing so we disable this
                    _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + mipcSubject + "/" + hostname + "/" + pid;);
                    return;
                }
                */
                 
            }
            catch (Exception e)
            {
                msg = mipcSubject + "/" + hostname + "/" + pid + " .Unsubscribe Via Support SVC Fail." + e.Message;
                log.Error(msg);
                //_notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + mipcSubject + "/" + hostname + "/" + pid);
            }

            try
            {
                AppController.Singleton.StopProcess(procman, _userName, 15000);  //need to enable but now it's testing so we disable this
                msg += string.Format(@"Successfully stop the service {0}! ", mipcSubject);
                _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + mipcSubject + "/" + hostname + "/" + pid);
                log.Info(msg);
                 
            }
            catch (Exception e)
            {
                msg += string.Format(@"Could not stop service {0}/{1}/{2}.{3}", mipcSubject, hostname, pid, e.Message);
                _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + mipcSubject + "/" + hostname + "/" + pid);
                log.Error(msg);
                
            }
            
  
		}
        private void Unsubscribe(ProcmanProcess procman, string mipcSubject, string hostname, string pid, string servicename)
        {
            var msg = string.Empty;
            bool unrigister = true;
            try
            {
                var receiveMsg = _mipcManager.UnsubscribeViaSupportSVC(mipcSubject, hostname, pid);
                msg = mipcSubject + "/" + hostname + "/" + pid + ". Unsubscribe Via Support SVC.";
                //_notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + mipcSubject + "/" + hostname + "/" + pid);
                log.Info(msg);
                //Thread.Sleep(10000);
                //if (IsPoolProcessStateAllFree(hostname, pid))
                //    RestartService(procman, mipcSubject, hostname, pid);
                /*
                bool result = CheckRegisteredNamesBySubject(mipcSubject);
                if (result == false)
                {
                    msg += string.Format(@"Successfully unsubscribe on MIPC with {0} and Stop service! ", mipcSubject);
                    log.Info(msg);

                }
                else
                {

                    msg = "Error occurs on Unsubscribe MIPC!" + receiveMsg.MsgContents;
                    log.Error(msg);
                    _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject);
                    return;
                }
                */
            }
            catch (Exception e1)
            {
                msg = mipcSubject + "/" + hostname + "/" + pid + ". Exception: Unsubscribe Via Support SVC." + e1.Message;
                log.Error(msg);

                try
                {
                    var receiveMsg = _mipcManager.UnsubscribeViaSupportSVC(mipcSubject, hostname, pid);
                    bool result = CheckRegisteredNamesBySubject(mipcSubject);
                    if (result == false)
                    {
                        msg += string.Format(@"Successfully unsubscribe on MIPC with {0} ! ", mipcSubject);
                        log.Info(msg);
                        //Thread.Sleep(10000);
                        // if (IsPoolProcessStateAllFree(hostname, pid))
                        //    RestartService(procman, mipcSubject, hostname, pid);

                    }
                }
                catch (Exception e2)
                {
                    msg = mipcSubject + "/" + hostname + "/" + pid + ". Exception: Unsubscribe Via Support SVC." + e2.Message;
                    log.Error(msg);
                    _notify.SendEmailNotification(msg, _notifyFrom, _notifyTo, _mailSubject + mipcSubject + "/" + hostname + "/" + pid);
                    unrigister = false;
                }
                



            }
            finally
            {
                if (unrigister == true)
                {
                    StreamWriter log = new StreamWriter("C:\\logs\\un.txt", append: true);
                    log.WriteLine(servicename+",/" + _siteName + "/MTI/MFG/OPENAUTO/SUPPORTSVC/" + hostname + "/" + pid);
                    
                    log.Close();
                }

            }

        }
        #endregion

        #region //Procman
        private List<ProcmanInfo> GetProcmanInfo(string service)
		{
            try
            {
                string str = string.Empty;

                if (service.Equals("MESController"))
                {
                    str = string.Format(@"
                                    set transaction isolation level read uncommitted
        							SELECT group_name, pro.process_name, host_name, pid
        							FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        							WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}'
        							AND pro.process_name LIKE '%{1}%' AND pro.process_name NOT LIKE '%SUPPORT%' AND status = 'Running'", _procContainer, service);
                }
                else if (service.Equals("MAMOperationsServer_TEST"))
                {
                    str = string.Format(@"
                                    set transaction isolation level read uncommitted
        							SELECT group_name, pro.process_name, host_name, pid
        							FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        							WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}'
        							AND pro.process_name LIKE '%{1}%' AND pro.process_name NOT LIKE '%SUPPORT%' AND status = 'Running'", _procContainer, service);

                }
                else
                {
                    str = string.Format(@"        								
        								SELECT group_name, pro.process_name, host_name, pid
        								FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        								WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}' AND status = 'Running'
        								AND pro.process_name LIKE '%{1}%'", _procContainer, service);
                }

                if (_debugMode.Equals("Y")) { PrintSql(str); }
                var conn = _datManager.GetProcmanConnection();
                SqlCommand cmd = new SqlCommand(str, conn);
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    var infoList = new List<ProcmanInfo>();
                    while (reader.Read())
                    {
                        infoList.Add(
                             new ProcmanInfo()
                             {
                                 ContainerName = reader["group_name"].ToString(),
                                 HostName = reader["host_name"].ToString(),
                                 ProcessName = reader["process_name"].ToString(),
                                 Pid = reader["pid"].ToString()
                             }
                        );
                    }
                    return infoList;
                }


            


            }
            catch (Exception e)
            {
                log.Error("GetProcmanInfo error!");
                var infoList = new List<ProcmanInfo>();
                return infoList;
            }
		}

        private List<ProcmanInfo> GetMSCInstProcmanInfo(string service)
        {
            try
            {

             string  str = string.Format(@"
                                    set transaction isolation level read uncommitted
        							SELECT distinct pro.process_name 
        							FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        							WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}'
        							AND pro.process_name LIKE '%{1}%' AND pro.process_name NOT LIKE '%SUPPORT%' AND status = 'Running'", _procContainer, service);

                if (_debugMode.Equals("Y")) { PrintSql(str); }
                var conn = _datManager.GetProcmanConnection();
                SqlCommand cmd = new SqlCommand(str, conn);
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    var infoList = new List<ProcmanInfo>();
                    while (reader.Read())
                    {
                        var a = reader.Read().ToString();
                        infoList.Add(
                             new ProcmanInfo()
                             {
                                 ProcessName = reader["process_name"].ToString(),
                             }
                        );
                    }
                    return infoList;
                }





            }
            catch (Exception e)
            {
                log.Error("GetProcmanInfo error!");
                var infoList = new List<ProcmanInfo>();
                return infoList;
            }
        }



        private List<ProcmanInfo> GetInstanceInfo(string instance, string service)
		{

            string str="";
            if (service.Contains("MESController"))
            {
                str = string.Format(@"SELECT group_name, pro.process_name, host_name, pid
        								FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        								WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}' AND pro.status = 'Running' and pro.process_name LIKE '%MESController%' AND pro.process_name LIKE '%{1}%'", _procContainer, instance);
                PrintSql(str);
            }
            else if (service.Contains("MESSRVb"))
            {
                str = string.Format(@"SELECT group_name, pro.process_name, host_name, pid
        								FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        								WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}' AND pro.status = 'Running' AND (pro.process_name LIKE '%MESSRVb%' OR pro.process_name LIKE '%FE_ETA%' )  AND pro.process_name LIKE '%{1}%'", _procContainer, instance);
                PrintSql(str);
            }
                   //AND (pro.process_name LIKE '%MESSRVb%' OR pro.process_name LIKE '%MESController%' OR pro.process_name LIKE '%FE_ETA%' ) 


			var conn = _datManager.GetProcmanConnection();
			SqlCommand cmd = new SqlCommand(str, conn);
			conn.Open();
			using (var reader = cmd.ExecuteReader())
			{
				var infoList = new List<ProcmanInfo>();
				while (reader.Read())
				{
					infoList.Add(
						 new ProcmanInfo()
						 {
							 ContainerName = reader["group_name"].ToString(),
							 HostName = reader["host_name"].ToString(),
							 ProcessName = reader["process_name"].ToString(),
							 Pid = reader["pid"].ToString()
						 }
					);
				}
				return infoList;
			}
		}

		private List<ProcmanInfo> GetPublicMESSRVProcmanInfo(string service)
		{
            try
            {
                string str = string.Empty;
                if (service.Equals("MESSRVb"))
                {
                    str = string.Format(@"
       								set transaction isolation level read uncommitted
       								SELECT group_name, pro.process_name, host_name, pid
       								FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
       								WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}'
       								AND pro.process_name LIKE '%{1}%' AND pro.process_name NOT LIKE '%PUBLIC%' AND status = 'Running'", _procContainer, service);
                }
                else if (service.Equals("PUBLIC_MESSRVb"))
                {
                    str = string.Format(@"
        							set transaction isolation level read uncommitted
        							SELECT group_name, pro.process_name, host_name, pid
        							FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        							WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}'
        							AND pro.process_name like '%PUBLIC_MIPC%' AND status = 'Running'", _procContainer, service);
                }
                else if (service.Equals("MESController"))
                {
                    str = string.Format(@"
                                    set transaction isolation level read uncommitted
        							SELECT group_name, pro.process_name, host_name, pid
        							FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        							WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}'
        							AND pro.process_name LIKE '%{1}%' AND pro.process_name NOT LIKE '%SUPPORT%' AND status = 'Running'", _procContainer, service);
                }
                else if (service.Equals("MAMOperationsServer_TEST"))
                {
                    str = string.Format(@"
                                    set transaction isolation level read uncommitted
        							SELECT group_name, pro.process_name, host_name, pid
        							FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        							WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}'
        							AND pro.process_name LIKE '%{1}%' AND pro.process_name NOT LIKE '%SUPPORT%' AND status = 'Running'", _procContainer, service);
                }

                if (_debugMode.Equals("Y")) { PrintSql(str); }
                var conn = _datManager.GetProcmanConnection();
                SqlCommand cmd = new SqlCommand(str, conn);
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    var infoList = new List<ProcmanInfo>();
                    while (reader.Read())
                    {
                        infoList.Add(
                             new ProcmanInfo()
                             {
                                 ContainerName = reader["group_name"].ToString(),
                                 HostName = reader["host_name"].ToString(),
                                 ProcessName = reader["process_name"].ToString(),
                                 Pid = reader["pid"].ToString()
                             }
                        );
                    }
                    return infoList;
                }
            }
            catch (Exception ex)
            {
                var infoList = new List<ProcmanInfo>();
                return infoList;

            }
		}

		private List<ProcmanInfo> GetMESCProcmanInfo(string instance)
		{
			string str = string.Format(@"
        								set transaction isolation level read uncommitted
        								SELECT group_name, pro.process_name, host_name, pid
        								FROM [procman].[dbo].[process] pro LEFT JOIN [procman].[dbo].[application] app ON pro.application_OID = app.application_OID
        								WHERE monitor = 'Y' AND startup <> 'disable' AND group_name = '{0}' AND status = 'Running'
        								AND  (pro.process_name LIKE '%FE_MESSRVb_PROD%' OR pro.process_name LIKE '%FE_MESController_PROD%' OR pro.process_name LIKE '%FE_ETA_PROD%') AND pro.process_name LIKE '%{1}%'", _procContainer, instance);

			if (_debugMode.Equals("Y")) { PrintSql(str); }
			var conn = _datManager.GetProcmanConnection();
			SqlCommand cmd = new SqlCommand(str, conn);
			conn.Open();
			using (var reader = cmd.ExecuteReader())
			{
				var infoList = new List<ProcmanInfo>();
				while (reader.Read())
				{
					infoList.Add(
						 new ProcmanInfo()
						 {
							 ContainerName = reader["group_name"].ToString(),
							 HostName = reader["host_name"].ToString(),
							 ProcessName = reader["process_name"].ToString(),
							 Pid = reader["pid"].ToString()
						 }
					);
				}
				return infoList;
			}
		}

		private bool PreCheck(string process, string hostname)
		{
            try
            {
                string str = string.Format(@"        								
        								SELECT DISTINCT pah.host_name from [procman].[dbo].[process_audit_hist] pah WITH (NOLOCK) 
						                INNER JOIN [procman].[dbo].[process] p WITH (NOLOCK) on p.application_OID = pah.application_OID
						                WHERE pah.action = 'STOP' and p.process_name = '{0}' and p.host_name like '%{1}%' 
                                        AND pah.last_update_datetime > DATEADD(MINUTE,  (-{2}), getdate()) ", process, hostname, _monitorTime);

                if (_debugMode.Equals("Y")) { PrintSql(str); }
                var conn = _datManager.GetProcmanConnection();
                SqlCommand cmd = new SqlCommand(str, conn);
                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string val = reader["host_name"].ToString();
                        return string.IsNullOrEmpty(val) ? true : false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                log.Error("PreCheck fail. "+ e.Message);
                return  false;

            }
		}

		private void PrintSql(string str)
		{
			log.Info(str);
		}
		#endregion
	}
}
