using System;

namespace NewSchool.Helpers;

/// <summary>
/// 작업 학년도·학기(<c>Settings.WorkYear</c>·<c>WorkSemester</c>)가 달력보다 뒤처졌는지 가린다.
///
/// <para>작업 학기는 사용자가 설정에서 직접 바꾸는 값이라 3월·9월이 지나도 그대로 남는다.
/// 그러면 오늘 화면의 시간표가 지난 학기 것으로 뜨고, 새로 쓰는 누가기록이 지난 학년도·학기로
/// 저장된다. 앱을 켤 때와 켜 둔 채 날이 바뀔 때 이것을 물어 알린다(<c>MainWindow</c>).</para>
///
/// <para><b>앞선 것은 묻지 않는다</b> — 2월에 새 학년도를 미리 준비하려고 작업 학년도를 올려 둔
/// 경우다. [그대로 두기] 를 누른 학기도 그 학기 동안은 다시 묻지 않는다.</para>
/// </summary>
public static class WorkTerm
{
    /// <summary>
    /// 작업 학기가 오늘보다 뒤처졌으면 오늘의 학년도·학기를, 아니면 null.
    /// 작업 학년도가 아직 없거나(초기 설정 전) 이 학기에 [그대로 두기] 를 눌렀으면 null.
    /// </summary>
    public static (int Year, int Semester)? BehindToday(
        int workYear, int workSemester, string? dismissedKey, DateTime today)
    {
        if (workYear <= 0) return null;

        int year = DateTimeHelper.SchoolYearOf(today);
        int semester = DateTimeHelper.SemesterOf(today);

        bool behind = workYear < year || (workYear == year && workSemester < semester);
        if (!behind) return null;
        if (dismissedKey == KeyOf(year, semester)) return null;

        return (year, semester);
    }

    /// <summary>[그대로 두기] 를 기록하는 꼴 — <c>Settings.WorkTermNoticeDismissed</c>.</summary>
    public static string KeyOf(int year, int semester) => $"{year}-{semester}";
}
