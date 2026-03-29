using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 공통 첨부파일 서비스
/// 파일 복사/삭제 + DB 레코드 관리를 통합 처리
/// 저장 경로: Data/Attachments/{OwnerType}/{OwnerNo}/
/// </summary>
public class AttachmentService : IDisposable
{
    private readonly AttachmentRepository _repository;
    private bool _disposed;

    /// <summary>첨부파일 루트 디렉토리</summary>
    private static string AttachmentsRoot =>
        Path.Combine(Settings.UserDataPath, "Attachments");

    public AttachmentService()
    {
        _repository = new AttachmentRepository(SchoolDatabase.DbPath);
    }

    #region 파일 추가

    /// <summary>
    /// 파일을 첨부 (소스 파일을 저장소로 복사 + DB 등록)
    /// </summary>
    /// <param name="ownerType">소유자 유형 ("LessonLog", "ClassDiary", "CourseSection")</param>
    /// <param name="ownerNo">소유자 레코드 No</param>
    /// <param name="sourceFilePath">원본 파일 경로</param>
    /// <param name="description">설명 (선택)</param>
    /// <returns>생성된 Attachment (No 포함)</returns>
    public async Task<Attachment> AddFileAsync(string ownerType, int ownerNo, string sourceFilePath, string description = "")
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("첨부할 파일을 찾을 수 없습니다.", sourceFilePath);

        var fileInfo = new FileInfo(sourceFilePath);
        var originalFileName = fileInfo.Name;

        // 저장 디렉토리 생성
        var destDir = GetOwnerDirectory(ownerType, ownerNo);
        Directory.CreateDirectory(destDir);

        // 중복 방지 파일명 생성
        var storedFileName = GenerateUniqueFileName(originalFileName);
        var destPath = Path.Combine(destDir, storedFileName);

        // 파일 복사
        File.Copy(sourceFilePath, destPath, overwrite: false);

        // DB 등록
        var attachment = new Attachment
        {
            OwnerType = ownerType,
            OwnerNo = ownerNo,
            FileName = storedFileName,
            OriginalFileName = originalFileName,
            FileSize = fileInfo.Length,
            FilePath = destPath,
            ContentType = GetContentType(originalFileName),
            Description = description,
            SortOrder = await _repository.GetCountAsync(ownerType, ownerNo),
            CreatedAt = DateTime.Now
        };

        attachment.No = await _repository.InsertAsync(attachment);

