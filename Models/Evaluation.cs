using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models
{
    /// <summary>
    /// 평가(성적) 정보 (NEIS 표준)
    /// 학생의 과목별 평가 결과 관리
    /// </summary>
    public class Evaluation : NotifyPropertyChangedBase, IEntity, IYearSemesterEntity
    {
        #region Fields

        private int _no = -1;
        private string _studentId = string.Empty;
        private string _schoolCode = string.Empty;
        private int _year = DateTime.Today.Year;
        private int _semester = 1;
        private int _courseNo = -1;
        private string _subject = string.Empty;
        private string _evaluationType = "지필";
        private int _round = 1;
        private decimal _score = 0;
        private decimal _maxScore = 100;
        private string _grade = string.Empty;
        private int _rank = 0;
        private int _totalStudents = 0;
        private string _achievement = string.Empty;
        private string _teacherId = string.Empty;
        private string _memo = string.Empty;
        private DateTime _createdAt = DateTime.Now;
        private DateTime _updatedAt = DateTime.Now;
        private bool _isDeleted = false;

        #endregion

        #region Properties - 기본 정보

        /// <summary>PK (자동 증가)</summary>
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        /// <summary>학생 ID (FK: Student.StudentID)</summary>
        public string StudentID
        {
            get => _studentId;
            set => SetProperty(ref _studentId, value);
        }

        /// <summary>학교 코드 (FK: School.SchoolCode)</summary>
        public string SchoolCode
        {
            get => _schoolCode;
            set => SetProperty(ref _schoolCode, value);
        }

        /// <summary>학년도</summary>
        public int Year
        {
            get => _year;
            set => SetProperty(ref _year, value);
        }

        /// <summary>학기 (1 or 2)</summary>
        public int Semester
        {
            get => _semester;
            set => SetProperty(ref _semester, value);
        }

        /// <summary>개설 과목 번호 (FK: Course.No)</summary>
        public int CourseNo
        {
            get => _courseNo;
            set => SetProperty(ref _courseNo, value);
        }

        /// <summary>과목명 (비정규화, 빠른 조회용)</summary>
        public string Subject
        {
            get => _subject;
            set => SetProperty(ref _subject, value);
        }

        #endregion

        #region Properties - 평가 정보

        /// <summary>
        /// 평가 유형
        /// 지필/수행1/수행2/수행3/기말/종합
        /// </summary>
        public string EvaluationType
        {
            get => _evaluationType;
            set => SetProperty(ref _evaluationType, value);
        }

        /// <summary>차시 (1차, 2차, 중간, 기말 등)</summary>
        public int Round
        {
            get => _round;
            set => SetProperty(ref _round, value);
        }

        #endregion

        #region Properties - 성적

        /// <summary>득점</summary>
        public decimal Score
        {
            get => _score;
            set => SetProperty(ref _score, value);
        }

        /// <summary>만점</summary>
        public decimal MaxScore
        {
            get => _maxScore;
            set => SetProperty(ref _maxScore, value);
        }

        /// <summary>
        /// 등급 (석차등급)
        /// 1~9등급, A~F, 수우미양가 등
        /// </summary>
        public string Grade
        {
            get => _grade;
            set => SetProperty(ref _grade, value);
        }

        /// <summary>석차</summary>
        public int Rank
        {
            get => _rank;
            set => SetProperty(ref _rank, value);
        }

        /// <summary>전체 학생 수</summary>
        public int TotalStudents
        {
            get => _totalStudents;
            set => SetProperty(ref _totalStudents, value);
        }

        /// <summary>성취도 (성취기준 평가)</summary>
        public string Achievement
        {
            get => _achievement;
            set => SetProperty(ref _achievement, value);
        }

        #endregion

        #region Properties - 기타

        /// <summary>평가 교사 ID (FK: Teacher.TeacherID)</summary>
        public string TeacherID
        {
            get => _teacherId;
            set => SetProperty(ref _teacherId, value);
        }

        /// <summary>특이사항 메모</summary>
        public string Memo
        {
            get => _memo;
            set => SetProperty(ref _memo, value);
        }

        #endregion

        #region Properties - 메타 정보

        /// <summary>생성일시</summary>
        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value);
        }

        /// <summary>수정일시</summary>
        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set => SetProperty(ref _updatedAt, value);
        }

        /// <summary>논리 삭제 플래그</summary>
        public bool IsDeleted
        {
            get => _isDeleted;
            set => SetProperty(ref _isDeleted, value);
        }

        #endregion

        #region Methods

        public override string ToString()
        {
            return $"{Subject} {EvaluationType} {Round}차 - {Score}점";
        }

        /// <summary>
        /// 백분율 계산
        /// </summary>
        public decimal GetPercentage()
        {
            if (MaxScore == 0) return 0;
            return (Score / MaxScore) * 100;
        }

        /// <summary>
        /// 석차 백분율 계산
        /// </summary>
        public decimal GetRankPercentage()
        {
            if (TotalStudents == 0) return 0;
            return ((decimal)Rank / TotalStudents) * 100;
        }

        /// <summary>
        /// 등급 계산 (9등급제)
        /// </summary>
        public int CalculateGrade9()
        {
            var percentage = GetRankPercentage();

            if (percentage <= 4) return 1;
            if (percentage <= 11) return 2;
            if (percentage <= 23) return 3;
            if (percentage <= 40) return 4;
            if (percentage <= 60) return 5;
            if (percentage <= 77) return 6;
            if (percentage <= 89) return 7;
            if (percentage <= 96) return 8;
            return 9;
        }

        /// <summary>
        /// 성취도 등급 검증 (A~E)
        /// </summary>
        public bool IsValidAchievement()
        {
            return Achievement == "A" || Achievement == "B" || Achievement == "C"
                || Achievement == "D" || Achievement == "E";
        }

        #endregion
    }
}
