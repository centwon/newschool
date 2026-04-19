using System.Collections.Generic;

namespace NewSchool.Models;

/// <summary>
/// 좌석 배치 옵션 — 학급별로 DB `SeatArrangement.OptionsJson`에 JSON으로 저장.
/// </summary>
public class SeatOptions
{
    #region 이력 기반

    /// <summary>최근 N회 짝 이력을 배제한다. 0이면 비활성.</summary>
    public int RecentPairAvoidRounds { get; set; } = 1;

    /// <summary>최근 N회 같은 자리(Row/Col 일치) 배치를 배제한다. 0이면 비활성.</summary>
    public int RecentPositionAvoidRounds { get; set; }

    #endregion

    #region 학생 속성 기반

    /// <summary>짝 모드(Jjak=2)에서 남녀 교차 짝을 우선한다.</summary>
    public bool PreferMixedGenderPair { get; set; }

    /// <summary>앞자리 우선 학생 ID 목록 — Row 범위 내 고정.</summary>
    public List<string> FrontPriorityStudentIds { get; set; } = new();

    /// <summary>앞자리 범위 (0 ~ FrontPriorityMaxRow) — 기본 0,1 두 줄</summary>
    public int FrontPriorityMaxRow { get; set; } = 1;

    #endregion

    #region 배치 제약 (기존)

    /// <summary>절대 짝이 되면 안 되는 학생 쌍 목록 (StudentID_A, StudentID_B)</summary>
    public List<PairRule> ExclusionPairs { get; set; } = new();

    /// <summary>반드시 짝이 되어야 하는 학생 쌍 목록</summary>
    public List<PairRule> FixedPairs { get; set; } = new();

    #endregion

    #region 배치 방식

    /// <summary>제약 만족을 위한 재시도 횟수 (100/500/1000 등).</summary>
    public int MaxAttempts { get; set; } = 500;

    #endregion

    public class PairRule
    {
        public string IdA { get; set; } = string.Empty;
        public string IdB { get; set; } = string.Empty;
    }
}
