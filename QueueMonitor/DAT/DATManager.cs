using Micron.Data;
using QueueMonitor.Interface;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QueueMonitor.DAT
{
    public class DATManager : IDATManager
    {
        public SqlConnection GetProcmanConnection()
        {
            //BOMSSPROD120; PROCMAN_APP; Procman@123
            Micron.Data.Credential credential = GetDATCredential("ProcmanBASERV", "1.0", "BOISE", "ProcmanDB");
            if (credential != null)
            {
                SqlConnection conn = new SqlConnection();
                conn.ConnectionString = "Data Source=" + credential.Domain + ";Initial Catalog=" + credential.SubDomain + ";User id=" + credential.UserID + ";Password=" + credential.Password + ";";
                return conn;
            }
            return null;
        }

        public SqlConnection GetMetabaservConnection()
        {
            //BOMSSPROD122; METABASERV; b2llyb1b
            SqlConnection conn = new SqlConnection();
            conn.ConnectionString = "Data Source=BOMSSPROD122;User id=METABASERV; Password=b2llyb1b";
            return conn;
        }

        public Credential GetDATCredential(string inSystem, string inVersion, string inSiteName, string inService)
        {
            Micron.Application.Context context = new Micron.Application.Context(inSystem, inVersion, Micron.Application.Context.Environments.Production, inSiteName);
            Micron.Data.Credential credential = new Micron.Data.Credential(context, inService);
            return credential;
        }
    }
}
