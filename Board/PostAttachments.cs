using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NewSchool.Board.Controls;
using NewSchool.Board.Services;
using Windows.Storage;

namespace NewSchool.Board;

/// <summary>
/// 글 첨부 저장 — 편집 창이 들고 있던 첨부 변경을 실제 파일과 DB 에 반영한다.
///
/// 첨부는 "붙일 때"가 아니라 <b>저장을 누를 때</b> 한꺼번에 처리된다. 편집 화면이
/// 셋(게시글 편집 페이지 · 메모 창 · 수업 일지 창)이라 같은 코드가 흩어지기 쉬워 한자리에 모은다.
/// </summary>
internal static class PostAttachments
{
    /// <summary>
    /// 첨부 한 건을 다른 카테고리 폴더로 옮긴다 (글의 카테고리를 바꿀 때).
    ///
    /// 첨부 실물은 <c>Data\Files\{카테고리}\{파일명}</c> 에 살고, 경로를 만드는
    /// <see cref="Board.GetFilePath"/> 는 언제나 <b>글의 현재 카테고리</b>를 쓴다.
    /// 그래서 옮기지 못한 파일은 "안 보이는" 정도로 끝나지 않는다 — 대상 폴더에 같은 이름의
    /// <b>남의 파일</b>이 있으면 그 글의 첨부를 열 때 그것이 열리고, 지우면 그것이 지워진다.
    /// 예전에는 그 경우를 조용히 건너뛰었다.
    ///
    /// 이제 <see cref="SaveFileAsync"/> 와 같은 규칙으로 푼다 — <b>충돌하면 빈 이름을 찾아
    /// 옮기고, 실제로 저장된 이름을 DB 에 넣는다.</b> DB 를 먼저 고치고 실물을 옮기는 순서인데,
    /// 도중에 실패해도 DB 가 가리키는 이름은 방금 비어 있음을 확인한 이름이라
    /// 절대로 남의 파일을 가리키지 않기 때문이다.
    /// </summary>
    /// <param name="renameInDb">이름이 바뀔 때만 불린다. DB 의 파일명을 새 이름으로 고치고 성공 여부를 낸다.</param>
    /// <returns>옮겼거나 옮길 것이 없으면 true, 실패해 첨부가 끊겼으면 false</returns>
    public static async Task<bool> MoveToCategoryAsync(
        string fileName, string oldCategory, string newCategory, Func<string, Task<bool>> renameInDb)
    {
        var oldPath = Board.GetFilePath(fileName, oldCategory);
        if (!File.Exists(oldPath))
            return true;   // 실물이 원래 없다 — 이 함수가 만든 문제가 아니다

        Board.EnsureCategoryDirectory(newCategory);
        var targetName = ResolveFreeName(fileName, newCategory);

        try
        {
            if (targetName != fileName && !await renameInDb(targetName))
            {
                Debug.WriteLine($"[PostAttachments] 첨부 이름 변경 실패: {fileName} → {targetName}");
                return false;
            }

            File.Move(oldPath, Board.GetFilePath(targetName, newCategory));
            Debug.WriteLine($"[PostAttachments] 첨부 이동: {oldCategory}/{fileName} → {newCategory}/{targetName}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PostAttachments] 첨부 이동 실패({fileName}): {ex.Message}");

            // DB 이름만 바뀌고 실물이 안 옮겨졌으면 되돌린다.
            if (targetName != fileName)
            {
                try { await renameInDb(fileName); }
                catch (Exception rex) { Debug.WriteLine($"[PostAttachments] 이름 되돌리기 실패: {rex.Message}"); }
            }

            return false;
        }
    }

    /// <summary>
    /// <paramref name="category"/> 폴더에서 아직 비어 있는 이름을 찾는다.
    /// 부딪히지 않으면 원래 이름 그대로, 부딪히면 탐색기와 같은 <c>이름 (2).ext</c> 꼴.
    /// </summary>
    internal static string ResolveFreeName(string fileName, string category)
    {
        if (!File.Exists(Board.GetFilePath(fileName, category)))
            return fileName;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);

