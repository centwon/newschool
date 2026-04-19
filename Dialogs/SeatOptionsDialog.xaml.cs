using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using NewSchool.Models;
using System.Collections.Generic;
using System.Linq;

namespace NewSchool.Dialogs;

/// <summary>
/// 좌석 배치 옵션 다이얼로그. 탭 구조로 이력·속성·짝제약·방식을 관리한다.
/// 확인 시 `Result`에 현재 입력값이 반영된 SeatOptions를 채워 리턴한다.
/// </summary>
public sealed partial class SeatOptionsDialog : ContentDialog
{
    /// <summary>최종 옵션값 (확인 누를 때 갱신)</summary>
    public SeatOptions Result { get; private set; }

    private readonly List<StudentCardData> _students;
    private readonly Dictionary<string, string> _nameLookup;
    private readonly Dictionary<string, string> _sexLookup;

    // 현재 편집 중인 값
    private readonly List<SeatOptions.PairRule> _exclusionPairs = new();
    private readonly List<SeatOptions.PairRule> _fixedPairs = new();
    private readonly List<string> _frontIds = new();

    public SeatOptionsDialog(
        IEnumerable<StudentCardData> students,
        SeatOptions initial,
        int savedRoundsCount)
    {
        InitializeComponent();

        _students = students.OrderBy(s => s.Number).ToList();
        _nameLookup = _students.ToDictionary(s => s.StudentID, s => $"{s.Name}({s.Number})");
        _sexLookup = _students.ToDictionary(s => s.StudentID, s => s.Sex ?? string.Empty);

        Result = CloneOptions(initial);

        // 초기값 UI 반영
        ChkRecentPair.IsChecked = Result.RecentPairAvoidRounds > 0;
        NbxRecentPair.Value = Result.RecentPairAvoidRounds > 0 ? Result.RecentPairAvoidRounds : 1;

        ChkRecentPos.IsChecked = Result.RecentPositionAvoidRounds > 0;
        NbxRecentPos.Value = Result.RecentPositionAvoidRounds > 0 ? Result.RecentPositionAvoidRounds : 1;

        ChkMixedGender.IsChecked = Result.PreferMixedGenderPair;
        NbxFrontMaxRow.Value = Result.FrontPriorityMaxRow;

        _frontIds.AddRange(Result.FrontPriorityStudentIds);
        _exclusionPairs.AddRange(Result.ExclusionPairs.Select(p => new SeatOptions.PairRule { IdA = p.IdA, IdB = p.IdB }));
        _fixedPairs.AddRange(Result.FixedPairs.Select(p => new SeatOptions.PairRule { IdA = p.IdA, IdB = p.IdB }));

        // 시도 횟수 초기 선택
        var attempts = Result.MaxAttempts <= 0 ? 500 : Result.MaxAttempts;
        foreach (var obj in CbxAttempts.Items)
        {
            if (obj is ComboBoxItem ci && ci.Tag is string s && int.TryParse(s, out var v) && v == attempts)
            {
                CbxAttempts.SelectedItem = ci;
                break;
            }
        }

        // 학생 ComboBox 채우기
        PopulateStudentComboBoxes();

        // 이력 메시지
        HistoryInfo.Message = savedRoundsCount > 0
            ? $"누적된 배치 회차: {savedRoundsCount}회"
            : "아직 저장된 배치가 없습니다. 첫 저장 이후부터 이력 기반 옵션이 활성화됩니다.";

        RefreshPairList();
        RefreshFrontList();

        PrimaryButtonClick += OnConfirm;
    }

    private static SeatOptions CloneOptions(SeatOptions src)
    {
        return new SeatOptions
        {
            RecentPairAvoidRounds = src.RecentPairAvoidRounds,
            RecentPositionAvoidRounds = src.RecentPositionAvoidRounds,
            PreferMixedGenderPair = src.PreferMixedGenderPair,
            FrontPriorityStudentIds = new List<string>(src.FrontPriorityStudentIds),
            FrontPriorityMaxRow = src.FrontPriorityMaxRow,
            ExclusionPairs = src.ExclusionPairs.Select(p => new SeatOptions.PairRule { IdA = p.IdA, IdB = p.IdB }).ToList(),
            FixedPairs = src.FixedPairs.Select(p => new SeatOptions.PairRule { IdA = p.IdA, IdB = p.IdB }).ToList(),
            MaxAttempts = src.MaxAttempts <= 0 ? 500 : src.MaxAttempts,
        };
    }

    private void PopulateStudentComboBoxes()
    {
        var items = _students.Select(s => new StudentItem(s.StudentID, _nameLookup[s.StudentID])).ToList();
        CbxPairA.ItemsSource = items;
        CbxPairB.ItemsSource = items;
        CbxFrontStudent.ItemsSource = items;
    }

    #region 짝 제약

