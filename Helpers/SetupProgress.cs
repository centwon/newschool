using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NewSchool.Repositories;
using NewSchool.Services;

namespace NewSchool.Helpers;

/// <summary>
/// "초기 설정은 끝났는데 그 다음은?" 을 판정한다.
///
/// <para>초기 설정 창은 학교·사용자·학년도까지만 받는다. 정작 프로그램을 쓰려면 학생을
/// 넣고 수업을 개설해야 하는데, 그 두 가지가 비어 있어도 학급·수업 화면들은 그냥 빈
/// 목록만 보여 줬다. 빈 화면에 다음 할 일을 적어 주려면 먼저 "정말 하나도 없는지"를
/// 물어야 한다 — 그 물음을 화면마다 다르게 세지 않도록 여기 모은다.</para>
/// </summary>
public static class SetupProgress
{
    /// <summary>
    /// 학생이 한 명이라도 등록되어 있는지. 학년도·학급과 무관하게 <c>Student</c> 전체를 센다
    /// — 화면의 필터가 비어 있는 것과 "학생을 아직 한 명도 안 넣은 것" 은 다른 이야기라서다.
    /// </summary>
    /// <remarks>DB 를 읽지 못하면 <c>true</c> 를 돌려준다 — 안내판을 잘못 띄워
    /// 멀쩡한 화면을 가리는 쪽이 안 띄우는 쪽보다 나쁘다.</remarks>
    public static async Task<bool> HasAnyStudentAsync()
    {
        try
        {
            var repository = new StudentRepository(SchoolDatabase.DbPath);
            return await repository.GetCountAsync() > 0;
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Warning("SetupProgress", $"학생 수를 세지 못해 '있음' 으로 본다(첫 실행 안내 생략): {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// 현재 사용자의 이번 학년도·학기 수업이 하나라도 개설되어 있는지.
    /// (학생과 달리 수업은 "내 수업" 이 기준이다 — 화면들이 모두 그렇게 읽는다.)
    /// </summary>
    /// <remarks>실패 시 <c>true</c> 인 이유는 <see cref="HasAnyStudentAsync"/> 와 같다.</remarks>
    public static async Task<bool> HasAnyCourseAsync()
    {
        try
        {
            using var service = new CourseService(SchoolDatabase.DbPath);
            var courses = await service.GetMyCoursesAsync();
            return courses.Count > 0;
        }
        catch (Exception ex)
        {
            NewSchool.Logging.Log.Warning("SetupProgress", $"수업 수를 세지 못해 '있음' 으로 본다(첫 실행 안내 생략): {ex.Message}");
            return true;
        }
    }
}
