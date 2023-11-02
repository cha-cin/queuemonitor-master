using QueueMonitor.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor.FileHandler
{
    public class FileManager : IFileManager
    {
        private string defLogFile = "Log.log";
        public void LogFile(string text)
        {
            if (!File.Exists(defLogFile))
            {
                File.CreateText(defLogFile);
            }

            using (var outputFile = new StreamWriter(defLogFile))
            {
                outputFile.Write(DateTime.Now + " : ");
                outputFile.WriteLine(text);
            }
        }
    }
}
