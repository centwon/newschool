using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models
{
    /// <summary>
    /// 기본 엔티티 인터페이스
    /// 모든 데이터베이스 테이블 엔티티가 구현
    /// </summary>
    public interface IEntity
    {
        /// <summary>Primary Key (자동 증가)</summary>
        int No { get; set; }
    }

    /// <summary>
    /// 삭제 가능한 엔티티 인터페이스
    /// 논리 삭제를 지원하는 엔티티가 구현
    /// </summary>
    public interface IDeletable : IEntity
    {
        /// <summary>논리 삭제 플래그</summary>
        bool IsDeleted { get; set; }
    }

    /// <summary>
    /// 학년도/학기 정보를 가진 엔티티 인터페이스
    /// </summary>
    public interface IYearSemesterEntity : IEntity
    {
        /// <summary>학년도</summary>
        int Year { get; set; }

        /// <summary>학기</summary>
        int Semester { get; set; }
    }

    /// <summary>
    /// 교사 기록 인터페이스
    /// </summary>
    public interface ITeacherRecord : IEntity
    {
        /// <summary>작성 교사 ID</summary>
        string TeacherID { get; set; }

        /// <summary>작성일시</summary>
        DateTime Date { get; set; }
    }

    /// <summary>
    /// 교사 일지 공통 인터페이스
    /// ClassDiary(학급일지)와 LessonLog(수업일지)가 구현
    /// </summary>
    public interface IDailyRecord : IEntity, IYearSemesterEntity
    {
        /// <summary>작성 교사 ID</summary>
        string TeacherID { get; set; }

        /// <summary>기록 날짜</summary>
        DateTime Date { get; set; }

        /// <summary>생성일시</summary>
        DateTime CreatedAt { get; set; }

        /// <summary>수정일시</summary>
        DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// 학생 기록 인터페이스
    /// ⭐ 재설계: Subject_No 제거, CourseNo/SubjectName 추가
    /// ⭐ Category: string → LogCategory enum 변경
    /// </summary>
    public interface IStudentRecord : ITeacherRecord
    {
        /// <summary>학생 ID</summary>
        string StudentID { get; set; }

        /// <summary>학기</summary>
        int Semester { get; set; }

        /// <summary>카테고리 (LogCategory enum)</summary>
        LogCategory Category { get; set; }

        /// <summary>수업 번호 (FK: Course.No)</summary>
        int CourseNo { get; set; }

        /// <summary>과목명</summary>
        string SubjectName { get; set; }

        /// <summary>기록 내용</summary>
        string Log { get; set; }
    }
}
