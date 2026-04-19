using System;
using System.Collections.Generic;

namespace NewSchool.Models;

/// <summary>
/// 학급별 좌석 배치 메타 정보 — DB `SeatArrangement` 테이블에 1:1 매핑.
/// (SchoolCode, Year, Grade, Class)가 유일키.
/// </summary>
public class SeatArrangement
{
    public int No { get; set; }
    public string SchoolCode { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Grade { get; set; }
    public int Class { get; set; }

    /// <summary>줄 수 (가로 그룹 수)</summary>
    public int Jul { get; set; } = 5;

    /// <summary>짝 여부 → Jjak=2면 짝 모드, 1이면 1인석</summary>
    public int Jjak { get; set; } = 1;

    /// <summary>세로 행 수</summary>
    public int Rows { get; set; } = 1;

    /// <summary>사진 표시 여부</summary>
    public bool ShowPhoto { get; set; }

    /// <summary>출력용 공지/메시지</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>배치 잠금 여부 (true면 드래그·재배치 금지)</summary>
    public bool IsLocked { get; set; }

    /// <summary>배치 옵션 JSON (SeatOptions 직렬화)</summary>
    public string OptionsJson { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>메모리 상 부가 정보 — 좌석 셀 목록 (DB 로드 시 채움)</summary>
    public List<SeatAssignment> Assignments { get; set; } = new();
}

/// <summary>
/// 좌석 한 칸 — DB `SeatAssignment` 테이블에 1:1 매핑.
/// </summary>
public class SeatAssignment
{
    public int No { get; set; }
    public int ArrangementNo { get; set; }
    public int Row { get; set; }
    public int Col { get; set; }

    /// <summary>배정된 학생 — null이면 빈 좌석</summary>
    public string? StudentID { get; set; }

    public bool IsUnUsed { get; set; }
    public bool IsHidden { get; set; }
    public bool IsFixed { get; set; }
}

/// <summary>
/// 짝 이력 한 건 — DB `SeatHistory`.
/// 저장 시점마다 짝(인접 좌석)이었던 모든 학생 쌍을 기록한다.
/// </summary>
public class SeatHistoryEntry
{
    public int No { get; set; }
    public string SchoolCode { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Grade { get; set; }
    public int Class { get; set; }
    public string StudentID_A { get; set; } = string.Empty;
    public string StudentID_B { get; set; } = string.Empty;
    public int Round { get; set; }

    /// <summary>Pair = 짝 (인접 좌석). 향후 확장용.</summary>
    public string Kind { get; set; } = "Pair";

    public DateTime SavedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 좌석 위치 이력 — DB `SeatPosHistory`. 지난 자리 배제용.
/// </summary>
public class SeatPosHistoryEntry
{
    public int No { get; set; }
    public string SchoolCode { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Grade { get; set; }
    public int Class { get; set; }
    public string StudentID { get; set; } = string.Empty;
    public int Row { get; set; }
    public int Col { get; set; }
    public int Round { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.Now;
}
