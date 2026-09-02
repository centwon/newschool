using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// 누가기록 첨부파일 데이터 접근.
///
/// <para>실물 파일은 손대지 않는다 — 여기는 DB 행만 다룬다. 파일과 DB 를 함께 맞추는 일은
/// <see cref="Services.StudentLogAttachments"/> 한 곳에 모여 있다.</para>
/// </summary>
public class StudentLogFileRepository : BaseRepository
{
    public StudentLogFileRepository(string dbPath) : base(dbPath) { }

    #region Create

    /// <summary>첨부 한 건을 등록하고 새 No 를 돌려준다.</summary>
    public async Task<int> CreateAsync(StudentLogFile file)
    {
        const string query = @"
                INSERT INTO StudentLogFile (LogNo, Year, StudentID, FileName, FileSize, DateTime)
                VALUES (@LogNo, @Year, @StudentID, @FileName, @FileSize, @DateTime);
                SELECT last_insert_rowid();";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@LogNo", SqliteType.Integer).Value = file.LogNo;
            cmd.Parameters.Add("@Year", SqliteType.Integer).Value = file.Year;
            cmd.Parameters.AddWithValue("@StudentID", file.StudentID ?? string.Empty);
            cmd.Parameters.AddWithValue("@FileName", file.FileName ?? string.Empty);
            cmd.Parameters.Add("@FileSize", SqliteType.Integer).Value = file.FileSize;
            cmd.Parameters.AddWithValue("@DateTime", file.DateTime.ToString("yyyy-MM-dd HH:mm:ss"));

            file.No = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            LogInfo($"누가기록 첨부 등록: No={file.No}, LogNo={file.LogNo}, {file.FileName}");
            return file.No;
        }
        catch (Exception ex)
        {
            LogError($"누가기록 첨부 등록 실패: LogNo={file.LogNo}, {file.FileName}", ex);
            throw;
        }
    }

    #endregion

    #region Read

    /// <summary>기록 하나에 딸린 첨부 전부. 붙인 순서대로 준다.</summary>
    public async Task<List<StudentLogFile>> GetByLogAsync(int logNo)
    {
        const string query = @"
                SELECT * FROM StudentLogFile
                WHERE LogNo = @LogNo
                ORDER BY No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@LogNo", SqliteType.Integer).Value = logNo;

            return await ExecuteListAsync(cmd, Map).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError($"누가기록 첨부 조회 실패: LogNo={logNo}", ex);
            throw;
        }
    }

    /// <summary>
    /// 여러 기록의 첨부를 단일 쿼리로 일괄 조회 (N+1 해소).
    /// 목록 화면이 행마다 📎 를 그릴 때 쓴다.
    /// </summary>
    public async Task<Dictionary<int, List<StudentLogFile>>> GetByLogsAsync(IEnumerable<int> logNos)
    {
        var idList = logNos?.Where(n => n > 0).Distinct().ToList() ?? new List<int>();
        var result = new Dictionary<int, List<StudentLogFile>>();
        if (idList.Count == 0) return result;

        var placeholders = string.Join(",", idList.Select((_, i) => $"@id{i}"));
        string query = $@"
                SELECT * FROM StudentLogFile
                WHERE LogNo IN ({placeholders})
                ORDER BY LogNo, No";

        try
        {
            using var cmd = CreateCommand(query);
            for (int i = 0; i < idList.Count; i++)
                cmd.Parameters.Add($"@id{i}", SqliteType.Integer).Value = idList[i];

            // 요청한 모든 기록에 대해 키가 있도록 먼저 채운다 — 부르는 쪽이
            // "없으면 빈 목록"을 따로 다루지 않아도 되게.
            foreach (var id in idList) result[id] = new List<StudentLogFile>();

            foreach (var file in await ExecuteListAsync(cmd, Map).ConfigureAwait(false))
            {
                if (!result.TryGetValue(file.LogNo, out var list))
                {
                    list = new List<StudentLogFile>();
                    result[file.LogNo] = list;
                }
                list.Add(file);
            }

            return result;
        }
        catch (Exception ex)
        {
            LogError($"누가기록 첨부 일괄 조회 실패: Count={idList.Count}", ex);
            throw;
        }
    }

    /// <summary>
    /// 저장된 첨부 전부. 폴더에 남은 고아 파일을 가려내려면 "DB 가 아는 이름"의
    /// 전체 목록이 필요하다(<see cref="Services.StudentLogAttachments"/>).
    /// </summary>
    public async Task<List<StudentLogFile>> GetAllAsync()
    {
        const string query = "SELECT * FROM StudentLogFile";

        try
        {
            using var cmd = CreateCommand(query);
            return await ExecuteListAsync(cmd, Map).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogError("누가기록 첨부 전체 조회 실패", ex);
            throw;
        }
    }

    #endregion

    #region Delete

    /// <summary>첨부 한 건의 DB 행을 지운다. 실물은 부르는 쪽이 치운다.</summary>
    public async Task<bool> DeleteAsync(int no)
    {
        const string query = "DELETE FROM StudentLogFile WHERE No = @No";

        try
        {
            using var cmd = CreateCommand(query);
            cmd.Parameters.Add("@No", SqliteType.Integer).Value = no;

            bool ok = await cmd.ExecuteNonQueryAsync() > 0;
            if (!ok) LogWarning($"누가기록 첨부 삭제 실패: No={no}");
            return ok;
        }
        catch (Exception ex)
        {
            LogError($"누가기록 첨부 삭제 실패: No={no}", ex);
            throw;
        }
    }

    #endregion

    #region Helper

    private StudentLogFile Map(SqliteDataReader reader, ReaderColumnCache cache)
    {
        var sizeIdx = cache.GetOrdinal("FileSize");
        var dateIdx = cache.GetOrdinal("DateTime");

        return new StudentLogFile
        {
            No = reader.GetInt32(cache.GetOrdinal("No")),
            LogNo = reader.GetInt32(cache.GetOrdinal("LogNo")),
            Year = reader.GetInt32(cache.GetOrdinal("Year")),
            StudentID = reader.GetString(cache.GetOrdinal("StudentID")),
            FileName = reader.GetString(cache.GetOrdinal("FileName")),
            // FileSize·DateTime 은 DEFAULT 만 있고 NOT NULL 이 아니다 — 맨몸으로 읽으면
            // NULL 인 행에서 InvalidCastException 이 난다(학생부 매퍼가 세 번 데인 자리다).
            FileSize = reader.IsDBNull(sizeIdx) ? 0 : reader.GetInt64(sizeIdx),
            DateTime = ParseStamp(reader.IsDBNull(dateIdx) ? null : reader.GetString(dateIdx)),
        };
    }

    /// <summary>
    /// 저장한 그대로 "yyyy-MM-dd HH:mm:ss" 를 되읽는다.
    ///
    /// <para><c>DateTimeHelper.FromDateString</c> 을 쓰지 않는 이유: 그쪽은 <b>날짜만</b> 있는
    /// 형식들만 본다. 시각까지 넣어 두고 그것으로 읽으면 조용히 <c>MinValue</c> 가 되어,
    /// 목록의 붙인 시각이 1년 1월 1일로 찍힌다.</para>
    /// </summary>
    private static DateTime ParseStamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;

        return DateTime.TryParseExact(
                   value, "yyyy-MM-dd HH:mm:ss",
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out var stamp)
            ? stamp
            : (DateTime.TryParse(value, out var loose) ? loose : default);
    }

    #endregion
}
