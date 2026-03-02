using System;

namespace NewSchool.Models;

/// <summary>
/// 수업 일지 모델
/// 교사의 교시별 수업 진행 기록
/// IDailyRecord 구현으로 ClassDiary(학급일지)와 공통 기반 공유
/// </summary>
public class LessonLog : NotifyPropertyChangedBase, IDailyRecord
{
    #region Fields - 기본 정보

    private int _no = -1;
    private int? _lesson;
    private string _teacherID = string.Empty;
    private int _year = DateTime.Today.Year;
    private int _semester;
    private DateTime _date = DateTime.Today;
    private int _period;
    private string _subject = string.Empty;

    #endregion

    #region Fields - 학급/장소

    private int _grade;
    private int _class;
    private string _room = string.Empty;

    #endregion

    #region Fields - 단원 연결

    private int? _courseSectionNo;
    private string _sectionName = string.Empty;

    #endregion

    #region Fields - 수업 내용

    private string _topic = string.Empty;
    private string _content = string.Empty;
    private string _note = string.Empty;

    #endregion

    #region Fields - 메타 정보

    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;

    #endregion

    #region Properties - IDailyRecord

    /// <summary>일련번호 (PK)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>교사 ID</summary>
    public string TeacherID
    {
        get => _teacherID;
        set => SetProperty(ref _teacherID, value);
    }

    /// <summary>학년도</summary>
    public int Year
    {
        get => _year;
        set => SetProperty(ref _year, value);
    }

    /// <summary>학기</summary>
    public int Semester
    {
        get => _semester;
        set => SetProperty(ref _semester, value);
    }

    /// <summary>수업 날짜</summary>
    public DateTime Date
    {
        get => _date;
        set => SetProperty(ref _date, value);
    }

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

    #endregion

    #region Properties - 시간표 연결

    /// <summary>Lesson FK (시간표 연결, nullable)</summary>
    public int? Lesson
    {
        get => _lesson;
        set => SetProperty(ref _lesson, value);
    }

    /// <summary>교시 (1~7)</summary>
    public int Period
    {
        get => _period;
        set => SetProperty(ref _period, value);
    }

    /// <summary>과목명</summary>
    public string Subject
    {
        get => _subject;
        set => SetProperty(ref _subject, value);
    }

    #endregion

    #region Properties - 학급/장소

    /// <summary>학년</summary>
    public int Grade
    {
        get => _grade;
        set => SetProperty(ref _grade, value);
    }

    /// <summary>반</summary>
    public int Class
    {
        get => _class;
        set => SetProperty(ref _class, value);
    }

    /// <summary>강의실 (예: "과학실", "음악실"). 일반 교실이면 비워둠</summary>
    public string Room
    {
        get => _room;
        set => SetProperty(ref _room, value);
    }

    #endregion

    #region Properties - 단원 연결

    /// <summary>단원 번호 (FK → CourseSection, 선택사항)</summary>
    public int? CourseSectionNo
    {
        get => _courseSectionNo;
        set => SetProperty(ref _courseSectionNo, value);
    }

    /// <summary>단원명 (비정규화, 빠른 표시용)</summary>
    public string SectionName
    {
        get => _sectionName;
        set => SetProperty(ref _sectionName, value);
    }

    #endregion

    #region Properties - 수업 내용

    /// <summary>수업 주제</summary>
    public string Topic
    {
        get => _topic;
        set => SetProperty(ref _topic, value);
    }

    /// <summary>수업 내용</summary>
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    /// <summary>교사 메모 (수업 반성, 개인 노트)</summary>
    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    #endregion

    #region Computed Properties

    /// <summary>교시 표시 문자열</summary>
    public string PeriodDisplay => Period > 0 ? $"{Period}교시" : "";

    /// <summary>날짜 표시 (짧은 형식)</summary>
    public string DateDisplay => Date.ToString("M/d(ddd)");

    /// <summary>날짜+교시 표시</summary>
    public string DatePeriodDisplay => $"{DateDisplay} {PeriodDisplay}";

    /// <summary>학급 표시 (예: "2-3")</summary>
    public string ClassDisplay => Grade > 0 && Class > 0 ? $"{Grade}-{Class}" : "";

    /// <summary>수업 요약 (목록 표시용)</summary>
    public string Summary
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(ClassDisplay)) parts.Add(ClassDisplay);
            if (!string.IsNullOrWhiteSpace(PeriodDisplay)) parts.Add(PeriodDisplay);
            if (!string.IsNullOrWhiteSpace(Subject)) parts.Add(Subject);
            return string.Join(" ", parts);
        }
    }

    #endregion
}
