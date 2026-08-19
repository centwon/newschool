using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NewSchool.Services;

namespace NewSchool.Helpers;

/// <summary>
/// 현재 학교의 성격 중 화면 여러 곳이 되풀이해 묻는 값을 한곳에서 답한다.
///
/// <para>지금은 학년 수 하나뿐이다. 학사일정의 "이 행사에 우리 학년이 빠지나" 판정
/// (<see cref="SchoolCalendar.IsGradeOnlyEvent"/>)이 학교에 학년이 몇 개인지 알아야 하는데,
/// 그 값은 <c>School.SchoolType</c> 에 있고 화면마다 DB 를 다시 뒤지게 두면
/// <see cref="SchoolCalendar"/> 가 경고하는 "규칙을 화면마다 복붙" 이 그대로 재현된다.</para>
///
/// <para>캐시 열쇠를 학교 코드로 둔다 — 설정에서 학교를 바꾸면 코드가 달라져 자동으로 다시 읽는다.
/// 따로 무효화를 호출할 필요가 없다.</para>
/// </summary>
public static class SchoolProfile
{
    private static readonly object _lock = new();
    private static string? _cachedCode;
    private static int _cachedGradeCount;

    /// <summary>
    /// 현재 학교의 학년 수. 학교급을 모르거나(특수학교 등) 조회에 실패하면 <b>0</b>.
    /// 0 은 "모름" 이며, 호출부는 종전 동작으로 물러나야 한다.
    /// </summary>
    public static async Task<int> GetGradeCountAsync()
    {
        string code = Settings.SchoolCode.Value ?? string.Empty;

        lock (_lock)
        {
            if (string.Equals(code, _cachedCode, StringComparison.Ordinal))
                return _cachedGradeCount;
        }

        int gradeCount = 0;
        if (!string.IsNullOrWhiteSpace(code))
        {
            try
            {
                using var service = new SchoolService(SchoolDatabase.DbPath);
                var school = await service.GetSchoolByCodeAsync(code);
                gradeCount = SchoolCalendar.GradeCountFor(school?.SchoolType);
            }
            catch (Exception ex)
            {
                // 못 읽으면 0(모름) 으로 둔다 — 학사일정 판정이 종전 기준으로 돌아갈 뿐이다.
                Debug.WriteLine($"[SchoolProfile] 학교급 조회 실패: {ex.Message}");
            }
        }

        lock (_lock)
        {
            _cachedCode = code;
            _cachedGradeCount = gradeCount;
        }

        return gradeCount;
    }
}
