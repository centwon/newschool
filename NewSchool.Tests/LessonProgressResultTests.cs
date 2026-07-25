using System;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.Tests.Infrastructure;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 진도 갱신 결과 보고 회귀 테스트 — 전수 조사 21차.
///
/// <c>UpdateAsync</c>/<c>MarkAs*</c> 가 결과를 버려서, 갱신된 행이 하나도 없어도
/// 진도 매트릭스는 "N개 단원 완료 처리됨"이라고 알렸다. 이제 실제 반영 여부를 돌려준다.
/// </summary>
public class LessonProgressResultTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fx;

    public LessonProgressResultTests(SqliteTestFixture fx) => _fx = fx;

    [Fact]
    public async Task 없는_행_갱신은_false()
    {
        // FK 부모인 Schedule 테이블은 DatabaseInitializer 가 만든다(21차 1단계).
        // 예전에는 ScheduleRepository 를 먼저 열어야만 통과했다.
        using var repo = new LessonProgressRepository(_fx.DbPath);

        var ghost = new LessonProgress
        {
            No = 999_999,
            CourseSectionId = 1,
            Room = "2-3",
            ProgressType = ProgressType.Normal,
            UpdatedAt = DateTime.Now,
        };

        Assert.False(await repo.UpdateAsync(ghost));
    }

    [Fact]
    public async Task 진도_기록이_없으면_미완료_처리는_false()
    {
        using var repo = new LessonProgressRepository(_fx.DbPath);

        // 해당 (단원, 학급)에 진도 행이 아예 없는 상태 → 되돌릴 것이 없다
        Assert.False(await repo.MarkAsIncompleteAsync(987_654, "9-9"));
    }
}
