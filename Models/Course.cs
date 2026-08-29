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
public class Course : NotifyPropertyChangedBase
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
    private string _type = CourseTypes.Class;
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
        set { if (SetProperty(ref _grade, value)) Notify(nameof(DisplayName)); }
    }

    /// <summary>과목명 (예: "국어", "수학")</summary>
    public string Subject
    {
        get => _subject;
        set { if (SetProperty(ref _subject, value)) Notify(nameof(DisplayName)); }
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
        set
        {
            if (SetProperty(ref _type, value))
                Notify(nameof(EffectiveType), nameof(IsClassType), nameof(TypeDisplay));
        }
    }

    /// <summary>
    /// 강의실 목록 (쉼표로 구분)
    /// 예: "음악실", "음악실,미술실", "과학실1,과학실2"
    /// LessonLog나 StudentLog에서 개별 Room으로 선택하여 사용
    /// </summary>
    public string Rooms
    {
        get => _rooms;
        set
        {
            if (SetProperty(ref _rooms, value))
                Notify(nameof(RoomList), nameof(RoomListDisplay));
        }
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
    public string EffectiveType => string.IsNullOrEmpty(Type) ? CourseTypes.Class : Type;

    /// <summary>
    /// 학급 공통 수업 여부 (Class 또는 빈값)
    /// </summary>
    public bool IsClassType => EffectiveType == CourseTypes.Class;

    // 유형 판정 넷(IsSelectiveType·IsClubType·RequiresAutoEnrollment·RequiresManualEnrollment)은
    // 읽는 곳이 없어 지웠다(39차). 화면은 IsClassType 과 TypeDisplay 만 쓴다.

    /// <summary>
    /// 수업 유형 표시명
    /// </summary>
    public string TypeDisplay => EffectiveType switch
    {
        CourseTypes.Class => "학급 공통",
        CourseTypes.Selective => "선택 과목",
        CourseTypes.Club => "동아리",
        _ => EffectiveType
    };

    /// <summary>
    /// 강의실 목록 파싱
    /// "음악실" → ["음악실"]
    /// "음악실,미술실" → ["음악실", "미술실"]
    /// "과학실1,과학실2" → ["과학실1", "과학실2"]
    /// </summary>
    public List<string> RoomList => ParseRooms(Rooms);

    /// <summary>
    /// <see cref="RoomList"/> 의 규칙을 <c>Course</c> 인스턴스 없이 쓰는 자리.
    ///
    /// <para>미리보기와 초기화 판정이 "고치는 중인 텍스트" 를 다뤄야 하는데, 그때마다
    /// <c>new Course { Rooms = … }.RoomList</c> 를 만들면 규칙이 두 벌로 갈릴 길이 생긴다.</para>
    /// </summary>
    public static List<string> ParseRooms(string? rooms)
    {
        if (string.IsNullOrWhiteSpace(rooms))
            return new List<string>();

        return rooms
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrEmpty(r))
            .ToList();
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
