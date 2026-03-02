namespace NewSchool.Pages
{
    /// <summary>
    /// AnnualLessonPlanPage 네비게이션 파라미터
    /// </summary>
    public class AnnualLessonPlanPageParameter
    {
        /// <summary>교사 ID (필수)</summary>
        public string TeacherId { get; set; } = string.Empty;

        /// <summary>DB 경로 (필수)</summary>
        public string DbPath { get; set; } = string.Empty;

        /// <summary>학년도</summary>
        public int Year { get; set; }

        /// <summary>학기 (1 또는 2)</summary>
        public int Semester { get; set; }

        /// <summary>특정 Course로 바로 이동 (선택)</summary>
        public int? CourseNo { get; set; }

        /// <summary>특정 학급으로 바로 이동 (선택)</summary>
        public int? TargetGrade { get; set; }

        /// <summary>특정 반으로 바로 이동 (선택)</summary>
        public int? TargetClass { get; set; }
    }
}
