using System.IO;
using NewSchool.Models;
using NewSchool.Services;
using NewSchool.Tests.Infrastructure;
using NewSchool.ViewModels;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 학생카드 미저장 편집 플래그(IsChanged) 회귀 테스트 — 전수 조사 21차.
///
/// 예전 <c>OnModelPropertyChanged</c> 는 어떤 모델이 바뀌든 IsChanged 를 true 로 올린 뒤,
/// 읽기 전용인 Enrollment 분기에서 다시 false 로 되돌렸다. 그래서 학생 정보를 편집하는
/// 도중 학적(학년·반·번호) 변경 알림이 한 번이라도 끼면 미저장 편집이 통째로 사라졌다.
/// </summary>
public class StudentCardDirtyFlagTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fx;

    public StudentCardDirtyFlagTests(SqliteTestFixture fx) => _fx = fx;

    private StudentCardViewModel NewViewModel() => new(
        new StudentService(_fx.DbPath),
        new StudentDetailService(_fx.DbPath),
        new EnrollmentService(_fx.DbPath),
        new PhotoService(Path.Combine(Path.GetTempPath(), "NewSchoolTests", "photos")));

    private static Enrollment NewEnrollment(string studentId) => new()
    {
        StudentID = studentId,
        SchoolCode = TestData.SchoolCode,
        Year = TestData.Year,
        Grade = 2,
        Class = 3,
        Number = 15,
        Name = "홍길동",
        Sex = "남",
    };

    [Fact]
    public void 학적_변경은_미저장_편집_플래그를_지우지_않는다()
    {
        var vm = NewViewModel();
        var enrollment = NewEnrollment(TestData.NewStudentId());
        vm.LoadFromEnrollment(enrollment);
        Assert.False(vm.IsChanged);

        // 사용자가 학생 정보를 편집
        vm.Student!.Phone = "010-1234-5678";
        Assert.True(vm.IsChanged);

        // 학적(읽기 전용) 갱신이 끼어들어도 편집 플래그는 살아 있어야 한다
        enrollment.Number = 16;

        Assert.True(vm.IsChanged);
        Assert.Equal("2학년 3반 16번", vm.ClassInfo);
    }

    [Fact]
    public void 학적_변경만으로는_편집_플래그가_서지_않는다()
    {
        var vm = NewViewModel();
        var enrollment = NewEnrollment(TestData.NewStudentId());
        vm.LoadFromEnrollment(enrollment);

        enrollment.Class = 5;

        Assert.False(vm.IsChanged);
    }

    [Fact]
    public void 학생_정보_편집은_편집_플래그를_세운다()
    {
        var vm = NewViewModel();
        vm.LoadFromEnrollment(NewEnrollment(TestData.NewStudentId()));

        vm.Student!.Memo = "메모";

        Assert.True(vm.IsChanged);
    }
}
