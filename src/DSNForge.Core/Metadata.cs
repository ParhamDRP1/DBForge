using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace DSNForge.Core
{
    public class DbMetadata
    {
        public int Version { get; set; } = 1;
        public string SourceTable
        {
            get; set;
        }
        public string Query
        {
            get; set;
        }
        public string CreateScript
        {
            get; set;
        }
        public string LoadCards
        {
            get; set;
        }
        public string UnloadCards
        {
            get; set;
        }
        public int CodePage { get; set; } = 65001;
        public string Delimiter { get; set; } = ",";
        public bool HasHeader
        {
            get; set;
        }
        public string NullToken { get; set; } = "";
        public bool Native
        {
            get; set;
        }
        public int NativeRecordBytes
        {
            get; set;
        }
        public bool NativeInputHasRdw
        {
            get; set;
        }
        public DatasetSettings Dataset
        {
            get; set;
        }
        public List<TextColumn> Columns { get; set; } = new List<TextColumn>();
        public List<string> Notes { get; set; } = new List<string>();
        public List<Dictionary<string, string>> CatalogColumns { get; set; } = new List<Dictionary<string, string>>();
        public List<Dictionary<string, string>> CatalogTable { get; set; } = new List<Dictionary<string, string>>();
        public List<Dictionary<string, string>> CatalogTablespace { get; set; } = new List<Dictionary<string, string>>();
        public static DbMetadata Load(string path)
        {
            var m = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<DbMetadata>(File.ReadAllText(path));
            if (m == null || m.Version != 1)
                throw new InvalidDataException("Unsupported dbmeta version");
            return m;
        }
        public void Apply(TransferStep s)
        {
            s.MetadataApplied = true;
            s.Columns = ConnectionProfile.JsonCopy(Columns);
            s.LocalCodePage = CodePage;
            s.Delimiter = Delimiter;
            s.HasHeader = HasHeader;
            s.CsvNullToken = NullToken;
            s.CreateScript = CreateScript;
            if (string.IsNullOrWhiteSpace(s.Table) || s.Table == "APP.TABLE_NAME")
                s.Table = SourceTable;
            if (Native || !string.IsNullOrWhiteSpace(LoadCards))
            {
                // Rebound when running, never execute the source table's cards unchanged.
                s.Input = LoadInput.GeneratedCards;
                s.BatchRecords = 0;
                s.MetadataLoadCards = LoadCards ?? "";
                s.NativeRecordBytes = NativeRecordBytes;
                s.NativeInputHasRdw = NativeInputHasRdw;
                if (Dataset != null)
                    s.Dataset = ConnectionProfile.JsonCopy(Dataset);
            }
        }
        public void ApplyDefaults(TransferStep s)
        {
            if (!s.UseMetadataLayout || s.MetadataApplied) return;
            string script = s.CreateScript;
            Apply(s);
            if (!string.IsNullOrWhiteSpace(script)) s.CreateScript = script;
        }
    }
    public static class MetadataCatalog
    {
        public static string Identifier(string value)
        {
            if (!Regex.IsMatch(value ?? "", @"^[A-Za-z][\w#$@]*\.[A-Za-z][\w#$@]*$"))
                throw new ArgumentException("Use SCHEMA.TABLE");
            return value.ToUpperInvariant();
        }
        public static List<string> Tables(Db2Settings s, string password)
        {
            var rows = Read(s, password, "SELECT CREATOR,NAME FROM SYSIBM.SYSTABLES WHERE TYPE='T' ORDER BY CREATOR,NAME WITH UR");
            return rows.Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0]).Trim() + "." + Convert.ToString(r[1]).Trim()).ToList();
        }
        public static DataTable Read(Db2Settings s, string password, string sql)
        {
            using (var c = ProviderFactory.Create(s, password))
            {
                c.Open();
                using (var q = c.CreateCommand())
                {
                    q.CommandText = sql;
                    q.CommandTimeout = s.ConnectTimeoutSeconds + 30;
                    using (var r = q.ExecuteReader())
                    {
                        var t = new DataTable();
                        for (int i = 0; i < r.FieldCount; i++)
                            t.Columns.Add(r.GetName(i), typeof(object));
                        while (r.Read())
                        {
                            var values = new object[r.FieldCount];
                            r.GetValues(values);
                            t.Rows.Add(values);
                        }
                        return t;
                    }
                }
            }
        }
        public static bool Exists(Db2Settings s, string password, string table)
        {
            var n = Identifier(table).Split('.');
            return Read(s, password, "SELECT NAME FROM SYSIBM.SYSTABLES WHERE CREATOR='" + n[0] + "' AND NAME='" + n[1] + "' WITH UR").Rows.Count > 0;
        }
        public static DbMetadata Describe(Db2Settings s, string password, string table)
        {
            table = Identifier(table);
            var n = table.Split('.');
            var m = new DbMetadata { SourceTable = table };
            var columns = Read(s, password, "SELECT NAME,COLTYPE,LENGTH,SCALE,NULLS FROM SYSIBM.SYSCOLUMNS WHERE TBCREATOR='" + n[0] + "' AND TBNAME='" + n[1] + "' ORDER BY COLNO WITH UR");
            m.CatalogColumns = Rows(Read(s, password, "SELECT * FROM SYSIBM.SYSCOLUMNS WHERE TBCREATOR='" + n[0] + "' AND TBNAME='" + n[1] + "' ORDER BY COLNO WITH UR"));
            m.CatalogTable = Rows(Read(s, password, "SELECT * FROM SYSIBM.SYSTABLES WHERE CREATOR='" + n[0] + "' AND NAME='" + n[1] + "' WITH UR"));
            if (columns.Rows.Count == 0)
                throw new InvalidDataException("Table not found: " + table);
            foreach (DataRow r in columns.Rows)
            {
                var type = Convert.ToString(r[1]).Trim();
                int length = Convert.ToInt32(r[2]);
                if (type == "DECIMAL")
                    type += "(" + length + "," + r[3] + ")";
                else if (new[] { "CHAR", "VARCHAR", "GRAPHIC", "VARGRAPHIC", "BINARY", "VARBINARY" }.Contains(type))
                    type += "(" + length + ")";
                else if (type == "INTEGER")
                    type = "INTEGER";
                else if (type == "TIMESTMP")
                    type = "TIMESTAMP";
                var raw = m.CatalogColumns[m.Columns.Count];
                string defaultKind = Get(raw, "DEFAULT").Trim(), defaultValue = Get(raw, "DEFAULTVALUE"), defaultSql = "";
                if (defaultKind == "1")
                    defaultSql = "DEFAULT '" + defaultValue.Replace("'", "''") + "'";
                else if (new[] { "2", "3", "4", "9" }.Contains(defaultKind))
                    defaultSql = "DEFAULT " + defaultValue;
                else if (defaultKind == "Y")
                    defaultSql = "WITH DEFAULT";
                else if (new[] { "A", "D", "I", "J" }.Contains(defaultKind))
                    m.Notes.Add("Review generated/identity definition for column " + r[0] + "; original catalog attributes are included in CatalogColumns.");
                m.Columns.Add(new TextColumn { Name = Convert.ToString(r[0]).Trim(), SqlType = type, DefaultSql = defaultSql, MaxUtf8Bytes = Math.Min(32704, Math.Max(32, length * 4)), Nullable = Convert.ToString(r[4]) == "Y" });
            }
            var ts = Read(s, password, "SELECT T.DBNAME,T.TSNAME,S.BPOOL,S.SEGSIZE,S.MAXPARTITIONS,S.DSSIZE FROM SYSIBM.SYSTABLES T JOIN SYSIBM.SYSTABLESPACE S ON T.DBNAME=S.DBNAME AND T.TSNAME=S.NAME WHERE T.CREATOR='" + n[0] + "' AND T.NAME='" + n[1] + "' WITH UR");
            string location = "";
            if (ts.Rows.Count > 0)
            {
                var r = ts.Rows[0];
                string db = Convert.ToString(r[0]).Trim(), space = Convert.ToString(r[1]).Trim();
                m.CatalogTablespace = Rows(Read(s, password, "SELECT * FROM SYSIBM.SYSTABLESPACE WHERE DBNAME='" + db + "' AND NAME='" + space + "' WITH UR"));
                m.CreateScript = "CREATE TABLESPACE " + space + " IN " + db + " BUFFERPOOL " + Convert.ToString(r[2]).Trim();
                if (Convert.ToInt32(r[3]) > 0)
                    m.CreateScript += " SEGSIZE " + r[3];
                if (Convert.ToInt32(r[4]) > 0)
                    m.CreateScript += " MAXPARTITIONS " + r[4];
                long kb = Convert.ToInt64(r[5]);
                if (kb >= 1048576 && kb % 1048576 == 0)
                    m.CreateScript += " DSSIZE " + (kb / 1048576) + "G";
                m.CreateScript += ";\r\n";
                location = " IN " + db + "." + space;
            }
            m.CreateScript += CreateTable(table, m.Columns, location);
            m.Notes.Add("Editable reconstruction: database must exist. Review tablespace allocation, partitioning, defaults, identity/generated columns, keys, indexes, LOB/XML auxiliary objects and grants before executing. Existing tablespaces are skipped by name.");
            return m;
        }
        static string Get(Dictionary<string, string> row, string key) => row.TryGetValue(key, out var value) ? value : "";
        static List<Dictionary<string, string>> Rows(DataTable table) => table.Rows.Cast<DataRow>().Select(row => table.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => row[c] is byte[] bytes ? Convert.ToBase64String(bytes) : Convert.ToString(row[c], System.Globalization.CultureInfo.InvariantCulture))).ToList();
        public static string CreateTable(string table, IEnumerable<TextColumn> columns, string location = "") => "CREATE TABLE " + Identifier(table) + " (\r\n" + string.Join(",\r\n", columns.Where(c => c.Included).Select(c => "  \"" + c.Name.Replace("\"", "\"\"") + "\" " + c.SqlType + (c.Nullable ? "" : " NOT NULL") + (string.IsNullOrEmpty(c.DefaultSql) ? "" : " " + c.DefaultSql))) + "\r\n)" + location + ";";
        public static string SelectQuery(string query)
        {
            query = (query ?? "").Trim().TrimEnd(';').Trim();
            // Restrict to a single read-only SELECT or CTE; reject data-change table references.
            string tokens = Regex.Replace(query, @"'([^']|'')*'|""([^""]|"""")*""", " ");
            if (!Regex.IsMatch(tokens, @"^(SELECT|WITH)\b", RegexOptions.IgnoreCase) || Regex.IsMatch(tokens, @";|--|/\*|\b(INSERT|UPDATE|DELETE|MERGE|CALL|INTO|CREATE|DROP|ALTER)\b", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Enter one read-only SELECT query, without comments or data-change statements.");
            return Regex.IsMatch(query, @"\bWITH\s+(UR|CS|RS|RR)\s*$", RegexOptions.IgnoreCase) ? query : query + " WITH UR";
        }
        public static List<TextColumn> QueryLayout(Db2Settings s, string password, string query)
        {
            using (var c = ProviderFactory.Create(s, password))
            {
                c.Open();
                using (var q = c.CreateCommand())
                {
                    q.CommandText = SelectQuery(query);
                    q.CommandTimeout = 30;
                    using (var r = q.ExecuteReader(CommandBehavior.SchemaOnly))
                        return ReaderLayout(r);
                }
            }
        }
        public static List<TextColumn> ReaderLayout(DbDataReader r)
        {
            var result = new List<TextColumn>();
            var schema = r.GetSchemaTable();
            for (int i = 0; i < r.FieldCount; i++)
            {
                int size = schema != null && schema.Rows[i]["ColumnSize"] != DBNull.Value ? Convert.ToInt32(schema.Rows[i]["ColumnSize"]) : 256;
                string type = r.GetDataTypeName(i).ToUpperInvariant();
                if (type == "CHAR" || type == "VARCHAR")
                    type += "(" + Math.Min(32672, Math.Max(1, size)) + ")";
                if (type == "DECIMAL" && schema != null)
                    type += "(" + schema.Rows[i]["NumericPrecision"] + "," + schema.Rows[i]["NumericScale"] + ")";
                result.Add(new TextColumn { Name = r.GetName(i), SqlType = type, MaxUtf8Bytes = (int)Math.Min(32704L, Math.Max(64L, (long)size * 4)) });
            }
            return result;
        }
        public static void CreateIfMissing(Db2Settings s, string password, TransferStep step, CancellationToken token)
        {
            string table = step.Operation == Operation.Transfer ? step.TargetTable : step.Table;
            if (!step.CreateTableIfMissing || Exists(s, password, table))
                return;
            if (string.IsNullOrWhiteSpace(step.CreateScript))
                throw new InvalidDataException("Create table script is required.");
            var statements = SplitStatements(step.CreateScript).ToList();
            int targetStatements = 0;
            foreach (string sql in statements)
            {
                if (!Regex.IsMatch(sql, @"^CREATE\s+(TABLESPACE|TABLE)\b", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("Creation script accepts only CREATE TABLESPACE and CREATE TABLE statements.");
                var ct = Regex.Match(sql, @"^CREATE\s+TABLE\s+([\w#$@]+\.[\w#$@]+)\s*\(", RegexOptions.IgnoreCase);
                if (Regex.IsMatch(sql, @"^CREATE\s+TABLE\b", RegexOptions.IgnoreCase))
                {
                    if (!ct.Success || !ct.Groups[1].Value.Equals(table, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Edit the dbmeta CREATE TABLE name to match the task target: " + table);
                    targetStatements++;
                }
            }
            if (targetStatements != 1)
                throw new InvalidDataException("Creation script must contain exactly one CREATE TABLE for the task target.");
            using (var c = ProviderFactory.Create(s, password))
            {
                c.Open();
                foreach (var sql in statements)
                {
                    if (!Regex.IsMatch(sql, @"^CREATE\s+(TABLESPACE|TABLE)\b", RegexOptions.IgnoreCase))
                        throw new InvalidDataException("Creation script accepts only CREATE TABLESPACE and CREATE TABLE statements.");
                    var ts = Regex.Match(sql, @"^CREATE\s+TABLESPACE\s+(\w+)\s+IN\s+(\w+)\b", RegexOptions.IgnoreCase);
                    if (ts.Success && Read(s, password, "SELECT NAME FROM SYSIBM.SYSTABLESPACE WHERE NAME='" + ts.Groups[1].Value.ToUpperInvariant() + "' AND DBNAME='" + ts.Groups[2].Value.ToUpperInvariant() + "' WITH UR").Rows.Count > 0)
                        continue;
                    using (var q = c.CreateCommand())
                    {
                        q.CommandText = sql;
                        q.CommandTimeout = 60;
                        using (token.Register(() => { try { q.Cancel(); } catch { } }))
                        {
                            token.ThrowIfCancellationRequested();
                            q.ExecuteNonQuery();
                        }
                    }
                }
            }
            if (!Exists(s, password, table))
                throw new InvalidDataException("Creation script did not create the task target table.");
        }
        public static IEnumerable<string> SplitStatements(string sql)
        {
            var b = new StringBuilder();
            char quote = '\0';
            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];
                if (quote != '\0')
                {
                    b.Append(c);
                    if (c == quote)
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == quote)
                            b.Append(sql[++i]);
                        else
                            quote = '\0';
                    }
                }
                else if (c == '\'' || c == '"')
                {
                    quote = c;
                    b.Append(c);
                }
                else if (c == ';')
                {
                    if (b.ToString().Trim().Length > 0)
                        yield return b.ToString().Trim();
                    b.Clear();
                }
                else
                    b.Append(c);
            }
            if (quote != '\0')
                throw new InvalidDataException("Unterminated quote in DDL");
            if (b.ToString().Trim().Length > 0)
                yield return b.ToString().Trim();
        }
    }
}
