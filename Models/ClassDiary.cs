using System;
using System.Collections.Generic;
using System.Linq;

namespace NewSchool.Models;

/// <summary>
/// 학급 일지 모델
/// 출결, 메모, 알림장, 학생 생활 기록 관리
/// </summary>
public class ClassDiary : NotifyPropertyChangedBase
{
    #region Fields - 기본 정보

    private int _no = -1;
    private string _schoolCode = string.Empty;
    private string _teacherId = string.Empty;
    private int _year = DateTime.Today.Year;
    private int _semester = 1;
    private DateTime _date = DateTime.Today;
    private int _grade;
    private int _class;

    #endregion

    #region Fields - 출결 정보

    private string _absent = string.Empty;
    private string _late = string.Empty;
    private string _leaveEarly = string.Empty;

    #endregion

    #region Fields - 기록 내용

    private string _memo = string.Empty;
    private string _notice = string.Empty;
    private string _life = string.Empty;

    #endregion

    #region Fields - 메타 정보

    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;

    #endregion

    #region Properties - 기본 정보

    /// <summary>PK (자동 증가)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    #endregion

    #region Properties - 기본 정보

    /// <summary>학교 코드 (FK: School.SchoolCode)</summary>
    public string SchoolCode
    {
        get => _schoolCode;
        set => SetProperty(ref _schoolCode, value);
    }

