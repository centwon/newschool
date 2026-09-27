using System.Collections.ObjectModel;
using NewSchool.Collections;
using NewSchool.Models;

namespace NewSchool.ViewModels;

/// <summary>
/// 시간표 셀 단위 ViewModel (한 시간의 수업)
/// </summary>
public class TimetableItemViewModel : NotifyPropertyChangedBase
{
    private int _lessonNo;
    private int _courseNo;
    private string _subjectName = string.Empty;
    private string _teacherName = string.Empty;
    private string _room = string.Empty;
    private int _dayOfWeek; // 1=월, 2=화, 3=수, 4=목, 5=금
    private int _period;    // 1~7교시
    private bool _isEmpty = true;

    /// <summary>
    /// Lesson.No (FK)
    /// </summary>
    public int LessonNo
    {
        get => _lessonNo;
        set => SetProperty(ref _lessonNo, value);
    }

    /// <summary>
    /// Course.No
    /// </summary>
    public int CourseNo
    {
        get => _courseNo;
        set => SetProperty(ref _courseNo, value);
    }

    /// <summary>
    /// 과목명 (예: 국어, 수학)
    /// </summary>
    public string SubjectName
    {
        get => _subjectName;
        set => SetProperty(ref _subjectName, value);
    }

    /// <summary>
    /// 교사명
    /// </summary>
    public string TeacherName
    {
        get => _teacherName;
        set => SetProperty(ref _teacherName, value);
    }

    /// <summary>
    /// 교실
    /// </summary>
    public string Room
    {
        get => _room;
        set => SetProperty(ref _room, value);
    }

    /// <summary>
    /// 요일 (1=월, 2=화, 3=수, 4=목, 5=금)
    /// </summary>
    public int DayOfWeek
    {
        get => _dayOfWeek;
        set => SetProperty(ref _dayOfWeek, value);
    }

    /// <summary>
    /// 교시 (1~7)
    /// </summary>
    public int Period
    {
        get => _period;
        set => SetProperty(ref _period, value);
    }

    /// <summary>
    /// 빈 시간 여부
    /// </summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        set => SetProperty(ref _isEmpty, value);
    }

    // 현재 교시 강조(IsCurrentPeriod)와 그날 변경 멤버(ChangeKind·ChangeMemo·ChangeTooltip·
    // HasChange·IsCancelled·ChangeLabel·SubjectWithPrefix)는 지웠다(2026-09-28). 쓰던 곳이
    // 오늘 화면 하나였는데, 그 화면이 LessonSlotBook(DaySlot.Kind)으로 풀게 되면서 남은
    // 학급 시간표(TimetableControl)는 평소 칸만 그린다. 변경 표식은 LessonChangeLabels 에 있다.
    //
    // DisplayText·DayHeader 는 바인딩도 호출도 없어 지웠다(39차).
}

/// <summary>
/// 전체 시간표 ViewModel (월~금 × <see cref="PeriodCounts.MaxSupported"/> 교시)
/// </summary>
public class TimetableViewModel : NotifyPropertyChangedBase
{
    private string _title = string.Empty;
    private int _year;
    private int _semester;
    private OptimizedObservableCollection<TimetableItemViewModel> _items = new();

    /// <summary>
    /// 시간표 제목 (예: "3학년 2반 시간표", "홍길동 교사 시간표")
    /// </summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    /// <summary>
    /// 학년도
    /// </summary>
    public int Year
    {
        get => _year;
        set => SetProperty(ref _year, value);
    }

    /// <summary>
    /// 학기
    /// </summary>
    public int Semester
    {
        get => _semester;
        set => SetProperty(ref _semester, value);
    }

    /// <summary>
    /// 시간표 아이템 목록 (월~금 × <see cref="PeriodCounts.MaxSupported"/> 교시) (최적화됨)
    /// ⚡ OptimizedObservableCollection로 UI 업데이트 80% 향상
    /// </summary>
    public OptimizedObservableCollection<TimetableItemViewModel> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    /// <summary>
    /// 조회가 실패했는가. <b>빈 시간표와 조회 실패를 구분하기 위한 것</b>이다 —
    /// 예전에는 서비스가 예외를 삼키고 빈 시간표를 돌려줘, DB 오류로 아무것도 못 읽어도
    /// 화면에는 "수업이 없습니다"와 똑같이 보였다(호출부의 오류 처리는 영영 실행되지 않았다).
    /// </summary>
    public bool LoadFailed { get; set; }

    /// <summary>조회 실패 사유(사용자 안내용). <see cref="LoadFailed"/> 가 true 일 때만 의미가 있다.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 빈 시간표 초기화 (월~금 × <see cref="PeriodCounts.MaxSupported"/> 교시).
    ///
    /// <para>⚠ 교시 수를 여기 적지 말 것. 예전에는 <b>7</b> 로 박아 두어, 설정에서 8교시로
    /// 올려 배치한 수업이 <see cref="GetItem"/> 에서 <c>null</c> 로 떨어져
    /// <b>조용히 사라졌다</b>(내 시간표·오늘 화면).</para>
    /// </summary>
    public void InitializeEmptyTimetable()
    {
        Items.Clear();

        for (int day = 1; day <= 5; day++) // 월~금
        {
            for (int period = 1; period <= PeriodCounts.MaxSupported; period++)
            {
                Items.Add(new TimetableItemViewModel
                {
                    DayOfWeek = day,
                    Period = period,
                    IsEmpty = true
                });
            }
        }
    }

    /// <summary>
    /// 특정 요일/교시의 아이템 가져오기
    /// </summary>
    public TimetableItemViewModel? GetItem(int dayOfWeek, int period)
    {
        foreach (var item in Items)
        {
            if (item.DayOfWeek == dayOfWeek && item.Period == period)
            {
                return item;
            }
        }
        return null;
    }
}
