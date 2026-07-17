using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using NewSchool.Logging;

namespace NewSchool.Helpers
{
    /// <summary>
    /// 시작 시 SQLite 파일 무결성 점검. 손상('malformed')을 조용한 크래시 대신
    /// 복원 안내로 전환하기 위한 헬퍼. (SaemDesk 동일 구현 이식)
    /// </summary>
    public static class DbIntegrity
    {
        // SQLite primary result codes — 진짜 파일 손상만 이 둘로 판정한다
        private const int SQLITE_CORRUPT = 11; // malformed
        private const int SQLITE_NOTADB = 26;  // SQLite 형식이 아닌 파일

        /// <summary>
        /// 주어진 DB 경로들에 PRAGMA quick_check 를 실행하고 손상된 파일명 목록을 반환한다.
        /// 존재하지 않는 파일은 건너뛴다(신규 설치).
        /// 손상(CORRUPT·NOTADB·quick_check 비정상)만 보고하고, 잠금·권한·백신 스캔 등
        /// 일시 오류는 손상이 아니므로 로그만 남기고 통과시킨다 — 오탐으로 시작이
        /// 막히는 것보다 다음 실행에서 재점검되는 편이 안전하다.
        /// </summary>
        public static List<string> FindCorrupt(IEnumerable<string> dbPaths)
        {
            var corrupt = new List<string>();
            foreach (var path in dbPaths)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                try
                {
                    var cs = new SqliteConnectionStringBuilder
                    {
                        DataSource = path,
                        Mode = SqliteOpenMode.ReadWrite,
                        // 점검용 연결이 풀에 남아 파일 핸들을 쥐면 이후 백업/복원의
                        // 파일 교체를 방해할 수 있으므로 풀링 없이 즉시 닫는다
                        Pooling = false,
                    }.ToString();

                    using var con = new SqliteConnection(cs);
                    con.Open();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "PRAGMA quick_check;";
                    var result = cmd.ExecuteScalar() as string;
                    if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                        corrupt.Add(Path.GetFileName(path));
                }
                catch (SqliteException ex) when (
                    ex.SqliteErrorCode is SQLITE_CORRUPT or SQLITE_NOTADB ||
                    (ex.SqliteExtendedErrorCode & 0xFF) is SQLITE_CORRUPT or SQLITE_NOTADB)
                {
                    corrupt.Add(Path.GetFileName(path));
                }
                catch (Exception ex)
                {
                    // 잠금(BUSY)·읽기 전용·권한 부족 등 — 손상 판정 보류
                    FileLogger.Instance.Warning(
                        $"[DbIntegrity] {Path.GetFileName(path)} 점검 실패(손상 아님, 통과): {ex.Message}");
                }
            }
            return corrupt;
        }
    }
}
