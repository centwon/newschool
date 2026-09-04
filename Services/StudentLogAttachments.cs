using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using Windows.Storage;

namespace NewSchool.Services;

/// <summary>
/// 누가기록 첨부 — <b>실물 파일과 DB 행을 함께 맞추는 유일한 자리</b>.
///
/// <para>첨부는 "붙일 때"가 아니라 <b>저장을 누를 때</b> 한꺼번에 처리된다. 기록이 새것이면
/// 아직 <c>No</c> 가 없어서 어차피 저장 뒤에야 붙일 수 있고, 취소하고 창을 닫은 사용자의
/// 파일이 폴더에 남는 것도 막을 수 있다.</para>
///
/// <para><b>폴더 규칙:</b> <c>{데이터폴더}\StudentLogFiles\{학년도}\{학생ID}\{저장명}</c>.
/// 게시판처럼 분류(카테고리)로 나누지 않는다 — 분류는 바뀌고, 바뀌면 실물을 따라 옮겨야 하며,
/// 옮기지 못하면 <b>같은 이름의 남의 파일이 열리고 지워진다</b>. 학년도와 학생은 바뀌지 않으므로
/// 옮기는 코드 자체가 생기지 않는다. 근거는 <see cref="StudentLogFile"/> 머리 주석.</para>
/// </summary>
public static class StudentLogAttachments
{
    /// <summary>첨부 실물이 사는 뿌리 폴더.</summary>
    private static string RootDir => Path.Combine(Settings.UserDataPath, "StudentLogFiles");

    /// <summary>한 학생·한 학년도의 첨부가 모이는 폴더.</summary>
    public static string GetFolderPath(int year, string studentId) =>
        Path.Combine(RootDir, year.ToString(), SafeSegment(studentId));

    /// <summary>첨부 한 건의 절대 경로.</summary>
    public static string GetFilePath(StudentLogFile file) =>
        Path.Combine(GetFolderPath(file.Year, file.StudentID), file.FileName);

