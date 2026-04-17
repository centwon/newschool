using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewSchool.Models;

/// <summary>
/// Undo/Redo 작업 기록
/// </summary>
public class UndoAction
{
    /// <summary>
    /// 기록 번호 (PK)
    /// </summary>
    public int No { get; set; }

    /// <summary>
    /// 과목 번호
    /// </summary>
    public int CourseId { get; set; }

    /// <summary>
    /// 학급/강의실
    /// </summary>
    public string Room { get; set; } = string.Empty;

    /// <summary>
    /// 작업 유형
    /// </summary>
    public UndoActionType ActionType { get; set; }

    /// <summary>
    /// 작업 설명
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 작업 데이터 (JSON)
    /// </summary>
    public string ActionData { get; set; } = string.Empty;

    /// <summary>
    /// 작업 시각
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Undo 여부
    /// </summary>
    public bool IsUndone { get; set; }

    /// <summary>
    /// Undo 시각
    /// </summary>
    public DateTime? UndoneAt { get; set; }

    #region Computed Properties

    /// <summary>
    /// 작업 유형 표시명
    /// </summary>
    public string ActionTypeDisplay => ActionType switch
    {
        UndoActionType.ScheduleShift => "일정 이동",
        UndoActionType.ScheduleCreate => "일정 생성",
        UndoActionType.ScheduleDelete => "일정 삭제",
        UndoActionType.ScheduleUpdate => "일정 수정",
        UndoActionType.SectionUpdate => "단원 수정",
        UndoActionType.BulkGenerate => "자동 배치",
        UndoActionType.Merge => "수업 병합",
        UndoActionType.Split => "수업 분할",
        _ => "알 수 없음"
    };

    /// <summary>
    /// 생성 시각 표시
    /// </summary>
    public string CreatedAtDisplay => CreatedAt.ToString("M/d HH:mm");

    /// <summary>
    /// 상태 표시
    /// </summary>
    public string StatusDisplay => IsUndone ? "취소됨" : "실행됨";

    #endregion

    #region Data Serialization

    /// <summary>
    /// 작업 데이터 가져오기 (AOT 안전 — 비제네릭 오버로드 사용)
    /// </summary>
    public T? GetData<T>() where T : class
    {
        if (string.IsNullOrEmpty(ActionData))
            return null;

        try
        {
            return JsonSerializer.Deserialize(ActionData, typeof(T), UndoActionJsonContext.Default) as T;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 작업 데이터 설정 (AOT 안전)
    /// </summary>
    public void SetData<T>(T data) where T : class
    {
        ActionData = JsonSerializer.Serialize(data, typeof(T), UndoActionJsonContext.Default);
    }

    #endregion
}

/// <summary>
/// UndoAction 직렬화용 JsonSerializerContext (AOT/Trimming 호환)
/// </summary>
[JsonSerializable(typeof(ShiftActionData))]
[JsonSerializable(typeof(ScheduleActionData))]
[JsonSerializable(typeof(BulkGenerateActionData))]
[JsonSerializable(typeof(MergeActionData))]
internal partial class UndoActionJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Undo 작업 유형
/// </summary>
public enum UndoActionType
{
    /// <summary>
    /// 일정 이동 (Push/Pull)
    /// </summary>
    ScheduleShift = 1,

    /// <summary>
    /// 일정 생성
    /// </summary>
    ScheduleCreate = 2,

    /// <summary>
    /// 일정 삭제
    /// </summary>
    ScheduleDelete = 3,

    /// <summary>
    /// 일정 수정
    /// </summary>
    ScheduleUpdate = 4,

    /// <summary>
    /// 단원 수정
    /// </summary>
    SectionUpdate = 5,

    /// <summary>
    /// 자동 배치
    /// </summary>
    BulkGenerate = 6,

    /// <summary>
    /// 수업 병합
    /// </summary>
    Merge = 7,

    /// <summary>
    /// 수업 분할
    /// </summary>
    Split = 8
}

#region Action Data Classes

/// <summary>
/// 일정 이동 데이터
/// </summary>
public class ShiftActionData
{
    /// <summary>
    /// 이동된 일정 목록
    /// </summary>
    public List<ScheduleShiftInfo> ShiftedSchedules { get; set; } = new();

    /// <summary>
    /// 이동 방향 (Push: 1, Pull: -1)
    /// </summary>
    public int Direction { get; set; }

    /// <summary>
    /// 이동 기준일
    /// </summary>
    public DateTime FromDate { get; set; }

    /// <summary>
    /// 이동 시작 교시
    /// </summary>
    public int FromPeriod { get; set; }
}

/// <summary>
/// 일정 이동 정보
/// </summary>
public class ScheduleShiftInfo
{
    /// <summary>
    /// 일정 번호
    /// </summary>
    public int ScheduleId { get; set; }

    /// <summary>
    /// 원래 날짜
    /// </summary>
    public DateTime OriginalDate { get; set; }

    /// <summary>
    /// 원래 교시
    /// </summary>
    public int OriginalPeriod { get; set; }

    /// <summary>
    /// 이동 후 날짜
    /// </summary>
    public DateTime NewDate { get; set; }

    /// <summary>
    /// 이동 후 교시
    /// </summary>
    public int NewPeriod { get; set; }
}

/// <summary>
/// 일정 생성/삭제 데이터
/// </summary>
public class ScheduleActionData
{
    /// <summary>
    /// 일정 번호
    /// </summary>
    public int ScheduleId { get; set; }

    /// <summary>
    /// 과목 번호
    /// </summary>
    public int CourseId { get; set; }

    /// <summary>
    /// 학급
    /// </summary>
    public string Room { get; set; } = string.Empty;

    /// <summary>
    /// 날짜
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// 교시
    /// </summary>
    public int Period { get; set; }

    /// <summary>
    /// 고정 여부
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// 매핑된 단원 목록
    /// </summary>
    public List<int> SectionIds { get; set; } = new();
}

/// <summary>
/// 자동 배치 데이터
/// </summary>
public class BulkGenerateActionData
{
    /// <summary>
    /// 생성된 일정 ID 목록
    /// </summary>
    public List<int> CreatedScheduleIds { get; set; } = new();

    /// <summary>
    /// 시작일
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// 종료일
    /// </summary>
    public DateTime EndDate { get; set; }
}

/// <summary>
/// 병합 데이터
/// </summary>
public class MergeActionData
{
    /// <summary>
    /// 대상 일정 번호
    /// </summary>
    public int TargetScheduleId { get; set; }

    /// <summary>
    /// 병합된 단원 ID 목록
    /// </summary>
    public List<int> MergedSectionIds { get; set; } = new();

    /// <summary>
    /// 병합 전 매핑 정보
    /// </summary>
    public List<UnitMapInfo> OriginalMaps { get; set; } = new();
}

/// <summary>
/// 단원 매핑 정보
/// </summary>
public class UnitMapInfo
{
    public int MapId { get; set; }
    public int ScheduleId { get; set; }
    public int SectionId { get; set; }
    public int AllocatedHours { get; set; }
    public int OrderInSlot { get; set; }
}

#endregion
