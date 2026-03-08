using System;

namespace NewSchool.Models;

/// <summary>
/// 수강 신청 정보
/// 학생이 특정 과목을 수강하는 정보 (기존 CourseAssignment 대체)
/// </summary>
public partial class CourseEnrollment : NotifyPropertyChangedBase, IEntity
{
    #region Fields

    private int _no = -1;
    private string _studentId = string.Empty;
    private int _courseNo = -1;
    private string _status = "수강중";
    private string _remark = string.Empty;
    private string _room = string.Empty;
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

    /// <summary>학생 ID (FK: Student.StudentID)</summary>
    public string StudentID
    {
        get => _studentId;
        set => SetProperty(ref _studentId, value);
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

    public override string ToString()
    {
        return $"Student={StudentID}, Course={CourseNo}";
    }

    #endregion
}
