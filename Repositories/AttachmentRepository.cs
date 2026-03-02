using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Repositories;

/// <summary>
/// 공통 첨부파일 Repository
/// school.db의 Attachment 테이블 관리
/// </summary>
public class AttachmentRepository : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public AttachmentRepository(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        EnsureTableExists();
    }

    #region Table Management

    private void EnsureTableExists()
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS Attachment (
                No INTEGER PRIMARY KEY AUTOINCREMENT,
                OwnerType TEXT NOT NULL,
                OwnerNo INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                OriginalFileName TEXT NOT NULL,
                FileSize INTEGER DEFAULT 0,
                FilePath TEXT NOT NULL,
                ContentType TEXT DEFAULT '',
                Description TEXT DEFAULT '',
                SortOrder INTEGER DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_attachment_owner 
                ON Attachment(OwnerType, OwnerNo);
        ";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.ExecuteNonQuery();
    }

    #endregion

    #region CRUD

    /// <summary>
    /// 첨부파일 추가
    /// </summary>
    public async Task<int> InsertAsync(Attachment attachment)
    {
        const string sql = @"
            INSERT INTO Attachment (
                OwnerType, OwnerNo, FileName, OriginalFileName, 
                FileSize, FilePath, ContentType, Description, SortOrder, CreatedAt
            ) VALUES (
                @OwnerType, @OwnerNo, @FileName, @OriginalFileName,
                @FileSize, @FilePath, @ContentType, @Description, @SortOrder, @CreatedAt
            );
            SELECT last_insert_rowid();
        ";

        using var cmd = new SqliteCommand(sql, _connection);
        AddParameters(cmd, attachment);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// 특정 소유자의 첨부파일 목록 조회
    /// </summary>
    public async Task<List<Attachment>> GetByOwnerAsync(string ownerType, int ownerNo)
    {
        const string sql = @"
            SELECT * FROM Attachment 
            WHERE OwnerType = @OwnerType AND OwnerNo = @OwnerNo
            ORDER BY SortOrder, CreatedAt
        ";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@OwnerType", ownerType);
        cmd.Parameters.AddWithValue("@OwnerNo", ownerNo);

        return await ExecuteQueryAsync(cmd);
    }

    /// <summary>
    /// 단일 첨부파일 조회
    /// </summary>
    public async Task<Attachment?> GetByIdAsync(int no)
    {
        const string sql = "SELECT * FROM Attachment WHERE No = @No";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@No", no);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToAttachment(reader);
        }
        return null;
    }

    /// <summary>
    /// 첨부파일 설명 수정
    /// </summary>
    public async Task<int> UpdateDescriptionAsync(int no, string description)
    {
        const string sql = "UPDATE Attachment SET Description = @Description WHERE No = @No";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@No", no);
        cmd.Parameters.AddWithValue("@Description", description ?? string.Empty);

        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 정렬 순서 수정
    /// </summary>
    public async Task<int> UpdateSortOrderAsync(int no, int sortOrder)
    {
        const string sql = "UPDATE Attachment SET SortOrder = @SortOrder WHERE No = @No";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@No", no);
        cmd.Parameters.AddWithValue("@SortOrder", sortOrder);

        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 첨부파일 삭제 (DB 레코드만, 파일 삭제는 Service에서)
    /// </summary>
    public async Task<int> DeleteAsync(int no)
    {
        const string sql = "DELETE FROM Attachment WHERE No = @No";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@No", no);

        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 특정 소유자의 첨부파일 전체 삭제
    /// </summary>
    public async Task<int> DeleteByOwnerAsync(string ownerType, int ownerNo)
    {
        const string sql = "DELETE FROM Attachment WHERE OwnerType = @OwnerType AND OwnerNo = @OwnerNo";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@OwnerType", ownerType);
        cmd.Parameters.AddWithValue("@OwnerNo", ownerNo);

        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 소유자별 첨부파일 수 조회
    /// </summary>
    public async Task<int> GetCountAsync(string ownerType, int ownerNo)
    {
        const string sql = "SELECT COUNT(*) FROM Attachment WHERE OwnerType = @OwnerType AND OwnerNo = @OwnerNo";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@OwnerType", ownerType);
        cmd.Parameters.AddWithValue("@OwnerNo", ownerNo);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    #endregion

    #region Helper Methods

    private void AddParameters(SqliteCommand cmd, Attachment a)
    {
        cmd.Parameters.AddWithValue("@OwnerType", a.OwnerType);
        cmd.Parameters.AddWithValue("@OwnerNo", a.OwnerNo);
        cmd.Parameters.AddWithValue("@FileName", a.FileName);
        cmd.Parameters.AddWithValue("@OriginalFileName", a.OriginalFileName);
        cmd.Parameters.AddWithValue("@FileSize", a.FileSize);
        cmd.Parameters.AddWithValue("@FilePath", a.FilePath);
        cmd.Parameters.AddWithValue("@ContentType", a.ContentType ?? string.Empty);
        cmd.Parameters.AddWithValue("@Description", a.Description ?? string.Empty);
        cmd.Parameters.AddWithValue("@SortOrder", a.SortOrder);
        cmd.Parameters.AddWithValue("@CreatedAt", a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private async Task<List<Attachment>> ExecuteQueryAsync(SqliteCommand cmd)
    {
        var list = new List<Attachment>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapToAttachment(reader));
        }
        return list;
    }

    private static Attachment MapToAttachment(SqliteDataReader reader)
    {
        return new Attachment
        {
            No = reader.GetInt32(reader.GetOrdinal("No")),
            OwnerType = reader.GetString(reader.GetOrdinal("OwnerType")),
            OwnerNo = reader.GetInt32(reader.GetOrdinal("OwnerNo")),
            FileName = reader.GetString(reader.GetOrdinal("FileName")),
            OriginalFileName = reader.GetString(reader.GetOrdinal("OriginalFileName")),
            FileSize = reader.GetInt64(reader.GetOrdinal("FileSize")),
            FilePath = reader.GetString(reader.GetOrdinal("FilePath")),
            ContentType = GetStringSafe(reader, "ContentType"),
            Description = GetStringSafe(reader, "Description"),
            SortOrder = reader.IsDBNull(reader.GetOrdinal("SortOrder")) ? 0 : reader.GetInt32(reader.GetOrdinal("SortOrder")),
            CreatedAt = DateTime.TryParse(reader.GetString(reader.GetOrdinal("CreatedAt")), out var dt) ? dt : DateTime.Now
        };
    }

    private static string GetStringSafe(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.Close();
            _connection?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}
