using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Services;

/// <summary>
/// 학교를 떠난 학생에게 기록을 남기려 할 때 알려 주는 판정.
///
/// <para><b>막지 않고 알리기만 한다.</b> 막으면 전출일을 뒤늦게 입력했을 때 이미 적어 둔
/// 기록을 못 고치게 된다 — 실제로 전출은 늦게 입력되는 쪽이 흔하다.</para>
///
/// <para>판정을 여기 한 곳에 모으는 이유는 부르는 화면이 여럿이기 때문이다. 조건을
/// 화면마다 적으면 값이 늘 때 어긋난다. 설계 근거는 <c>docs/enrollment-redesign.md</c>.</para>
/// </summary>
public static class EnrollmentGuard
{
    /// <summary>
    /// "이 학교를 떠났다" 로 보는 변동.
    ///
    /// <para>비활성 전부가 아니라 넷만이다. 휴학·유예·정원외는 학적이 살아 있어 그 사이에도
    /// 기록할 일이 있지만, 이 넷은 더 남길 것이 없다.</para>
    /// </summary>
    private static bool HasLeft(string? changeType) =>
        changeType is EnrollmentChange.TransferredOut
                   or EnrollmentChange.Graduated
                   or EnrollmentChange.Withdrawn
                   or EnrollmentChange.Expelled;

    /// <summary>
    /// 이 학생에게 이 날짜로 기록을 남겨도 되는지 본다.
    /// </summary>
    /// <param name="dbPath">테스트에서 임시 DB 를 넘기기 위한 것. 비우면 실제 DB.</param>
    /// <param name="schoolCode">같은 이유. 비우면 현재 설정된 학교.</param>
    /// <returns>문제가 없으면 <c>null</c>, 있으면 사용자에게 보여 줄 경고 문구.</returns>
    public static async Task<string?> DescribeRecordAfterLeavingAsync(
        string studentId, int year, DateTime recordDate,
        string? dbPath = null, string? schoolCode = null)
    {
        if (string.IsNullOrWhiteSpace(studentId)) return null;

        try
        {
            using var repo = new EnrollmentRepository(dbPath ?? SchoolDatabase.DbPath);

            // 그 학년도 학적을 본다. 없으면(다른 학년도 기록 등) 판단하지 않는다 —
            // 근거가 없는데 경고를 띄우면 사람이 경고를 무시하는 법을 배운다.
            var enrollments = await repo.GetEnrollmentsAsync(
                schoolCode ?? Settings.SchoolCode.Value, year, includeInactive: true);

            var enrollment = enrollments.FirstOrDefault(e => e.StudentID == studentId);
            if (enrollment == null) return null;

            if (!HasLeft(enrollment.ChangeType)) return null;

            var left = ParseDate(enrollment.ChangeDate);
            if (left == null) return null;                  // 날짜를 모르면 기준이 없다
            if (recordDate.Date <= left.Value.Date) return null;

            string who = string.IsNullOrWhiteSpace(enrollment.Name) ? "이 학생" : enrollment.Name;

            return $"{who} 은(는) {left.Value:yyyy-MM-dd} 에 {enrollment.ChangeType}했습니다.\n" +
                   $"그 뒤 날짜({recordDate:yyyy-MM-dd})로 기록을 남기시겠습니까?";
        }
        catch (Exception ex)
        {
            // 판정에 실패하면 조용히 통과시킨다. 기록을 막는 것이 이 함수의 일이 아니다.
            System.Diagnostics.Debug.WriteLine($"[EnrollmentGuard] 판정 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 학적 변동을 저장하기 <b>전에</b> 부른다 — 그 날짜 뒤에 이미 남아 있는 기록을 센다.
    ///
    /// <para>전출은 늦게 입력되는 쪽이 흔해서, 실제로는 이 방향이 더 자주 걸린다.
    /// 세어서 알려 줄 뿐 지우지 않는다 — 그때는 우리 학생이었을 수도 있고,
    /// 날짜를 잘못 넣은 것일 수도 있다. 어느 쪽인지는 사람이 안다.</para>
    /// </summary>
    /// <returns>알릴 것이 없으면 <c>null</c>.</returns>
    public static async Task<string?> DescribeExistingRecordsAfterAsync(
        string studentId, int year, string changeType, string? changeDate,
        string? dbPath = null)
    {
        if (!HasLeft(changeType)) return null;

        var left = ParseDate(changeDate);
        if (left == null) return null;

        try
        {
            using var logRepo = new StudentLogRepository(dbPath ?? SchoolDatabase.DbPath);
            using var specRepo = new StudentSpecialRepository(dbPath ?? SchoolDatabase.DbPath);

            var logs = await logRepo.GetByStudentAsync(studentId, year);
            var specs = await specRepo.GetByStudentAsync(studentId, year);

            // StudentLog.Date 는 DateTime, StudentSpecial.Date 는 문자열이다 — 두 모델의
            // 표현이 달라 각각 비교한다.
            int logCount = logs.Count(l => l.Date.Date > left.Value.Date);
            int specCount = specs.Count(s => IsAfter(s.Date, left.Value));

            if (logCount == 0 && specCount == 0) return null;

            var parts = new System.Collections.Generic.List<string>();
            if (logCount > 0) parts.Add($"누가기록 {logCount}건");
            if (specCount > 0) parts.Add($"학생부 {specCount}건");

            return $"{left.Value:yyyy-MM-dd} 이후에 남은 기록이 있습니다 — {string.Join(" · ", parts)}.\n" +
                   "기록은 지우지 않습니다. 날짜를 잘못 넣었다면 지금 고쳐 주세요.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EnrollmentGuard] 기존 기록 확인 실패: {ex.Message}");
            return null;
        }
    }

    private static bool IsAfter(string? recordDate, DateTime pivot)
    {
        var d = ParseDate(recordDate);
        return d != null && d.Value.Date > pivot.Date;
    }

    /// <summary>"yyyy-MM-dd" → 날짜. 비었거나 형식이 다르면 null.</summary>
    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
                                 DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
