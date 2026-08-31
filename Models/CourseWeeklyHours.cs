using System;

namespace NewSchool.Models;

/// <summary>
/// 주차별 시수 조정 — <b>교사가 손으로 고친 칸만</b> 저장한다.
///
/// 자동 계산값은 저장하지 않는다. 시간표 배치나 학사일정이 바뀌면 저장해 둔 자동값은
/// 그 순간 거짓이 되기 때문에, 화면을 열 때마다 다시 계산하고 이 표는 덮어쓰기로만 쓴다.
///
/// 단위가 (수업, <b>학급</b>, 주차) 인 이유 — 같은 과목이라도 학급마다 시간표가 다르고
/// 학급 행사로 빠지는 날도 다르다. 옛 시수 관리도 학급별 열을 두고 각각 고칠 수 있었다.
///
/// ⚠ 예전 <c>WeeklyLessonHours</c> 는 연간 수업 계획(SubjectYearPlan)에 묶여 있었고,
/// 연간계획이 사라지면서 함께 없어졌다. 되살리면서 부모를 <c>Course</c> 로 바꿨다.
/// </summary>
public class CourseWeeklyHours
{
    /// <summary>PK</summary>
    public int No { get; set; } = -1;

    /// <summary>수업 번호 (FK: Course.No)</summary>
    public int CourseNo { get; set; }

    /// <summary>학급/강의실 (Lesson.Room)</summary>
    public string Room { get; set; } = string.Empty;

    /// <summary>
    /// 주차 (1, 2, 3, ...). <b>표시용이다</b> — 이 값으로 조정을 찾지 말 것.
    /// 학기 구간이 바뀌면 다시 세어진다(<see cref="WeekStart"/> 참고).
    /// </summary>
    public int Week { get; set; }

    /// <summary>
    /// 주 시작일 (월요일). <b>한 주를 가리키는 키다.</b>
    ///
    /// <para>예전에는 주차 번호가 키였고 이 값은 참고로만 저장했다. 그런데 2학기를
    /// 관례값(9/1 시작)으로 보다가 학사일정을 내려받으면 학기 시작이 여름방학 다음
    /// 첫 수업일로 당겨져 <b>번호가 통째로 밀린다</b> — 손으로 고친 시수가 다른 주에
    /// 가서 붙었다. 날짜는 그렇게 움직이지 않는다.</para>
    /// </summary>
    public DateTime WeekStart { get; set; }

    /// <summary>교사가 정한 시수</summary>
    public int PlannedHours { get; set; }
}
