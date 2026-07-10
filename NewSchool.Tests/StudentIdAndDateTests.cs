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
}
