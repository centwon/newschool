using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Dialogs;

/// <summary>
/// 앞으로 걸린 수업 변경 목록 — <b>보고 지우는 곳</b>이다.
///
/// 넣고 고치는 것은 배치판의 주별 시간표에서 한다. 같은 표에 쓰는 입구가 둘이면
/// 어느 쪽이 진짜인지 헷갈린다(예전에 카드의 [시간표 배치] 다이얼로그를 없앤 것과 같은 이유).
/// 다만 먼 날짜의 변경을 찾으려고 주를 여러 번 넘기는 건 번거로우므로,
/// "앞으로 무엇이 걸려 있나" 를 한 번에 훑는 자리는 남겨 둔다.
/// </summary>
public sealed partial class LessonChangeDialog : ContentDialog
{
    private readonly int _year;
    private readonly int _semester;
    private readonly string _teacherId;

    private readonly ObservableCollection<LessonChange> _changes = [];

    public LessonChangeDialog(int year, int semester)
    {
        this.InitializeComponent();

        _year = year;
        _semester = semester;
        _teacherId = Settings.User.Value;

        ChangeListView.ItemsSource = _changes;

        Loaded += OnDialogLoaded;
    }

    private async void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        // async void 라 여기서 새는 예외는 아무 데도 잡히지 않는다 — 앱이 메시지 없이 멈춘다.
        try
        {
            await LoadChangesAsync();
        }
        catch (Exception ex)
        {
            await Controls.UserErrorReporter.ReportAsync("수업 변경 목록 열기", ex);
        }
    }

    /// <summary>
    /// 오늘부터 학기 끝까지의 변경. 지나간 것까지 쌓아 두면 앞으로 무엇이 걸려 있는지가 묻힌다.
    /// </summary>
    private async Task LoadChangesAsync()
    {
        var (_, end) = Services.WeeklyHoursCalculator.DefaultSemesterRange(_year, _semester);

        using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);
        var list = await repo.GetRangeAsync(_teacherId, DateTime.Today, end);

        _changes.Clear();
        foreach (var change in list)
            _changes.Add(change);

        UpdateEmptyState();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not LessonChange change) return;

        // 되돌릴 수 없는데 확인 없이 지우고 있었다 — 목록에서 누르는 즉시 사라졌다.
        // 다른 삭제는 모두 확인을 받는다.
        if (!await MessageBox.ShowConfirmAsync(
                $"{change.Date:yyyy-MM-dd} {change.Period}교시의 수업 변경을 되돌립니다.\n되돌릴 수 없습니다.",
                "수업 변경 삭제", "삭제", "취소"))
            return;

        try
        {
            using var repo = new LessonChangeRepository(SchoolDatabase.DbPath);

            // 반영된 것만 화면에서 뺀다 — 결과를 버리면 0행 삭제(이미 지워진 변경 등)에도
            // 목록에서 사라지고, 새로 고치면 되살아난다.
            if (!await repo.DeleteAsync(change.No))
            {
                ChangeInfoBar.Message = "삭제되지 않았습니다. 이미 지워진 변경일 수 있습니다.";
                ChangeInfoBar.IsOpen = true;
                return;
            }

            _changes.Remove(change);
            UpdateEmptyState();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LessonChangeDialog] 변경 삭제 실패: {ex.Message}");
            ChangeInfoBar.Message = $"변경을 되돌리지 못했습니다.\n{ex.Message}";
            ChangeInfoBar.IsOpen = true;
        }
    }

    private void UpdateEmptyState()
    {
        bool has = _changes.Count > 0;
        ChangeListView.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
    }
}