    private void BtnAddExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPair(CbxPairA, CbxPairB, out var a, out var b)) return;
        if (IsDuplicatePair(a, b)) return;
        _exclusionPairs.Add(new SeatOptions.PairRule { IdA = a, IdB = b });
        CbxPairA.SelectedIndex = -1;
        CbxPairB.SelectedIndex = -1;
        RefreshPairList();
    }

    private void BtnAddFixed_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPair(CbxPairA, CbxPairB, out var a, out var b)) return;
        if (IsDuplicatePair(a, b)) return;
        _fixedPairs.Add(new SeatOptions.PairRule { IdA = a, IdB = b });
        CbxPairA.SelectedIndex = -1;
        CbxPairB.SelectedIndex = -1;
        RefreshPairList();
    }

    private static bool TryGetPair(ComboBox a, ComboBox b, out string idA, out string idB)
    {
        idA = idB = string.Empty;
        if (a.SelectedItem is not StudentItem sa || b.SelectedItem is not StudentItem sb) return false;
        if (sa.Id == sb.Id) return false;
        idA = sa.Id;
        idB = sb.Id;
        return true;
    }

    private bool IsDuplicatePair(string idA, string idB)
    {
        bool eq(SeatOptions.PairRule p) =>
            (p.IdA == idA && p.IdB == idB) || (p.IdA == idB && p.IdB == idA);
        return _exclusionPairs.Any(eq) || _fixedPairs.Any(eq);
    }

    private void RefreshPairList()
    {
        PairListView.Items.Clear();
        for (int i = 0; i < _exclusionPairs.Count; i++)
        {
            var p = _exclusionPairs[i];
            PairListView.Items.Add(CreatePairRow(
                $"🚫  {GetName(p.IdA)}  ↔  {GetName(p.IdB)}", i, isExclusion: true));
        }
        for (int i = 0; i < _fixedPairs.Count; i++)
        {
            var p = _fixedPairs[i];
            PairListView.Items.Add(CreatePairRow(
                $"📌  {GetName(p.IdA)}  ↔  {GetName(p.IdB)}", i, isExclusion: false));
        }
        PairEmpty.Visibility = PairListView.Items.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid CreatePairRow(string text, int index, bool isExclusion)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        var tb = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        Grid.SetColumn(tb, 0);
        var btn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 11 },
            Padding = new Thickness(5, 3, 5, 3),
            Tag = index
        };
        btn.Click += isExclusion ? RemoveExclusion_Click : RemoveFixed_Click;
        Grid.SetColumn(btn, 1);
        grid.Children.Add(tb);
        grid.Children.Add(btn);
        return grid;
    }

    private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is int idx && idx < _exclusionPairs.Count)
        {
            _exclusionPairs.RemoveAt(idx);
            RefreshPairList();
        }
    }

    private void RemoveFixed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is int idx && idx < _fixedPairs.Count)
        {
            _fixedPairs.RemoveAt(idx);
            RefreshPairList();
        }
    }

    #endregion

    #region 앞자리 우선

    private void BtnAddFront_Click(object sender, RoutedEventArgs e)
    {
        if (CbxFrontStudent.SelectedItem is not StudentItem s) return;
        if (_frontIds.Contains(s.Id)) return;
        _frontIds.Add(s.Id);
        CbxFrontStudent.SelectedIndex = -1;
        RefreshFrontList();
    }

    private void RemoveFront_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is int idx && idx < _frontIds.Count)
        {
            _frontIds.RemoveAt(idx);
            RefreshFrontList();
        }
    }

    private void RefreshFrontList()
    {
        FrontListView.Items.Clear();
        for (int i = 0; i < _frontIds.Count; i++)
        {
            var id = _frontIds[i];
            FrontListView.Items.Add(CreateFrontRow(GetName(id), i));
        }
    }

    private Grid CreateFrontRow(string name, int index)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        var tb = new TextBlock { Text = "🪑 " + name, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        Grid.SetColumn(tb, 0);
        var btn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 11 },
            Padding = new Thickness(5, 3, 5, 3),
            Tag = index
        };
        btn.Click += RemoveFront_Click;
        Grid.SetColumn(btn, 1);
        grid.Children.Add(tb);
        grid.Children.Add(btn);
        return grid;
    }

    #endregion

    private string GetName(string id) => _nameLookup.GetValueOrDefault(id, id);

    private void OnConfirm(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result.RecentPairAvoidRounds = ChkRecentPair.IsChecked == true ? (int)NbxRecentPair.Value : 0;
        Result.RecentPositionAvoidRounds = ChkRecentPos.IsChecked == true ? (int)NbxRecentPos.Value : 0;
        Result.PreferMixedGenderPair = ChkMixedGender.IsChecked == true;
        Result.FrontPriorityMaxRow = (int)NbxFrontMaxRow.Value;
        Result.FrontPriorityStudentIds = new List<string>(_frontIds);
        Result.ExclusionPairs = new List<SeatOptions.PairRule>(_exclusionPairs);
        Result.FixedPairs = new List<SeatOptions.PairRule>(_fixedPairs);

        if (CbxAttempts.SelectedItem is ComboBoxItem ci && ci.Tag is string s && int.TryParse(s, out var v))
            Result.MaxAttempts = v;
        else
            Result.MaxAttempts = 500;
    }

    private record StudentItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
