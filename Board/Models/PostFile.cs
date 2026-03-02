using System;

namespace NewSchool.Board
{
    /// <summary>
    /// PostFile 모델 - 게시글 첨부파일 데이터
    /// </summary>
    [Microsoft.UI.Xaml.Data.Bindable]
    public class PostFile
    {
        public int No { get; set; } = -1;
        public int Post { get; set; }
        public DateTime DateTime { get; set; } = DateTime.Now;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }

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
}
