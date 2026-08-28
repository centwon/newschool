using System;

namespace NewSchool.Models;

/// <summary>
/// 수강 배정 — 어느 <b>학적</b>이 어느 과목을 듣는가.
///
/// <para>예전에는 <c>StudentID</c> 로 학생을 직접 가리켰다. 그러면 "어느 학년도의 배정인가"
/// 가 관계에 담기지 않아, 전출한 학생이 그 해 수업 명단에 계속 남았다. 이제
/// <see cref="EnrollmentNo"/> 로 학적을 가리키므로 학년도와 재적 여부가 따라온다.</para>
///
/// <para>설계 근거는 <c>docs/enrollment-redesign.md</c> 6장.</para>
/// </summary>
public partial class CourseEnrollment : NotifyPropertyChangedBase
{
    #region Fields

    private int _no = -1;
    private int _enrollmentNo = -1;
    private int _courseNo = -1;
    private string _status = CourseEnrollmentStatus.Active;
    private string _remark = string.Empty;
    private string _room = string.Empty;

    // JOIN 으로 채워지는 값 (컬럼 아님)
    private string _studentId = string.Empty;
    private string _name = string.Empty;
    private bool _isActive = true;

    #endregion

    #region Properties - 기본 정보

    /// <summary>PK (자동 증가)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>학적 번호 (FK: Enrollment.No). 학적이 지워지면 이 배정도 함께 사라진다.</summary>
    public int EnrollmentNo
    {
        get => _enrollmentNo;
        set => SetProperty(ref _enrollmentNo, value);
    }

    /// <summary>
    /// 학생 ID. <b>이 표의 컬럼이 아니다</b> — <c>CourseEnrollmentFull</c> 뷰가 학적을 거쳐
    /// 채워 주는 읽기 전용 값이다. 여기에 넣어도 저장되지 않는다.
    /// </summary>
    public string StudentID
    {
        get => _studentId;
        set => SetProperty(ref _studentId, value);
    }

    /// <summary>학생 이름. <see cref="StudentID"/> 와 같이 JOIN 으로 채워지는 값이다.</summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>그 학적이 지금 명단에 있는가. JOIN 으로 채워지는 값이다.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>개설 과목 번호 (FK: Course.No)</summary>
    public int CourseNo
    {
        get => _courseNo;
        set => SetProperty(ref _courseNo, value);
    }

    #endregion

    #region Properties - 수강 정보

    /// <summary>
    /// 수강 상태
    /// 수강중/수강완료/수강취소
    /// </summary>
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <summary>비고</summary>
    public string Remark
    {
        get => _remark;
        set => SetProperty(ref _remark, value);
    }

    /// <summary>강의실 배정</summary>
    public string Room
    {
        get => _room;
        set => SetProperty(ref _room, value);
    }

    #endregion

    // CreatedAt·UpdatedAt 은 없앴다 — 학적에서와 같은 이유로 읽는 코드가 한 곳도 없었다.

    #region Methods

    public override string ToString()
    {
        return $"Student={StudentID}, Course={CourseNo}";
    }

    #endregion
}