        Debug.WriteLine($"[AttachmentService] 첨부 완료: {originalFileName} → {ownerType}:{ownerNo}");
        return attachment;
    }

    /// <summary>
    /// 여러 파일을 한번에 첨부
    /// </summary>
    public async Task<List<Attachment>> AddFilesAsync(string ownerType, int ownerNo, IEnumerable<string> sourceFilePaths)
    {
        var results = new List<Attachment>();
        foreach (var path in sourceFilePaths)
        {
            try
            {
                var attachment = await AddFileAsync(ownerType, ownerNo, path);
                results.Add(attachment);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AttachmentService] 첨부 실패: {path} - {ex.Message}");
            }
        }
        return results;
    }

    #endregion

    #region 조회

    /// <summary>
    /// 특정 레코드의 첨부파일 목록
    /// </summary>
    public async Task<List<Attachment>> GetByOwnerAsync(string ownerType, int ownerNo)
    {
        return await _repository.GetByOwnerAsync(ownerType, ownerNo);
    }

    /// <summary>
    /// 단일 첨부파일 조회
    /// </summary>
    public async Task<Attachment?> GetByIdAsync(int no)
    {
        return await _repository.GetByIdAsync(no);
    }

    /// <summary>
    /// 첨부파일 수
    /// </summary>
    public async Task<int> GetCountAsync(string ownerType, int ownerNo)
    {
        return await _repository.GetCountAsync(ownerType, ownerNo);
    }

    #endregion

    #region 수정

    /// <summary>
    /// 설명 수정
    /// </summary>
    public async Task UpdateDescriptionAsync(int no, string description)
    {
        await _repository.UpdateDescriptionAsync(no, description);
    }

    /// <summary>
    /// 정렬 순서 수정
    /// </summary>
    public async Task UpdateSortOrderAsync(int no, int sortOrder)
    {
        await _repository.UpdateSortOrderAsync(no, sortOrder);
    }

    #endregion

    #region 삭제

    /// <summary>
    /// 첨부파일 삭제 (DB + 물리 파일)
    /// </summary>
    public async Task<bool> DeleteAsync(int no)
    {
        var attachment = await _repository.GetByIdAsync(no);
        if (attachment == null) return false;

        // 물리 파일 삭제
        DeletePhysicalFile(attachment.FilePath);

        // DB 삭제
        await _repository.DeleteAsync(no);

        Debug.WriteLine($"[AttachmentService] 삭제 완료: {attachment.OriginalFileName}");
        return true;
    }

    /// <summary>
    /// 특정 소유자의 첨부파일 전체 삭제 (DB + 물리 파일 + 디렉토리)
    /// </summary>
    public async Task<int> DeleteByOwnerAsync(string ownerType, int ownerNo)
    {
        // 물리 파일 먼저 삭제
        var attachments = await _repository.GetByOwnerAsync(ownerType, ownerNo);
        foreach (var a in attachments)
        {
            DeletePhysicalFile(a.FilePath);
        }

        // 디렉토리 정리
        var dir = GetOwnerDirectory(ownerType, ownerNo);
        DeleteDirectoryIfEmpty(dir);

        // DB 삭제
        var count = await _repository.DeleteByOwnerAsync(ownerType, ownerNo);

        Debug.WriteLine($"[AttachmentService] 전체 삭제: {ownerType}:{ownerNo} - {count}건");
        return count;
    }

    #endregion

    #region 파일 열기

    /// <summary>
    /// 첨부파일을 기본 프로그램으로 열기
    /// </summary>
    public async Task<bool> OpenFileAsync(int no)
    {
        var attachment = await _repository.GetByIdAsync(no);
        if (attachment == null || !File.Exists(attachment.FilePath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = attachment.FilePath,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttachmentService] 파일 열기 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 첨부파일이 저장된 폴더 열기
    /// </summary>
    public async Task<bool> OpenFolderAsync(string ownerType, int ownerNo)
    {
        var dir = GetOwnerDirectory(ownerType, ownerNo);
        if (!Directory.Exists(dir)) return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttachmentService] 폴더 열기 실패: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 소유자별 저장 디렉토리 경로
    /// </summary>
    private static string GetOwnerDirectory(string ownerType, int ownerNo)
    {
        return Path.Combine(AttachmentsRoot, ownerType, ownerNo.ToString());
    }

    /// <summary>
    /// 중복 방지 파일명 생성 (타임스탬프 + 원본명)
    /// </summary>
    private static string GenerateUniqueFileName(string originalFileName)
    {
        var name = Path.GetFileNameWithoutExtension(originalFileName);
        var ext = Path.GetExtension(originalFileName);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"{timestamp}_{name}{ext}";
    }

    /// <summary>
    /// 확장자 기반 MIME 타입 추정
    /// </summary>
    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".hwp" => "application/x-hwp",
            ".hwpx" => "application/haansofthwpx",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".zip" => "application/zip",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// 물리 파일 안전 삭제
    /// </summary>
    private static void DeletePhysicalFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttachmentService] 파일 삭제 실패: {filePath} - {ex.Message}");
        }
    }

    /// <summary>
    /// 빈 디렉토리 삭제
    /// </summary>
    private static void DeleteDirectoryIfEmpty(string dirPath)
    {
        try
        {
            if (Directory.Exists(dirPath) && Directory.GetFiles(dirPath).Length == 0
                && Directory.GetDirectories(dirPath).Length == 0)
            {
                Directory.Delete(dirPath);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AttachmentService] 디렉토리 삭제 실패: {dirPath} - {ex.Message}");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (!_disposed)
        {
            _repository?.Dispose();
            _disposed = true;
        }
    }

    #endregion
}
