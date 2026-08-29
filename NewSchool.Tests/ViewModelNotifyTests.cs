using System.Collections.Generic;
using System.ComponentModel;
using NewSchool.Models;
using NewSchool.ViewModels;
using Xunit;

namespace NewSchool.Tests;

/// <summary>
/// 뷰모델이 <see cref="NotifyPropertyChangedBase"/> 로 옮겨 간 뒤에도 <b>알림이 그대로
/// 나가는가</b>(2026-08-30).
///
/// <para>열 개 뷰모델 중 여덟이 <c>PropertyChanged</c>·<c>OnPropertyChanged</c>·
/// <c>SetProperty</c> 를 저마다 손으로 들고 있었다. 세 벌은 기반 클래스와 글자 그대로
/// 같았고 나머지도 같은 일을 했다. 그걸 기반 클래스 하나로 모았다.</para>
///
/// <para>이 종류의 정리는 <b>깨져도 빌드가 통과한다</b> — 상속만 바꾸면 컴파일은 되고,
/// 알림이 안 나가는 것은 화면에서 값이 안 바뀌는 모습으로만 드러난다. 그래서 대표 몇 개로
/// 실제 이벤트가 오는지 확인한다.</para>
/// </summary>
public class ViewModelNotifyTests
{
    private static List<string> Capture(INotifyPropertyChanged vm, System.Action mutate)
    {
        var seen = new List<string>();
        void H(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null) seen.Add(e.PropertyName);
        }

        vm.PropertyChanged += H;
        try { mutate(); }
        finally { vm.PropertyChanged -= H; }
        return seen;
    }

    [Fact]
    public void ClassDiaryViewModel_이_알림을_낸다()
    {
        var vm = new ClassDiaryViewModel(new ClassDiary());

        var seen = Capture(vm, () => vm.Absent = "김하늘");

        Assert.Contains(nameof(ClassDiaryViewModel.Absent), seen);
        Assert.Contains(nameof(ClassDiaryViewModel.AttendanceSummary), seen);
        Assert.Contains(nameof(ClassDiaryViewModel.HasAttendanceIssues), seen);
    }

    [Fact]
    public void ClassDiaryViewModel_은_같은_값에_알리지_않는다()
    {
        var vm = new ClassDiaryViewModel(new ClassDiary()) { Memo = "메모" };

        Assert.Empty(Capture(vm, () => vm.Memo = "메모"));
    }

    [Fact]
    public void StudentCardViewModel_이_알림을_낸다()
    {
        var vm = new StudentCardViewModel();

        var seen = Capture(vm, () => vm.IsLoading = true);

        Assert.Contains(nameof(StudentCardViewModel.IsLoading), seen);
    }

    /// <summary>
    /// <c>StudentCardViewModel</c> 은 감싼 모델의 변경도 흘려보낸다 — 열 개 중 유일하게
    /// 모델의 <c>PropertyChanged</c> 를 구독하는 뷰모델이라, 상속을 바꿀 때 그 사슬이
    /// 끊기지 않았는지 함께 본다.
    /// </summary>
    [Fact]
    public void StudentCardViewModel_은_모델_변경도_흘려보낸다()
    {
        var vm = new StudentCardViewModel();
        vm.LoadFromModels(
            new Enrollment { StudentID = "T1", Name = "홍길동", Grade = 2, Class = 3, Number = 15 },
            new Student { StudentID = "T1", Name = "홍길동" },
            new StudentDetail { StudentID = "T1" });

        var seen = Capture(vm, () => vm.Student!.Name = "김하늘");

        Assert.NotEmpty(seen);
    }

    [Fact]
    public void StudentListItemViewModel_이_알림을_낸다()
    {
        var vm = new StudentListItemViewModel();

        var seen = Capture(vm, () => vm.Name = "박지민");

        Assert.Contains(nameof(StudentListItemViewModel.Name), seen);
    }
}