        for (int i = 2; i <= 1000; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!File.Exists(Board.GetFilePath(candidate, category)))
                return candidate;
        }

        // 1000개까지 찼다면 이름 규칙으로는 못 푼다 — 겹치지 않을 이름을 만들어 준다.
        return $"{stem}_{Guid.NewGuid():N}{ext}";
    }

    /// <summary>
    /// 글의 카테고리가 바뀌었을 때, 그 글에 딸린 첨부 <b>전부</b>(글 첨부 + 댓글 첨부)를
    /// 새 카테고리 폴더로 옮긴다.
    ///
    /// <para>글을 저장한 <b>뒤</b>, 첨부 변경(<see cref="ApplyAsync"/>)을 반영하기 <b>전</b>에
    /// 부른다. 그 순서라야 이번에 지우기로 한 첨부도 새 폴더에서 제대로 지워진다.</para>
    ///
    /// <para>옮길 일이 없으면(새 글이거나 카테고리가 그대로면) 아무 것도 하지 않으므로
    /// 부르는 쪽이 조건을 따로 걸 필요가 없다.</para>
    ///
    /// <para>⚠ 이 손질을 빠뜨리면 첨부가 조용히 끊긴다. <see cref="Board.GetFilePath"/> 는
    /// 언제나 <b>글의 현재 카테고리</b>로 경로를 만들기 때문에, 실물이 옛 폴더에 남으면
    /// 첨부를 열 때 "파일이 없다"가 되고 — 대상 폴더에 같은 이름의 <b>남의 파일</b>이
    /// 있으면 그것이 열리고 그것이 지워진다. 게시글 편집·메모 편집 창·메모 보드가
    /// 카테고리를 바꿀 수 있으므로 셋 다 이 한 벌을 부른다.</para>
    /// </summary>
    /// <returns>모두 옮겼거나 옮길 것이 없으면 true</returns>
    public static async Task<bool> MoveAllToCategoryAsync(
        BoardService service, int postNo, string oldCategory, string newCategory)
    {
        if (postNo <= 0 || string.IsNullOrEmpty(oldCategory) || oldCategory == newCategory)
            return true;

        var failed = new List<string>();

        try
        {
            Board.EnsureCategoryDirectory(newCategory);

            foreach (var file in await service.GetPostFilesByPostAsync(postNo))
            {
                if (string.IsNullOrEmpty(file.FileName)) continue;

                bool moved = await MoveToCategoryAsync(
                    file.FileName, oldCategory, newCategory,
                    renamed => service.UpdatePostFileNameAsync(file.No, renamed));

                if (!moved) failed.Add(file.FileName);
            }

            // 댓글 첨부파일도 같은 폴더에 살므로 함께 옮긴다
            foreach (var comment in await service.GetCommentsByPostAsync(postNo))
            {
                if (!comment.HasFile || string.IsNullOrEmpty(comment.FileName)) continue;

                var original = comment.FileName;

                bool moved = await MoveToCategoryAsync(
                    original, oldCategory, newCategory,
                    renamed =>
                    {
                        comment.FileName = renamed;
                        return service.UpdateCommentAsync(comment);
                    });

                if (!moved)
                {
                    comment.FileName = original;   // DB 를 못 고쳤으면 메모리도 되돌린다
                    failed.Add(original);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PostAttachments] 첨부 이동 실패: {ex.Message}");
            await NewSchool.Controls.UserErrorReporter.ReportAsync("첨부파일 이동", ex);
            return false;
        }

        if (failed.Count > 0)
        {
            await NewSchool.Controls.MessageBox.ShowErrorAsync(
                $"글은 '{newCategory}' 로 옮겼지만 첨부 {failed.Count}건을 함께 옮기지 못했습니다.\n" +
                string.Join("\n", failed) +
                "\n\n해당 첨부는 지금 열리지 않습니다. 다시 붙여 주세요.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 삭제 예정 파일을 지우고, 새로 붙인 파일을 복사해 등록한다.
    /// </summary>
    /// <returns>처리 후 이 글에 첨부가 남아 있는지(<c>Post.HasFile</c> 에 넣을 값)</returns>
    public static async Task<bool> ApplyAsync(
        BoardService service, PostFileListBox list, int postNo, string category)
    {
        // 글은 이미 저장됐고 첨부만 어긋난 상태다. 조용히 넘기면 화면에는 첨부가 있는데
        // 실제로는 없는(또는 지운 줄 알았는데 남은) 글이 된다 — 무엇이 어긋났는지 알린다.
        var failed = new List<string>();

        foreach (var fileToDelete in list.FilesToDelete)
        {
            if (await service.DeletePostFileAsync(fileToDelete.No, category))
                Debug.WriteLine($"[PostAttachments] 파일 삭제: {fileToDelete.FileName}");
            else
                failed.Add($"{fileToDelete.FileName} (삭제)");
        }

        int attached = 0;

        foreach (var fileBox in list.FileBoxes)
        {
            // OrgFilePath 가 있으면 새로 추가된 파일이다(기존 첨부는 비어 있다).
            if (string.IsNullOrEmpty(fileBox.OrgFilePath) || fileBox.PostFile == null)
            {
                attached++;   // 이미 붙어 있던 첨부
                continue;
            }

            // 복사 실패는 SaveFileAsync 가 이미 알렸다 — 여기서는 집계에서만 빼면 된다.
            var savedFile = await SaveFileAsync(fileBox.OrgFilePath, postNo, category);
            if (savedFile == null) continue;

            if (await service.AddPostFileAsync(savedFile) > 0)
            {
                attached++;
                Debug.WriteLine($"[PostAttachments] 파일 저장: {savedFile.FileName}");
            }
            else
            {
                failed.Add($"{savedFile.FileName} (등록)");
            }
        }

        if (failed.Count > 0)
        {
            await NewSchool.Controls.MessageBox.ShowErrorAsync(
                $"첨부파일 {failed.Count}건을 반영하지 못했습니다.\n{string.Join("\n", failed)}");
        }

        return attached > 0;
    }

    /// <summary>
    /// 원본 파일을 카테고리 폴더로 복사하고 등록할 <see cref="PostFile"/> 을 만든다.
    /// </summary>
    public static async Task<PostFile?> SaveFileAsync(string sourceFilePath, int postNo, string category)
    {
        try
        {
            Board.EnsureCategoryDirectory(category);

            var sourceFile = await StorageFile.GetFileFromPathAsync(sourceFilePath);
            var properties = await sourceFile.GetBasicPropertiesAsync();

            // 저장할 이름(희망값). 타임스탬프는 초 단위라 이것만으로는 유일하지 않다.
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var extension = Path.GetExtension(sourceFile.Name);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile.Name);
            var desiredName = $"{timestamp}_{fileNameWithoutExt}{extension}";

            var destinationFolder = await StorageFolder.GetFolderFromPathAsync(
                Path.GetDirectoryName(Board.GetFilePath(desiredName, category)));

            // ⚠ ReplaceExisting 을 쓰면 안 된다. 새 첨부는 저장 버튼을 눌렀을 때 한 루프에서
            // 한꺼번에 복사되므로 전부 같은 초를 받는다. 그래서 이름이 같은 파일을 두 개 붙이면
            // 뒤엣것이 앞엣것을 조용히 덮어썼고, DB 에는 첨부 2건인데 실물은 1개뿐인 상태가 됐다.
            // → 충돌 해소는 OS 에 맡기고(GenerateUniqueName), 실제로 저장된 이름을 DB 에 넣는다.
            var savedFile = await sourceFile.CopyAsync(
                destinationFolder, desiredName, NameCollisionOption.GenerateUniqueName);

            var postFile = new PostFile
            {
                Post = postNo,
                FileName = savedFile.Name,
                FileSize = (int)properties.Size,
                DateTime = DateTime.Now
            };

            Debug.WriteLine($"[PostAttachments] 파일 저장 완료: {savedFile.Path}");
            return postFile;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PostAttachments] 파일 저장 실패: {ex.Message}");
            await NewSchool.Controls.UserErrorReporter.ReportAsync("첨부파일 저장", ex);
            return null;
        }
    }
}
