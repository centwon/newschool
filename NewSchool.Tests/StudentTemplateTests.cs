using System.Collections.Generic;
using System.IO;
using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 학생 명단 템플릿 생성 회귀 테스트 (2026-08-25, 40차).
///
/// 예전에는 <c>Exports\학생명단_템플릿_yyyyMMdd.xlsx</c> 로 날짜만 붙여 썼다.
///   ① 갓 설치한 PC 에는 <c>Exports</c> 폴더가 없어 <c>DirectoryNotFoundException</c>
///   ② 같은 날 두 번째부터는 파일이 이미 있어 <c>IOException</c>
///      (<c>MiniExcel.SaveAs</c> 는 기본이 덮어쓰기 금지)
/// 둘 다 호출부가 삼켜 "템플릿 다운로드가 취소되었습니다" 로 둔갑했다.
/// </summary>
public class StudentTemplateTests
{
    [Fact]
    public void 템플릿을_연달아_만들어도_서로_덮어쓰지_않는다()
    {
        var made = new List<string>();

        try
        {
            for (int i = 0; i < 3; i++)
            {
                var path = ExcelHelper.CreateStudentTemplate();
                Assert.True(File.Exists(path), $"{i + 1}번째 템플릿이 만들어지지 않았다: {path}");
                Assert.DoesNotContain(path, made);   // 같은 경로를 다시 쓰지 않는다
                made.Add(path);
            }
        }
        finally
        {
            foreach (var p in made)
            {
                try { File.Delete(p); } catch { /* 정리 실패 무시 */ }
            }
        }
    }

    [Fact]
    public void 없는_폴더를_지정해도_만들어_준다()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ns_tpl_{System.Guid.NewGuid():N}", "sub");
        var target = Path.Combine(dir, "템플릿.xlsx");

        try
        {
            var path = ExcelHelper.CreateStudentTemplate(target);
            Assert.Equal(target, path);
            Assert.True(File.Exists(target));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(dir)!, true); } catch { /* 정리 실패 무시 */ }
        }
    }
}
