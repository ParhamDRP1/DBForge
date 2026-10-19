using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace DSNForge.Core
{
    public static class Catalog
    {
        public static List<TextColumn> ReadTextLayout(Db2Settings settings, string password, string table)
        {
            if (!Regex.IsMatch(table ?? "", @"^[A-Za-z][\w#$@]*\.[A-Za-z][\w#$@]*$"))
                throw new ArgumentException("Use SCHEMA.TABLE");
            string[] names = table.ToUpperInvariant().Split('.');
            var result = new List<TextColumn>();
            using (var c = Db2UtilityExecutor.CreateConnection(settings, password))
            {
                c.Open();
                using (var q = c.CreateCommand())
                {
                    q.CommandText = "SELECT NAME,COLTYPE,LENGTH FROM SYSIBM.SYSCOLUMNS WHERE TBCREATOR='" + names[0] + "' AND TBNAME='" + names[1] + "' ORDER BY COLNO WITH UR";
                    q.CommandTimeout = 30;
                    using (var r = q.ExecuteReader())
                        while (r.Read())
                        {
                            string type = Convert.ToString(r[1]).Trim();
                            if (type != "CHAR" && type != "VARCHAR")
                                throw new InvalidOperationException("Column " + r[0] + " is " + type + "; use native/generated LOAD cards for typed data.");
                            result.Add(new TextColumn { Name = Convert.ToString(r[0]).Trim(), MaxUtf8Bytes = Convert.ToInt32(r[2]) });
                        }
                }
            }
            if (result.Count == 0)
                throw new InvalidOperationException("Table not found or no visible columns");
            return result;
        }
    }
}
