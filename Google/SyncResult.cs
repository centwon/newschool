using System;
using System.Collections.Generic;

namespace NewSchool.Google;

/// <summary>
/// Google Calendar 동기화 결과
/// </summary>
public class SyncResult
{
    public bool Success { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Conflicts { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
    public DateTime SyncedAt { get; set; } = DateTime.Now;

    public string Summary =>
        Success
            ? $"동기화 완료: 생성 {Created}, 수정 {Updated}, 삭제 {Deleted}"
            : $"동기화 실패: {string.Join("; ", ErrorMessages)}";
}
