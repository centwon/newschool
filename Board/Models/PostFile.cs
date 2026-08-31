using System;

namespace NewSchool.Board;

/// <summary>
/// PostFile 모델 - 게시글 첨부파일 데이터
/// </summary>
public class PostFile
{
    public int No { get; set; } = -1;
    public int Post { get; set; }
    public DateTime DateTime { get; set; } = DateTime.Now;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }

    /// <summary>
    /// 값이 같은 새 <see cref="PostFile"/>. 캐시가 자기 것을 남에게 넘기지 않기 위한 사본이다
    /// (<see cref="Post.Clone"/> 참고).
    /// </summary>
    public PostFile Clone() => new()
    {
        No = No,
        Post = Post,
        DateTime = DateTime,
        FileName = FileName,
        FileSize = FileSize,
    };

    /// <summary>
    /// 파일 크기 표시용 문자열
    /// </summary>
    public string FileSizeDisplay
    {
        get
        {
            if (FileSize < 1024)
                return $"{FileSize} B";
            else if (FileSize < 1024 * 1024)
                return $"{FileSize / 1024.0:F1} KB";
            else
                return $"{FileSize / (1024.0 * 1024):F1} MB";
        }
    }
}