    /// <summary>
    /// 학생 ID 를 폴더 이름으로 쓸 수 있게 다듬는다.
    ///
    /// <para>학생 ID 는 앱이 만들지만, 다른 데서 들어온 값이 섞일 수 있다. 경로 구분자나
    /// <c>..</c> 가 들어오면 폴더를 빠져나가 <b>엉뚱한 곳에 쓰게 된다</b> — 첨부 저장은
    /// 파일을 만드는 일이므로 그 값을 그대로 믿지 않는다.</para>
    /// </summary>
    private static string SafeSegment(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "_";

        var cleaned = new string(id.Select(
            c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

        cleaned = cleaned.Trim().Trim('.');
        return cleaned.Length == 0 ? "_" : cleaned;
    }

    /// <summary>
    /// 원본 파일을 그 기록의 폴더로 복사하고 등록할 <see cref="StudentLogFile"/> 을 만든다.
    ///
    /// <para>⚠ 겹치는 이름을 <b>덮어쓰지 않는다</b>. 게시판이 여기서 데었다 — 새 첨부는
    /// 저장 버튼 한 번에 한 루프로 복사되므로 이름이 같으면 뒤엣것이 앞엣것을 조용히
    /// 덮어썼고, DB 에는 2건인데 실물은 1개인 상태가 됐다. 충돌 해소는 OS 에 맡기고
    /// (<see cref="NameCollisionOption.GenerateUniqueName"/>) <b>실제로 저장된 이름</b>을 넣는다.</para>
    /// </summary>
    /// <returns>실패하면 null. 사용자에게는 이미 알린 뒤다.</returns>
    public static async Task<StudentLogFile?> SaveFileAsync(
        string sourceFilePath, int logNo, int year, string studentId)
    {
        try
        {
            var folder = GetFolderPath(year, studentId);
            Directory.CreateDirectory(folder);

            var sourceFile = await StorageFile.GetFileFromPathAsync(sourceFilePath);
            var properties = await sourceFile.GetBasicPropertiesAsync();

            var destinationFolder = await StorageFolder.GetFolderFromPathAsync(folder);
            var savedFile = await sourceFile.CopyAsync(
                destinationFolder, sourceFile.Name, NameCollisionOption.GenerateUniqueName);

            return new StudentLogFile
            {
                LogNo = logNo,
                Year = year,
                StudentID = studentId,
                FileName = savedFile.Name,
                FileSize = (long)properties.Size,
                DateTime = DateTime.Now,
            };
        }
        catch (Exception ex)
        {
            // OneDrive 폴더에서 도는 설치가 있어 잠금으로 실패하는 일이 실제로 난다.
            // 조용히 넘기면 화면에는 첨부가 있는데 실물이 없는 기록이 된다.
            NewSchool.Logging.Log.Error("StudentLogAttachments", "첨부 파일을 저장하지 못했다", ex);
            await Controls.UserErrorReporter.ReportAsync("첨부파일 저장", ex);
            return null;
        }
    }

    /// <summary>
    /// 편집 창이 들고 있던 첨부 변경을 실제 파일과 DB 에 반영한다.
    ///
    /// <para>기록은 이미 저장된 뒤에 부른다 — 그래야 <paramref name="logNo"/> 가 있다.
    /// 순서는 <b>빼기 먼저, 붙이기 나중</b>이다. 반대로 하면 같은 이름을 뺐다가 다시 붙일 때
    /// 새로 복사한 것이 "겹치는 이름"이 되어 <c>(2)</c> 가 붙는다.</para>
    ///
    /// <para>실패는 삼키지 않는다. 조용히 넘기면 화면에는 첨부가 있는데 실물이 없는(또는
    /// 뺀 줄 알았는데 남은) 기록이 된다 — 게시판이 같은 이유로 실패 목록을 모아 알린다.</para>
    /// </summary>
    /// <returns>반영 뒤 이 기록에 남은 첨부 수</returns>
    public static async Task<int> ApplyAsync(
        StudentLogFileRepository repo,
        IReadOnlyList<StudentLogFile> toDelete,
        IReadOnlyList<string> newFilePaths,
        int logNo, int year, string studentId)
    {
        var failed = new List<string>();

        foreach (var file in toDelete)
        {
            if (!await DeleteAsync(repo, file))
                failed.Add($"{file.FileName} (빼기)");
        }

        foreach (var sourcePath in newFilePaths)
        {
            // 복사 실패는 SaveFileAsync 가 이미 알렸다 — 여기서는 집계에서만 빼면 된다.
            var saved = await SaveFileAsync(sourcePath, logNo, year, studentId);
            if (saved == null) continue;

            if (await repo.CreateAsync(saved) <= 0)
            {
                // 등록에 실패했으면 방금 복사한 실물은 아무도 가리키지 않는다 — 바로 치운다.
                TryDeleteFile(GetFilePath(saved));
                failed.Add($"{saved.FileName} (등록)");
            }
        }

        if (failed.Count > 0)
        {
            await Controls.MessageBox.ShowErrorAsync(
                $"첨부 {failed.Count}건을 반영하지 못했습니다.\n{string.Join("\n", failed)}\n\n" +
                "기록 자체는 저장됐습니다. 첨부는 다시 붙여 주세요.");
        }

        return (await repo.GetByLogAsync(logNo)).Count;
    }

    /// <summary>
    /// 첨부 한 건을 DB 와 실물에서 함께 지운다.
    ///
    /// <para><b>DB 를 먼저 지운다.</b> 실물만 남으면 "아무도 가리키지 않는 파일"이고
    /// <see cref="CleanupOrphanFilesAsync"/> 가 치울 수 있지만, 반대 순서로 하다 실패하면
    /// DB 는 있는데 실물이 없는 첨부가 되어 열 때마다 "파일이 없다"가 된다.</para>
    /// </summary>
    public static async Task<bool> DeleteAsync(StudentLogFileRepository repo, StudentLogFile file)
    {
        if (!await repo.DeleteAsync(file.No)) return false;

        TryDeleteFile(GetFilePath(file));
        return true;
    }

    /// <summary>
    /// 기록 하나가 지워질 때 그 첨부의 실물까지 치운다.
    ///
    /// <para>DB 행은 <c>ON DELETE CASCADE</c> 가 알아서 지우지만 <b>파일은 남는다</b>.
    /// 그래서 기록을 지우기 <b>전에</b> 무엇이 딸려 있었는지 읽어 두고, 지운 뒤에 치운다.</para>
    /// </summary>
    /// <returns>지워야 할 실물 목록(경로). 기록 삭제가 성공한 뒤 <see cref="DeleteFiles"/> 에 넘긴다.</returns>
    public static async Task<List<string>> CollectFilePathsAsync(
        StudentLogFileRepository repo, int logNo)
    {
        try
        {
            return (await repo.GetByLogAsync(logNo)).Select(GetFilePath).ToList();
        }
        catch (Exception ex)
        {
            // 목록을 못 읽어도 기록 삭제 자체를 막지는 않는다 — 남은 파일은 고아 정리가 치운다.
            NewSchool.Logging.Log.Warning("StudentLogAttachments", $"첨부 목록을 읽지 못해 파일이 남는다(고아 정리가 치운다): {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>모아 둔 경로를 지운다. 실패는 삼킨다 — 남아도 고아 정리가 치운다.</summary>
    public static void DeleteFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths) TryDeleteFile(path);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Warning("StudentLogAttachments", $"첨부 파일을 지우지 못했다({path}): {ex.Message}");
        }
    }

    /// <summary>
    /// 폴더에 남았지만 DB 가 가리키지 않는 파일을 치운다.
    ///
    /// <para>학생을 지우면 기록이 CASCADE 로 사라지고 첨부 행도 함께 사라지지만, 그 어느
    /// 단계도 <b>파일은 건드리지 않는다</b>. 여기가 마지막 그물이다.</para>
    ///
    /// <para>DB 가 아는 이름을 전부 모아 놓고, 폴더를 훑어 그 안에 없는 것만 지운다.
    /// 반대로(파일 하나마다 DB 를 물어보는 식으로) 하면 파일 수만큼 쿼리가 나간다.</para>
    /// </summary>
    /// <returns>지운 파일 수</returns>
    public static async Task<int> CleanupOrphanFilesAsync(string? dbPath = null)
    {
        if (!Directory.Exists(RootDir)) return 0;

        try
        {
            using var repo = new StudentLogFileRepository(dbPath ?? SchoolDatabase.DbPath);

            var known = new HashSet<string>(
                (await repo.GetAllAsync()).Select(GetFilePath),
                StringComparer.OrdinalIgnoreCase);

            int removed = 0;
            foreach (var path in Directory.EnumerateFiles(RootDir, "*", SearchOption.AllDirectories))
            {
                if (known.Contains(path)) continue;
                TryDeleteFile(path);
                removed++;
            }

            if (removed > 0)
                Debug.WriteLine($"[StudentLogAttachments] 고아 첨부 {removed}건 정리");

            return removed;
        }
        catch (Exception ex)
        {
            // 정리는 거들 뿐이다 — 실패해도 앱이 하려던 일을 막지 않는다.
            NewSchool.Logging.Log.Warning("StudentLogAttachments", $"고아 첨부 정리에 실패했다: {ex.Message}");
            return 0;
        }
    }
}
