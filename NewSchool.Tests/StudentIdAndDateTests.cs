using System;
using NewSchool;
using NewSchool.Models;
using Xunit;

namespace NewSchool.Tests;

/// <summary>학생 ID 생성 규칙 + 주차 계산 테스트 (TEST_PLAN 3단계).</summary>
public class StudentIdAndDateTests
{
    [Fact]
    public void GenerateStudentID_학교코드7_연도4_일련4_총15자리()
    {
        var id = Student.GenerateStudentID("7530072", 2026, 42);
        Assert.Equal(15, id.Length);
        Assert.Equal("753007220260042", id);
        Assert.StartsWith("7530072", id);
    }

    [Fact]
    public void GenerateStudentID_일련번호_4자리_제로패딩()
    {
        Assert.Equal("753007220260001", Student.GenerateStudentID("7530072", 2026, 1));
        Assert.Equal("753007220269999", Student.GenerateStudentID("7530072", 2026, 9999));
    }

    [Theory]
    [InlineData("753007")]     // 6자리
    [InlineData("75300721")]   // 8자리
    public void GenerateStudentID_학교코드_7자리아니면_예외(string badCode)
        => Assert.Throws<ArgumentException>(() => Student.GenerateStudentID(badCode, 2026, 1));

    [Theory]
    [InlineData(1899)]
    [InlineData(2101)]
    public void GenerateStudentID_연도범위_벗어나면_예외(int year)
        => Assert.Throws<ArgumentException>(() => Student.GenerateStudentID("7530072", year, 1));

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void GenerateStudentID_일련번호_범위밖이면_예외(int seq)
        => Assert.Throws<ArgumentException>(() => Student.GenerateStudentID("7530072", 2026, seq));

    [Fact]
    public void GenerateStudentID_ParseStudentID_왕복()
    {
        var id = Student.GenerateStudentID("7530072", 2026, 42);
        var parsed = new Student { StudentID = id }.ParseStudentID();
        Assert.Equal("7530072", parsed.SchoolCode);
        Assert.Equal(2026, parsed.EnrollmentYear);
        Assert.Equal(42, parsed.Sequence);
    }

    [Theory]
    [InlineData("2026-03-02", "2026-03-02", 1)]  // 시작일 = 1주차
    [InlineData("2026-03-08", "2026-03-02", 1)]  // +6일 = 아직 1주차
    [InlineData("2026-03-09", "2026-03-02", 2)]  // +7일 = 2주차
    [InlineData("2026-03-16", "2026-03-02", 3)]  // +14일 = 3주차
    public void WeekNumber_학기시작일_기준_주차(string date, string start, int expected)
        => Assert.Equal(expected, DateTimeHelper.WeekNumber(DateTime.Parse(date), DateTime.Parse(start)));

    /// <summary>
    /// 1학기 = 3~8월, 2학기 = 9~다음해 2월. 열두 달을 모두 고정한다.
    ///
    /// 예전에 누가기록 입력 상자만 <c>Month &lt;= 6</c> 이라 <b>7·8월과 1·2월에 학기가 뒤집혔다</b>.
    /// 기록 조회는 학기로 거르므로, 그렇게 저장된 기록은 제 학기 목록에서 사라진다.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]   // 겨울 — 2학기(학년말)
    [InlineData(2, 2)]
    [InlineData(3, 1)]   // 1학기 시작
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 1)]
    [InlineData(7, 1)]   // 여름방학도 1학기
    [InlineData(8, 1)]
    [InlineData(9, 2)]   // 2학기 시작
    [InlineData(10, 2)]
    [InlineData(11, 2)]
    [InlineData(12, 2)]
    public void SemesterOf_1학기는_3월부터_8월까지(int month, int expected)
        => Assert.Equal(expected, DateTimeHelper.SemesterOf(new DateTime(2026, month, 15)));

    /// <summary>
    /// 날짜→학기 규칙이 시수 계산의 관례값(<c>DefaultSemesterRange</c>)과 어긋나지 않는지 본다.
    /// 두 규칙이 갈라지면 "학기 안의 날짜인데 다른 학기로 판정되는" 날이 생긴다.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SemesterOf_는_DefaultSemesterRange_와_일치한다(int semester)
    {
        var (start, end) = NewSchool.Services.WeeklyHoursCalculator.DefaultSemesterRange(2026, semester);

        for (var d = start; d <= end; d = d.AddDays(1))
            Assert.Equal(semester, DateTimeHelper.SemesterOf(d));
    }
}
