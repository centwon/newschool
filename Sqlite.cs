using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace NewSchool
{
    /// <summary>
    /// 범용 SQLite 데이터베이스 관리 클래스
    /// 모든 DB 작업의 기반 클래스
    /// </summary>
    public class Sql : IDisposable
    {
        protected readonly string ConnectionString = string.Empty;
        protected readonly string DbPath = string.Empty;

        public Sql(string dbFile)
        {
            DbPath = dbFile;
            ConnectionString = $"Data Source={dbFile};Cache=Shared";
        }

        #region Connection Management

        protected SqliteConnection GetConnection()
        {
            var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        protected SqliteCommand CreateCommand(string sql, SqliteConnection? connection = null)
        {
            var conn = connection ?? GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd;
        }

        #endregion

        #region Database Initialization

        /// <summary>
        /// 데이터베이스 초기화
        /// </summary>
        public virtual bool Initialize()
        {
            try
            {
                using var connection = GetConnection();

                // WAL 모드 및 성능 설정
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        PRAGMA journal_mode=WAL;
                        PRAGMA synchronous=NORMAL;
                        PRAGMA busy_timeout=5000;
                        PRAGMA temp_store=MEMORY;
                        PRAGMA foreign_keys=ON;
                    ";
                    cmd.ExecuteNonQuery();
                }

                // 자식 클래스에서 테이블 생성
                CreateTables(connection);
                CreateIndexes(connection);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sql] 초기화 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 테이블 생성 (자식 클래스에서 구현)
        /// </summary>
        protected virtual void CreateTables(SqliteConnection connection) { }

        /// <summary>
        /// 인덱스 생성 (자식 클래스에서 구현)
        /// </summary>
        protected virtual void CreateIndexes(SqliteConnection connection) { }

        #endregion

        #region Generic CRUD Operations

        /// <summary>
        /// 제네릭 Insert
        /// </summary>
        public int Insert(string tableName, Dictionary<string, object?> columns)
        {
            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();

            var columnNames = string.Join(", ", columns.Keys);
            var parameters = string.Join(", ", columns.Keys.Select((_, i) => $"@p{i}"));

            cmd.CommandText = $"INSERT INTO {tableName} ({columnNames}) VALUES ({parameters}); SELECT last_insert_rowid();";

            int i = 0;
            foreach (var value in columns.Values)
            {
                cmd.Parameters.AddWithValue($"@p{i}", value ?? DBNull.Value);
                i++;
            }

            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : -1;
        }

        /// <summary>
        /// 제네릭 Update
        /// </summary>
        public int Update(string tableName, Dictionary<string, object?> columns, string whereClause, params (string name, object value)[] parameters)
        {
            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();

            var setClauses = string.Join(", ", columns.Select((kvp, i) => $"{kvp.Key} = @p{i}"));
            cmd.CommandText = $"UPDATE {tableName} SET {setClauses} WHERE {whereClause}";

            int i = 0;
            foreach (var value in columns.Values)
            {
                cmd.Parameters.AddWithValue($"@p{i}", value ?? DBNull.Value);
                i++;
            }

            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            return cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 제네릭 Delete
        /// </summary>
        public int Delete(string tableName, string whereClause, params (string name, object value)[] parameters)
        {
            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();

            cmd.CommandText = $"DELETE FROM {tableName} WHERE {whereClause}";

            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            return cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 제네릭 Select (단일 레코드)
        /// </summary>
        public T? SelectOne<T>(string tableName, Func<SqliteDataReader, T> mapper, string whereClause, params (string name, object value)[] parameters)
        {
            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();

            cmd.CommandText = $"SELECT * FROM {tableName} WHERE {whereClause}";

            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return mapper(reader);
            }

            return default;
        }

        /// <summary>
        /// 제네릭 Select (목록)
        /// </summary>
        public List<T> SelectList<T>(string tableName, Func<SqliteDataReader, T> mapper, string? whereClause = null, string? orderBy = null, params (string name, object value)[] parameters)
        {
            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();

            var where = string.IsNullOrWhiteSpace(whereClause) ? "" : $"WHERE {whereClause}";
            var order = string.IsNullOrWhiteSpace(orderBy) ? "" : $"ORDER BY {orderBy}";
            cmd.CommandText = $"SELECT * FROM {tableName} {where} {order}";

            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            var list = new List<T>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(mapper(reader));
            }

            return list;
        }

        /// <summary>
        /// 제네릭 ExecuteScalar
        /// </summary>
        public object? ExecuteScalar(string sql, params (string name, object value)[] parameters)
        {
            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();

            cmd.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            return cmd.ExecuteScalar();
        }

        /// <summary>
        /// 제네릭 ExecuteNonQuery
        /// </summary>
        public int ExecuteNonQuery(string sql, params (string name, object value)[] parameters)
        {
            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();

            cmd.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            return cmd.ExecuteNonQuery();
        }

        #endregion

        #region Table Management

        public static List<string> GetTables(string dbFile)
        {
            using var db = new Sql(dbFile);
            return db.GetTables();
        }

        public List<string> GetTables()
        {
            string getlist = "SELECT name FROM sqlite_master WHERE type IN('table', 'view') AND name NOT LIKE 'sqlite_%' UNION ALL SELECT name FROM sqlite_temp_master WHERE type IN('table', 'view') ORDER BY 1";

            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = getlist;
            using var reader = cmd.ExecuteReader();

            var tables = new List<string>();
            while (reader.Read())
            {
                tables.Add(reader["name"].ToString() ?? string.Empty);
            }

            return tables;
        }

        /// <summary>
        /// 테이블 존재 확인
        /// </summary>
        public bool TableExists(string tableName)
        {
            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@table";
            cmd.Parameters.AddWithValue("@table", tableName);
            using var reader = cmd.ExecuteReader();
            return reader.Read();
        }

        #endregion

        #region Backup & Restore

        /// <summary>
        /// 데이터베이스 백업
        /// </summary>
        public static string BackupDB()
        {
            string str;
            string DBFileName = "data.db";
            string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory + @"Data";
            string DBFile = $@"{BaseDirectory}\{DBFileName}";

            string timestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss", System.Globalization.CultureInfo.InvariantCulture);
            string NewDBFileName = $"data_bak_{timestamp}.db";

            System.IO.File.Copy(DBFile, $@"{BaseDirectory}\{NewDBFileName}", true);
            str = $"'{NewDBFileName}'로 저장됨!";
            return str;
        }

        /// <summary>
        /// 인스턴스 백업
        /// </summary>
        public string? Backup()
        {
            try
            {
                if (!System.IO.File.Exists(DbPath)) return null;

                string timestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss",
                    System.Globalization.CultureInfo.InvariantCulture);
                string backupFileName = $"{System.IO.Path.GetFileNameWithoutExtension(DbPath)}_backup_{timestamp}.db";
                string backupPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(DbPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    backupFileName
                );

                System.IO.File.Copy(DbPath, backupPath, true);
                System.Diagnostics.Debug.WriteLine($"[Sql] 백업 완료: {backupPath}");
                return backupPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sql] 백업 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 데이터베이스 복원
        /// </summary>
        public bool Restore(string backupPath)
        {
            try
            {
                if (!System.IO.File.Exists(backupPath)) return false;

                System.IO.File.Copy(backupPath, DbPath, true);
                System.Diagnostics.Debug.WriteLine($"[Sql] 복원 완료: {backupPath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Sql] 복원 실패: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// VACUUM (데이터베이스 최적화)
        /// </summary>
        public void Vacuum()
        {
            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "VACUUM";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// ANALYZE (통계 업데이트)
        /// </summary>
        public void Analyze()
        {
            using var connection = GetConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "ANALYZE";
            cmd.ExecuteNonQuery();
        }

        #endregion

        #region DateTime 변환 - DateTimeHelper 사용으로 통일

        /// <summary>
        /// DateTime을 문자열로 변환 (DateTimeHelper 사용)
        /// 이전 버전과의 호환성을 위해 메서드 유지
        /// </summary>
        public static string ConvertDatetime(DateTime datetime)
        {
            return DateTimeHelper.ToStandardString(datetime);
        }

        /// <summary>
        /// DateTimeOffset을 문자열로 변환 (DateTimeHelper 사용)
        /// </summary>
        public static string ConvertDatetime(DateTimeOffset datetime)
        {
            return DateTimeHelper.ToStandardString(datetime);
        }

        #endregion

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
