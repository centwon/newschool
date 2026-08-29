using System;
using System.Data;
using System.Reflection;
using Microsoft.Data.Sqlite;
using NewSchool.Models;
using NewSchool.ViewModels;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// <b>ViewModel 을 만드는 것만으로 DB 연결이 열려서는 안 된다.</b>
///
/// <para><c>BaseRepository</c> 는 <b>생성자에서</b> <c>Connection.Open()</c> 을 한다. 그래서
/// ViewModel 이 서비스를 미리 만들면, 그 ViewModel 을 하나 만들 때마다 SQLite 연결이 하나씩
/// 열린다. 목록·내보내기 경로는 ViewModel 을 <b>사람 수만큼 루프로</b> 만들고, 그 경로들은
/// 이미 읽어 둔 모델을 넣어 주므로 서비스를 <b>쓰지도 않는다</b>.</para>
///
/// <para>2026-08-30 이전에는 실제로 그랬다 —
/// <list type="bullet">
///   <item><c>ClassDiaryListWin</c>: 일지 <b>줄마다</b> 연결 1개, 새로고침할 때마다 누적</item>
///   <item><c>StudentCardPrintService.LoadClassStudentsAsync</c> 등: 학생마다 연결 <b>3개</b>
///         (Student·StudentDetail·Enrollment). 30명이면 90개</item>
/// </list>
/// 어느 쪽도 Dispose 되지 않아 풀로 돌아가지 못하고 GC 를 기다렸다.</para>
///
/// <para>고침은 서비스를 <b>지연 생성</b>으로 바꾼 것이다. 그 성질은 눈에 보이는 증상이 없어서
/// (연결이 새도 화면은 멀쩡하다) 되돌아가기 쉽다. 그래서 여기서 못박는다.</para>
/// </summary>
public class ViewModelNoEagerDbTests
{
    /// <summary>
    /// <paramref name="create"/> 를 실행하는 동안 <b>SQLite 연결이 열리는지</b> 본다.
    ///
    /// <para>연결 수를 직접 셀 방법이 없어, ViewModel 안의 서비스 필드가 만들어졌는지로
    /// 대신 본다 — 그 필드가 만들어지는 순간이 곧 연결이 열리는 순간이다
    /// (<c>BaseRepository</c> 생성자).</para>
    /// </summary>
    private static void AssertNoServiceCreated(object viewModel, params string[] fieldNames)
    {
        var type = viewModel.GetType();
        foreach (var name in fieldNames)
        {
            var f = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(f != null, $"필드 {name} 이 없다 — 이름이 바뀌었으면 이 테스트도 함께 고칠 것");
            Assert.True(f!.GetValue(viewModel) == null,
                $"{type.Name} 을 만들기만 했는데 {name} 이 이미 만들어졌다. " +
                "BaseRepository 는 생성자에서 연결을 열므로, 목록·내보내기가 사람 수만큼 " +
                "연결을 열게 된다. 지연 생성으로 되돌릴 것.");
        }
    }

    [Fact]
    public void ClassDiaryViewModel_은_만들기만_해서는_서비스를_만들지_않는다()
    {
        AssertNoServiceCreated(new ClassDiaryViewModel(), "_diaryService");
        AssertNoServiceCreated(new ClassDiaryViewModel(new ClassDiary()), "_diaryService");
    }

    [Fact]
    public void StudentCardViewModel_은_만들기만_해서는_서비스를_만들지_않는다()
    {
        AssertNoServiceCreated(
            new StudentCardViewModel(),
            "_studentService", "_studentDetailService", "_enrollmentService", "_photoService");
    }

    /// <summary>
    /// 내보내기·인쇄가 쓰는 길(<c>LoadFromModels</c>)은 이미 읽어 둔 모델을 넣는다 —
    /// 그 길에서도 서비스가 만들어지면 안 된다. 이게 학급 인원수만큼 도는 자리다.
    /// </summary>
    [Fact]
    public void 미리_읽은_모델을_넣는_길은_DB_를_건드리지_않는다()
    {
        var vm = new StudentCardViewModel();
        vm.LoadFromModels(
            new Enrollment { StudentID = "T1", Name = "홍길동", Grade = 2, Class = 3, Number = 15 },
            new Student { StudentID = "T1", Name = "홍길동" },
            new StudentDetail { StudentID = "T1" });

        Assert.Equal("홍길동", vm.Name);
        AssertNoServiceCreated(
            vm, "_studentService", "_studentDetailService", "_enrollmentService", "_photoService");
    }

    /// <summary>
    /// 한 번도 쓰지 않은 ViewModel 을 Dispose 해도 터지지 않아야 한다 —
    /// 목록이 줄을 버릴 때 그대로 부르는 길이다.
    /// </summary>
    [Fact]
    public void 쓰지_않은_ViewModel_을_Dispose_해도_안전하다()
    {
        new ClassDiaryViewModel(new ClassDiary()).Dispose();
        new StudentCardViewModel().Dispose();
    }
}