    /// <summary>작성 교사 ID (FK: Teacher.TeacherID)</summary>
    public string TeacherID
    {
        get => _teacherId;
        set => SetProperty(ref _teacherId, value);
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

    /// <summary>날짜</summary>
    public DateTime Date
    {
        get => _date;
        set
        {
            // 시간 부분 제거, 날짜만 저장
            if (SetProperty(ref _date, value.Date))
                Notify(nameof(DayOfWeek), nameof(DayOfWeekKorean), nameof(DateDisplay));
        }
    }

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

    #endregion

    #region Properties - 출결 메모

    // ★ 결정(2026-08-30): 아래 셋은 **교사가 손으로 적는 메모칸**이다. 출결 기록이 아니다.
    //
    // 그래서 학생을 가리키지 않는다 — 그냥 교사가 친 글자다. 동명이인은 구분되지 않고,
    // 학생 이름을 고쳐도 지난 일지의 글자는 그대로다. 그것이 이 칸의 성격이며 고칠 대상이
    // 아니다. 학생을 가리키는 출결이 필요해지면 학적(Enrollment)을 가리키는 표를 따로
    // 만들어야 하고, 이 칸을 고쳐 쓰면 안 된다.
    //
    // 그 성격에 맞춰 **이름으로 조회하던 API 를 걷어냈다**(2026-08-30) —
    // HasAttendanceIssue(name)·GetAttendanceStatus(name)·AttendanceIssueCount·
    // ClearAttendance()·ClearAll(). 부르는 곳은 한 곳도 없었지만(ViewModel 통과 래퍼뿐),
    // 남겨 두면 "이 칸으로 학생별 출결을 물을 수 있다" 로 읽힌다. 실제로 그 API 는
    // 부분 일치로 엉뚱한 학생을 잡은 적이 있고(김하 ⊂ 김하늘), 고쳐도 동명이인은 못 막는다.
    //
    // 남긴 둘(HasAttendanceIssues·AttendanceSummary)은 **글자가 있나 / 뭐라고 적혔나** 만
    // 본다. 학생을 찾지 않으므로 메모칸이라는 성격과 어긋나지 않는다.

    /// <summary>결석 (교사가 손으로 적는다. 보통 쉼표로 이름을 잇는다)</summary>
    public string Absent
    {
        get => _absent;
        set { if (SetProperty(ref _absent, value ?? string.Empty)) NotifyAttendance(); }
    }

    /// <summary>지각 (교사가 손으로 적는다)</summary>
    public string Late
    {
        get => _late;
        set { if (SetProperty(ref _late, value ?? string.Empty)) NotifyAttendance(); }
    }

    /// <summary>조퇴 (교사가 손으로 적는다)</summary>
    public string LeaveEarly
    {
        get => _leaveEarly;
        set { if (SetProperty(ref _leaveEarly, value ?? string.Empty)) NotifyAttendance(); }
    }

    /// <summary>출결 세 칸이 함께 먹이는 계산 속성들. 셋 중 무엇이 바뀌어도 같이 알린다.</summary>
    private void NotifyAttendance() => Notify(
        nameof(HasAttendanceIssues), nameof(AttendanceSummary));

    #endregion

    #region Properties - 기록 내용

    /// <summary>메모</summary>
    public string Memo
    {
        get => _memo;
        set { if (SetProperty(ref _memo, value ?? string.Empty)) Notify(nameof(HasMemo)); }
    }

    /// <summary>알림장 (HTML 또는 텍스트)</summary>
    public string Notice
    {
        get => _notice;
        set { if (SetProperty(ref _notice, value ?? string.Empty)) Notify(nameof(HasNotice)); }
    }

    /// <summary>학생 생활 기록 (StudentLog에서 자동 생성)</summary>
    public string Life
    {
        get => _life;
        set { if (SetProperty(ref _life, value ?? string.Empty)) Notify(nameof(HasLifeRecord)); }
    }

    #endregion

    #region Computed Properties

    /// <summary>
    /// 요일
    /// </summary>
    public DayOfWeek DayOfWeek => Date.DayOfWeek;

    /// <summary>
    /// 요일 (한글)
    /// </summary>
    public string DayOfWeekKorean => Date.ToString("ddd");

    /// <summary>
    /// 날짜 표시 (yyyy년 M월 d일 (요일))
    /// </summary>
    public string DateDisplay => $"{Date:yyyy년 M월 d일} ({DayOfWeekKorean})";

    /// <summary>
    /// 출결 문제가 있는지 확인
    /// </summary>
    public bool HasAttendanceIssues =>
        !string.IsNullOrWhiteSpace(Absent) ||
        !string.IsNullOrWhiteSpace(Late) ||
        !string.IsNullOrWhiteSpace(LeaveEarly);

    /// <summary>
    /// 출결 요약
    /// </summary>
    public string AttendanceSummary
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Absent))
                parts.Add($"결석: {Absent}");

            if (!string.IsNullOrWhiteSpace(Late))
                parts.Add($"지각: {Late}");

            if (!string.IsNullOrWhiteSpace(LeaveEarly))
                parts.Add($"조퇴: {LeaveEarly}");

            return parts.Count > 0 ? string.Join(", ", parts) : "출결 이상 없음";
        }
    }

    // 출결 학생 수(AttendanceIssueCount)는 지웠다 — 쉼표로 갈라 센 것이라 "학생 수" 가
    // 아니라 **적힌 토막의 수**였다. 메모칸이니 그 둘은 같지 않다("김하늘·박지민" 처럼
    // 쉼표를 안 쓰면 1, 빈칸이 섞이면 늘어난다). 읽는 곳도 없었다.

    /// <summary>
    /// 알림장 내용이 있는지 확인
    /// </summary>
    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    /// <summary>
    /// 학생 생활 기록이 있는지 확인
    /// </summary>
    public bool HasLifeRecord => !string.IsNullOrWhiteSpace(Life);

    /// <summary>
    /// 메모가 있는지 확인
    /// </summary>
    public bool HasMemo => !string.IsNullOrWhiteSpace(Memo);

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

    #endregion

    #region Methods

    /// <summary>
    /// 기본 생성자
    /// </summary>
    public ClassDiary()
    {
    }

    /// <summary>
    /// 학급과 날짜를 지정하는 생성자
    /// </summary>
    public ClassDiary(string schoolCode, int year, int semester, int grade, int classNum, DateTime date, string teacherId)
    {
        SchoolCode = schoolCode;
        Year = year;
        Semester = semester;
        Grade = grade;
        Class = classNum;
        Date = date;
        TeacherID = teacherId;
    }

    // 비우기 둘(ClearAttendance·ClearAll)은 지웠다 — 부르는 곳이 없었다.
    // 칸을 비우는 일은 화면에서 글자를 지우는 것으로 이미 된다.

    /// <summary>
    /// 유효성 검사
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(SchoolCode) &&
               !string.IsNullOrWhiteSpace(TeacherID) &&
               Year > 0 &&
               Semester > 0 &&
               Grade > 0 &&
               Class > 0 &&
               Date != default;
    }

    // ★ 학생 이름으로 출결을 묻던 셋(HasAttendanceIssue·GetAttendanceStatus·Listed)은
    //   지웠다 — 출결 칸이 **교사가 손으로 적는 메모**로 확정됐기 때문이다(2026-08-30).
    //
    //   이 칸은 학생을 가리키지 않으므로 이름으로 묻는 것 자체가 답할 수 없는 질문이다.
    //   예전에 Contains 로 검사해 부분 일치로 엉뚱한 학생을 잡은 적이 있고
    //   (김하 ⊂ 김하늘, 박지 ⊂ 박지민), Split(',') 으로 고쳐도 **동명이인은 못 막는다**.
    //   고쳐 쓸 수 있는 종류의 문제가 아니었다.
    //
    //   부르는 곳도 없었다(ViewModel 통과 래퍼뿐이고 그 래퍼도 호출 0).
    //   학생별 출결이 필요해지면 학적(Enrollment)을 가리키는 표를 새로 만들 것.

    /// <summary>
    /// 복사본 생성
    /// </summary>
    public ClassDiary Clone()
    {
        return new ClassDiary
        {
            No = this.No,
            SchoolCode = this.SchoolCode,
            TeacherID = this.TeacherID,
            Year = this.Year,
            Semester = this.Semester,
            Date = this.Date,
            Grade = this.Grade,
            Class = this.Class,
            Absent = this.Absent,
            Late = this.Late,
            LeaveEarly = this.LeaveEarly,
            Memo = this.Memo,
            Notice = this.Notice,
            Life = this.Life,
            CreatedAt = this.CreatedAt,
            UpdatedAt = this.UpdatedAt
        };
    }

    /// <summary>
    /// ToString 오버라이드
    /// </summary>
    public override string ToString()
    {
        return $"{Year}학년도 {Semester}학기 {Grade}학년 {Class}반 - {DateDisplay}";
    }

    #endregion
}
