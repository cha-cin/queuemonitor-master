using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Micron.Data;

namespace QueueMonitor.Interface
{
    interface IDATManager
    {
        SqlConnection GetProcmanConnection();

        SqlConnection GetMetabaservConnection();

        Credential GetDATCredential(string inSystem, string inVersion, string inSiteName, string inService);
    }
}
