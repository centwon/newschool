using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace NewSchool.Models;

/// <summary>
/// 수업 개설 정보
/// 특정 학년도/학기에 개설되는 수업 정보 관리
/// ⭐ 재설계: 시간표 정보는 CourseSchedule로 분리
/// </summary>
[Microsoft.UI.Xaml.Data.Bindable]
public partial class Course : NotifyPropertyChangedBase, IEntity, IYearSemesterEntity
{
    #region Fields

    private int _no = -1;
    private string _schoolCode = string.Empty;
    private string _teacherId = string.Empty;
    private int _year = DateTime.Today.Year;
    private int _semester = 1;
    private int _grade = 1;
    private string _subject = string.Empty;
    private int _unit = 0;
    private string _type = "Class";
    private string _rooms = string.Empty;
    private string _remark = string.Empty;

    #endregion

    #region Properties - 기본 정보

    /// <summary>PK (자동 증가)</summary>
    public int No
    {
        get => _no;
        set => SetProperty(ref _no, value);
    }

    /// <summary>학교 코드 (FK: School.SchoolCode)</summary>
    public string SchoolCode
    {
        get => _schoolCode;
        set => SetProperty(ref _schoolCode, value);
    }

    /// <summary>담당 교사 ID (FK: Teacher.TeacherID)</summary>
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

    #endregion

    #region Properties - 수업 정보

    /// <summary>대상 학년 (1, 2, 3)</summary>
    public int Grade
    {
        get => _grade;
        set => SetProperty(ref _grade, value);
    }

    /// <summary>과목명 (예: "국어", "수학")</summary>
    public string Subject
    {
        get => _subject;
        set => SetProperty(ref _subject, value);
    }

    /// <summary>주당 시수</summary>
    public int Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value);
    }

    /// <summary>
    /// 수업 유형
    /// Class(학급 공통), Selective(선택), Club(동아리)
    /// </summary>
    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    /// <summary>
    /// 강의실 목록 (쉼표로 구분)
    /// 예: "음악실", "음악실,미술실", "과학실1,과학실2"
    /// LessonLog나 StudentLog에서 개별 Room으로 선택하여 사용
    /// </summary>
    public string Rooms
    {
        get => _rooms;
        set => SetProperty(ref _rooms, value);
    }

    #endregion

    #region Properties - 기타

    /// <summary>비고</summary>
    public string Remark
    {
        get => _remark;
        set => SetProperty(ref _remark, value);
    }

    #endregion

    #region Computed Properties

    /// <summary>
    /// 유효한 수업 유형 (빈값은 "Class"로 간주)
    /// </summary>
    public string EffectiveType => string.IsNullOrEmpty(Type) ? "Class" : Type;

    /// <summary>
    /// 학급 공통 수업 여부 (Class 또는 빈값)
    /// </summary>
    public bool IsClassType => EffectiveType == "Class";

    /// <summary>
    /// 선택 과목 여부
    /// </summary>
    public bool IsSelectiveType => EffectiveType == "Selective";

    /// <summary>
    /// 동아리 여부
    /// </summary>
    public bool IsClubType => EffectiveType == "Club";

    /// <summary>
    /// 수강생 자동 등록 대상 여부
    /// Class 유형은 학급 학생 전체 자동 등록
    /// </summary>
    public bool RequiresAutoEnrollment => IsClassType;

    /// <summary>
    /// 수강생 수동 등록 대상 여부
    /// Selective, Club 유형은 CourseAssignment로 수동 등록
    /// </summary>
    public bool RequiresManualEnrollment => !IsClassType;

    /// <summary>
    /// 수업 유형 표시명
    /// </summary>
    public string TypeDisplay => EffectiveType switch
    {
        "Class" => "학급 공통",
        "Selective" => "선택 과목",
        "Club" => "동아리",
        _ => EffectiveType
    };

    /// <summary>
    /// 강의실 목록 파싱
    /// "음악실" → ["음악실"]
    /// "음악실,미술실" → ["음악실", "미술실"]
    /// "과학실1,과학실2" → ["과학실1", "과학실2"]
    /// </summary>
    public List<string> RoomList
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Rooms))
                return new List<string>();

            return Rooms
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(r => r.Trim())
                .Where(r => !string.IsNullOrEmpty(r))
                .ToList();
        }
    }

    /// <summary>
    /// 강의실 목록 문자열 (표시용)
    /// </summary>
    public string RoomListDisplay => string.Join(", ", RoomList);

    /// <summary>
    /// 표시용 이름 (ComboBox 등에서 사용)
    /// </summary>
    public string DisplayName => $"{Grade}학년 {Subject}";

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{Subject} ({Grade}학년)";
    }

    #endregion
}
