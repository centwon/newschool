using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Data;

namespace NewSchool.Models
{
    /// <summary>
    /// 학사일정 모델 (NEIS API + DB 저장용)
    /// NEIS API에서 받아온 학교 학사일정 정보
    /// WinUI3 x:Bind를 위한 Bindable 특성 추가
    /// </summary>
    [Microsoft.UI.Xaml.Data.Bindable]
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
        /// 휴일 여부 (휴업일 또는 공휴일)
        /// </summary>
        public bool IsHoliday => 
            SBTR_DD_SC_NM == "휴업일" || SBTR_DD_SC_NM == "공휴일";

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
}
