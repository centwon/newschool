using NewSchool.Helpers;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// "과목/분야" 표시 규칙 회귀 테스트 — 전수 조사 35차.
///
/// 화면 목록에만 이 규칙이 있고 인쇄(PDF 2종)·엑셀·HTML 은 <c>SubjectName</c> 만 찍었다.
/// 그래서 <b>진로활동 희망분야가 출력물에서 통째로 빠졌고</b> — 바이트는 희망분야까지
/// 합산되므로 숫자가 안 맞아 보였다 — 교과활동 학기 표기도 사라졌다.
/// 이제 <see cref="NeisHelper.BuildSubjectDisplay"/> 한 곳이 기준이다.
/// </summary>
public class SubjectDisplayTests
{
    [Fact]
    public void 진로활동은_희망분야를_보여준다()
    {
        // 진로활동은 Title 이 분량에 합산되는 영역 — 내용과 함께 보여야 한다
        Assert.Equal("컴퓨터공학",
            NeisHelper.BuildSubjectDisplay("진로활동", subjectName: "", title: "컴퓨터공학", semester: 0));
    }

    [Fact]
    public void 진로활동은_과목명이_있어도_희망분야가_이긴다()
    {
        Assert.Equal("의학",
            NeisHelper.BuildSubjectDisplay("진로활동", subjectName: "생명과학", title: "의학", semester: 0));
    }

    [Theory]
    [InlineData(1, "국어 (1학기)")]
    [InlineData(2, "국어 (2학기)")]
    public void 교과활동은_과목명에_학기를_붙인다(int semester, string expected)
    {
        Assert.Equal(expected,
            NeisHelper.BuildSubjectDisplay("교과활동", "국어", title: "국어", semester: semester));
    }

    [Fact]
    public void 교과활동이라도_학년단위_0이면_학기를_안_붙인다()
    {
        Assert.Equal("국어",
            NeisHelper.BuildSubjectDisplay("교과활동", "국어", title: "국어", semester: 0));
    }

    [Fact]
    public void 개인별세특은_교과영역이지만_학기를_안_붙인다()
    {
        // 학년 단위로 확정된 영역 — 학기 값이 들어와도 무시해야 한다
        Assert.Equal("수학",
            NeisHelper.BuildSubjectDisplay("개인별세특", "수학", title: "수학", semester: 2));
    }

    [Theory]
    [InlineData("자율활동")]
    [InlineData("동아리활동")]
    [InlineData("봉사활동")]
    [InlineData("행동특성")]
    public void 나머지_영역은_빈칸이다(string type)
    {
        Assert.Equal(string.Empty,
            NeisHelper.BuildSubjectDisplay(type, subjectName: "", title: "무언가", semester: 0));
    }

    [Fact]
    public void 과목명이_비었고_학기만_있으면_학기만_보여준다()
    {
        Assert.Equal("2학기",
            NeisHelper.BuildSubjectDisplay("교과활동", subjectName: "", title: "", semester: 2));
    }

    [Fact]
    public void null_은_빈칸으로_다룬다()
    {
        Assert.Equal(string.Empty,
            NeisHelper.BuildSubjectDisplay("진로활동", subjectName: null, title: null, semester: 0));
        Assert.Equal(string.Empty,
            NeisHelper.BuildSubjectDisplay("교과활동", subjectName: null, title: null, semester: 0));
    }
}

/// <summary>
/// 파일명 정리 회귀 테스트 — 전수 조사 35차.
///
/// 학생 이름·과목명은 사용자가 직접 입력하는 값인데 그대로 파일명에 들어갔다.
/// "국어/문학" 같은 과목명이면 Path.Combine 이 엉뚱한 경로를 만들어
/// 내보내기가 이유를 알 수 없는 오류로 실패했다.
/// </summary>
public class FileNameSanitizeTests
{
    [Theory]
    [InlineData("국어/문학", "국어_문학")]
    [InlineData("수학:심화", "수학_심화")]
    [InlineData("과학?탐구", "과학_탐구")]
    [InlineData(@"영어\회화", "영어_회화")]
    [InlineData("사회*일반", "사회_일반")]
    public void 경로_문자를_바꾼다(string input, string expected)
        => Assert.Equal(expected, NewSchool.Helpers.FileNameHelper.Sanitize(input));

    [Fact]
    public void 멀쩡한_이름은_그대로_둔다()
        => Assert.Equal("홍길동", NewSchool.Helpers.FileNameHelper.Sanitize("홍길동"));

    [Fact]
    public void 끝의_점과_공백은_잘라낸다()
    {
        // 윈도우가 어차피 잘라내므로 미리 맞춘다
        Assert.Equal("홍길동", NewSchool.Helpers.FileNameHelper.Sanitize("홍길동. "));
    }

    [Fact]
    public void null_과_빈값은_빈_문자열()
    {
        Assert.Equal(string.Empty, NewSchool.Helpers.FileNameHelper.Sanitize(null));
        Assert.Equal(string.Empty, NewSchool.Helpers.FileNameHelper.Sanitize(""));
    }
}
