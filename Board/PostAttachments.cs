using System;
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
    /// 삭제 예정 파일을 지우고, 새로 붙인 파일을 복사해 등록한다.
    /// </summary>
    /// <returns>처리 후 이 글에 첨부가 남아 있는지(<c>Post.HasFile</c> 에 넣을 값)</returns>
    public static async Task<bool> ApplyAsync(
        BoardService service, PostFileListBox list, int postNo, string category)
    {
        foreach (var fileToDelete in list.FilesToDelete)
        {
            await service.DeletePostFileAsync(fileToDelete.No, category);
            Debug.WriteLine($"[PostAttachments] 파일 삭제: {fileToDelete.FileName}");
        }

        foreach (var fileBox in list.FileBoxes)
        {
            // OrgFilePath 가 있으면 새로 추가된 파일이다(기존 첨부는 비어 있다).
            if (string.IsNullOrEmpty(fileBox.OrgFilePath) || fileBox.PostFile == null) continue;

            var savedFile = await SaveFileAsync(fileBox.OrgFilePath, postNo, category);
            if (savedFile != null)
            {
                await service.AddPostFileAsync(savedFile);
                Debug.WriteLine($"[PostAttachments] 파일 저장: {savedFile.FileName}");
            }
        }

        return list.FileCount > 0;
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
