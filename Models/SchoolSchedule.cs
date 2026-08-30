using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Data;

namespace NewSchool.Models;

/// <summary>
/// 학사일정 모델 (NEIS API + DB 저장용)
/// NEIS API에서 받아온 학교 학사일정 정보
/// WinUI3 x:Bind를 위한 Bindable 특성 추가
/// </summary>
public class SchoolSchedule
{
    // ==========================================
    // DB 관리 필드
    // ==========================================
    
    /// <summary>
    /// 일련번호 (Primary Key)
    /// </summary>
    public int No { get; set; }

    /// <summary>
    /// 수동 입력 여부 (NEIS API가 아닌 직접 입력한 일정)
    /// </summary>
    public bool IsManual { get; set; }

    /// <summary>
    /// 생성일시
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 수정일시
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 삭제 여부 (Soft Delete)
    /// </summary>
    public bool IsDeleted { get; set; }

    // ==========================================
    // NEIS API 필드
    // ==========================================

    /// <summary>
    /// 학교명
    /// </summary>
    public string SCHUL_NM { get; set; } = string.Empty;

    /// <summary>
    /// 시도교육청코드
    /// </summary>
    public string ATPT_OFCDC_SC_CODE { get; set; } = string.Empty;

    /// <summary>
    /// 시도교육청명
    /// </summary>
    public string ATPT_OFCDC_SC_NM { get; set; } = string.Empty;

    /// <summary>
    /// 표준학교코드
    /// </summary>
    public string SD_SCHUL_CODE { get; set; } = string.Empty;

    /// <summary>
    /// 학년도 (Academic Year)
    /// </summary>
    public int AY { get; set; }

    /// <summary>
    /// 수업공제일명 (예: 해당없음, 휴업일, 공휴일)
    /// </summary>
    public string SBTR_DD_SC_NM { get; set; } = string.Empty;

    /// <summary>
    /// 학사일자 (Academic Affairs Date)
    /// </summary>
    public DateTime AA_YMD { get; set; }

    /// <summary>
    /// 행사명
    /// </summary>
    public string EVENT_NM { get; set; } = string.Empty;

    /// <summary>
    /// 행사내용
    /// </summary>
    public string EVENT_CNTNT { get; set; } = string.Empty;

    // ==========================================
    // 학년별 행사 대상 여부
    // ==========================================

    /// <summary>
    /// 1학년 행사 여부
    /// </summary>
    public bool ONE_GRADE_EVENT_YN { get; set; }

    /// <summary>
    /// 2학년 행사 여부
    /// </summary>
    public bool TW_GRADE_EVENT_YN { get; set; }

    /// <summary>
    /// 3학년 행사 여부
    /// </summary>
    public bool THREE_GRADE_EVENT_YN { get; set; }

    /// <summary>
    /// 4학년 행사 여부
    /// </summary>
    public bool FR_GRADE_EVENT_YN { get; set; }

    /// <summary>
    /// 5학년 행사 여부
    /// </summary>
    public bool FIV_GRADE_EVENT_YN { get; set; }

    /// <summary>
    /// 6학년 행사 여부
    /// </summary>
    public bool SIX_GRADE_EVENT_YN { get; set; }

    // ==========================================
    // Helper Methods
    // ==========================================

    /// <summary>
    /// 수업을 하지 않는 날인가.
    ///
    /// <para>NEIS 의 <b>수업공제일자명</b>(<see cref="SBTR_DD_SC_NM"/>)이 그것만을 위한 칸이다 —
    /// 행사명으로 짐작할 일이 아니다. 앱에서 손으로 넣을 때도 같은 셋(해당없음·휴업일·공휴일)
    /// 중에서 고르므로 이 칸은 늘 채워져 있다.</para>
    ///
    /// <para>정확히 같은지가 아니라 <b>포함</b>으로 본다 — 교육청에 따라 "토요휴업일" 처럼
    /// 앞뒤가 붙은 값이 올 수 있다.</para>
    /// </summary>
    public bool IsHoliday =>
        (SBTR_DD_SC_NM ?? string.Empty).Contains("휴업")
        || (SBTR_DD_SC_NM ?? string.Empty).Contains("공휴");

    /// <summary>
    /// 대상 학년 텍스트 (예: "1,2,3학년")
    /// </summary>
    public string GetTargetGradesText()
    {
        var grades = new List<int>();
        if (ONE_GRADE_EVENT_YN) grades.Add(1);
        if (TW_GRADE_EVENT_YN) grades.Add(2);
        if (THREE_GRADE_EVENT_YN) grades.Add(3);
        if (FR_GRADE_EVENT_YN) grades.Add(4);
        if (FIV_GRADE_EVENT_YN) grades.Add(5);
        if (SIX_GRADE_EVENT_YN) grades.Add(6);

        if (grades.Count == 0)
            return "전체";
        if (grades.Count == 6)
            return "전체";

        return string.Join(",", grades) + "학년";
    }

    public override string ToString()
    {
        return $"{AA_YMD:yyyy-MM-dd} {EVENT_NM}";
    }
}
