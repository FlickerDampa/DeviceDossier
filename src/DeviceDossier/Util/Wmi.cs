using System;
using System.Collections.Generic;
using System.Management;

namespace DeviceDossier.Util
{
    public static class Wmi
    {
        public const string Cimv2 = @"root\cimv2";

        public static List<ManagementObject> Query(string wql)
        {
            return Query(Cimv2, wql);
        }

        public static List<ManagementObject> Query(string scope, string wql)
        {
            var list = new List<ManagementObject>();
            using (var searcher = new ManagementObjectSearcher(scope, wql))
            {
                searcher.Options.Timeout = TimeSpan.FromSeconds(60);
                foreach (ManagementBaseObject mo in searcher.Get())
                {
                    var obj = mo as ManagementObject;
                    if (obj != null) list.Add(obj);
                }
            }
            return list;
        }

        public static List<ManagementObject> TryQuery(string scope, string wql)
        {
            try { return Query(scope, wql); }
            catch { return new List<ManagementObject>(); }
        }

        public static List<ManagementObject> TryQuery(string wql)
        {
            return TryQuery(Cimv2, wql);
        }

        public static ManagementObject First(string wql)
        {
            var list = TryQuery(wql);
            return list.Count > 0 ? list[0] : null;
        }

        public static object Get(ManagementBaseObject mo, string property)
        {
            if (mo == null) return null;
            try { return mo[property]; }
            catch { return null; }
        }

        public static string Str(ManagementBaseObject mo, string property)
        {
            return Core.Fmt.Str(Get(mo, property));
        }

        public static long Long(ManagementBaseObject mo, string property)
        {
            var v = Get(mo, property);
            if (v == null) return 0;
            try { return Convert.ToInt64(v); }
            catch { return 0; }
        }

        public static bool IsAccessDenied(Exception ex)
        {
            var me = ex as ManagementException;
            if (me != null && me.ErrorCode == ManagementStatus.AccessDenied) return true;
            return ex is UnauthorizedAccessException;
        }
    }
}
