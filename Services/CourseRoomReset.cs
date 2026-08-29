using System;
using System.Threading.Tasks;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 강의실 목록을 다시 정할 때 딸린 기록을 세고 지운다.
///
/// <para><b>왜 초기화인가.</b> <c>Course.Rooms</c> 는 교사가 치는 자유 텍스트인데, 그 조각
/// 하나가 네 표의 키 노릇을 한다(<c>Lesson.Room</c>·<c>CourseWeeklyHours.Room</c>·
/// <c>LessonProgress.Room</c>·<c>CourseEnrollment.Room</c>). DB 에는 이 넷을 <c>Course.Rooms</c>
/// 와 잇는 제약이 하나도 없다 — 문자열이 우연히 같아서 이어져 있을 뿐이다. 그래서 이름을
/// 고치면 아무 경고 없이 갈라졌고, <c>UNIQUE</c> 키에 <c>Room</c> 이 든 두 표는 UPDATE 가
/// 아니라 <b>새 행</b>을 만들어 두 세대가 쌓였다.</para>
///
/// <para><b>정한 규칙(2026-08-29): 강의실은 정하고 나면 바뀌지 않는 것이 기본이다.</b>
/// 바꿔야 하면 그것은 이름 바꾸기가 아니라 <b>초기화</b>다 — 무엇이 지워지는지 세어서 알리고,
/// 사람이 확인하면 한 번에 지운다. 이러면 고아 행이 생길 길이 없어져
/// <c>CourseRoom</c> 같은 표를 따로 두지 않아도 된다.</para>
///
/// <para><b>무엇을 지우고 무엇을 살리는가.</b> 앞의 셋은 강의실이 정해져야 뜻이 생기는
/// 파생 기록이라 지운다. 특히 <c>Lesson</c> 은 <c>Room</c> 이 <b>그 칸이 어느 학급 수업인지를
/// 말하는 유일한 값</b>이다(격자는 요일·교시당 한 칸이고 학년·반 열은 채우는 코드가 없어
/// 걷어냈다). 이름이 뜻을 잃으면 칸도 뜻을 잃으므로 함께 지운다 — 한 수업치라 다시 놓기 싸다.</para>
///
/// <para>반면 <c>CourseEnrollment</c> 는 <b>학생 명단</b>이고 <c>Room</c> 은
/// <c>UNIQUE(EnrollmentNo, CourseNo)</c> 에 들어 있지 않은 속성이다. 그래서 행은 살리고
/// 강의실 지정만 비운다.</para>
/// </summary>
public static class CourseRoomReset
{
    /// <summary>강의실을 다시 정하면 무슨 일이 벌어지는지 — 사람에게 보여 줄 숫자.</summary>
    public readonly record struct Impact(
        int Lessons,
        int WeeklyHours,
        int Progress,
        int Enrollments)
    {
        /// <summary>지워지거나 비워질 것이 하나라도 있는가. 없으면 잠글 이유도 없다.</summary>
        public bool HasAny => Lessons > 0 || WeeklyHours > 0 || Progress > 0 || Enrollments > 0;

        /// <summary>지워지는 것만 — 배정은 살아남으므로 여기 세지 않는다.</summary>
        public int Deleted => Lessons + WeeklyHours + Progress;
    }

    /// <summary>
    /// 이 수업의 강의실을 다시 정하면 무엇이 얼마나 걸리는지 센다. 아무것도 지우지 않는다.
    /// </summary>
    public static async Task<Impact> MeasureAsync(string dbPath, int courseNo)
    {
        if (courseNo <= 0) return default;

        using var lessonRepo = new LessonRepository(dbPath);
        using var hoursRepo = new CourseWeeklyHoursRepository(dbPath);
        using var progressRepo = new LessonProgressRepository(dbPath);
        using var enrollRepo = new CourseEnrollmentRepository(dbPath);

        var lessons = await lessonRepo.GetByCourseAsync(courseNo);
        var hours = await hoursRepo.GetByCourseAsync(courseNo);
        var progress = await progressRepo.GetByCourseAsync(courseNo);
        var enrollments = await enrollRepo.GetByCourseAsync(courseNo);

        int roomed = 0;
        foreach (var e in enrollments)
            if (!string.IsNullOrEmpty(e.Room)) roomed++;

        return new Impact(lessons.Count, hours.Count, progress.Count, roomed);
    }

    /// <summary>
    /// 딸린 기록을 실제로 정리한다. <b>사람이 확인한 뒤에만</b> 부를 것.
    ///
    /// <para>지우는 차례는 자식부터다 — 진도는 <c>CourseSection</c> 을 거쳐 수업에 닿으므로
    /// 단원보다 먼저 지운다(여기서 단원 자체는 건드리지 않는다. 단원은 강의실과 무관하다).</para>
    /// </summary>
    public static async Task<Impact> ExecuteAsync(string dbPath, int courseNo)
    {
        if (courseNo <= 0) return default;

        var measured = await MeasureAsync(dbPath, courseNo);

        using (var progressRepo = new LessonProgressRepository(dbPath))
            await progressRepo.DeleteByCourseAsync(courseNo);

        using (var hoursRepo = new CourseWeeklyHoursRepository(dbPath))
            await hoursRepo.DeleteByCourseAsync(courseNo);

        using (var lessonRepo = new LessonRepository(dbPath))
            await lessonRepo.DeleteByCourseAsync(courseNo);

        // 배정은 지우지 않는다 — 강의실 지정만 비운다.
        using (var enrollRepo = new CourseEnrollmentRepository(dbPath))
            await enrollRepo.ClearRoomsByCourseAsync(courseNo);

        return measured;
    }

    /// <summary>
    /// 두 강의실 목록이 <b>같은 것</b>인가. 순서와 앞뒤 공백은 무시한다 —
    /// <c>"1-1,1-2"</c> 와 <c>"1-2, 1-1"</c> 은 같은 목록이므로 초기화할 이유가 없다.
    /// </summary>
    public static bool SameRooms(string? a, string? b)
    {
        var left = Models.Course.ParseRooms(a);
        var right = Models.Course.ParseRooms(b);

        if (left.Count != right.Count) return false;

        left.Sort(StringComparer.Ordinal);
        right.Sort(StringComparer.Ordinal);

        for (int i = 0; i < left.Count; i++)
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) return false;

        return true;
    }
}
