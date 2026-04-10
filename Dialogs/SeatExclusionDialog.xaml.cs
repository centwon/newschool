using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Controls;
using System.Collections.Generic;
using System.Linq;

namespace NewSchool.Dialogs;

public sealed partial class SeatExclusionDialog : ContentDialog
{
    /// <summary>
    /// 분리 쌍 목록
    /// </summary>
    public List<(string IdA, string IdB)> ExclusionPairs { get; }

    /// <summary>
    /// 고정 쌍 목록
    /// </summary>
    public List<(string IdA, string IdB)> FixedPairs { get; }

    private readonly List<StudentItem> _students;
    private readonly Dictionary<string, string> _nameLookup;

    public SeatExclusionDialog(
        IEnumerable<StudentCardData> students,
        List<(string IdA, string IdB)> exclusionPairs,
        List<(string IdA, string IdB)> fixedPairs)
    {
        this.InitializeComponent();

        ExclusionPairs = new List<(string, string)>(exclusionPairs);
        FixedPairs = new List<(string, string)>(fixedPairs);

        _students = students
            .OrderBy(s => s.Number)
            .Select(s => new StudentItem(s.StudentID, $"{s.Name}({s.Number})"))
            .ToList();

        _nameLookup = _students.ToDictionary(s => s.Id, s => s.Name);

        StudentABox.ItemsSource = _students;
        StudentBBox.ItemsSource = _students;

        RefreshList();
    }

    private void AddExclusionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPair(out var idA, out var idB)) return;
        if (IsDuplicate(idA, idB)) return;

        ExclusionPairs.Add((idA, idB));
        ClearSelection();
        RefreshList();
    }

    private void AddFixedButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedPair(out var idA, out var idB)) return;
        if (IsDuplicate(idA, idB)) return;

        FixedPairs.Add((idA, idB));
        ClearSelection();
        RefreshList();
    }

    private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int idx && idx < ExclusionPairs.Count)
        {
            ExclusionPairs.RemoveAt(idx);
            RefreshList();
        }
    }

    private void RemoveFixed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int idx && idx < FixedPairs.Count)
        {
            FixedPairs.RemoveAt(idx);
            RefreshList();
        }
    }

    private bool TryGetSelectedPair(out string idA, out string idB)
    {
        idA = idB = string.Empty;
        if (StudentABox.SelectedItem is not StudentItem a ||
            StudentBBox.SelectedItem is not StudentItem b)
            return false;
        if (a.Id == b.Id) return false;
        idA = a.Id;
        idB = b.Id;
        return true;
    }

    private bool IsDuplicate(string idA, string idB)
    {
        return ExclusionPairs.Any(p =>
                (p.IdA == idA && p.IdB == idB) || (p.IdA == idB && p.IdB == idA))
            || FixedPairs.Any(p =>
                (p.IdA == idA && p.IdB == idB) || (p.IdA == idB && p.IdB == idA));
    }

    private void ClearSelection()
    {
        StudentABox.SelectedIndex = -1;
        StudentBBox.SelectedIndex = -1;
    }

    private string GetName(string id) => _nameLookup.GetValueOrDefault(id, id);

    private void RefreshList()
    {
        var items = new List<UIElement>();

        // 분리 쌍
        for (int i = 0; i < ExclusionPairs.Count; i++)
        {
            var (idA, idB) = ExclusionPairs[i];
            items.Add(CreatePairRow($"🚫  {GetName(idA)}  ↔  {GetName(idB)}", i, isExclusion: true));
        }

        // 고정 쌍
        for (int i = 0; i < FixedPairs.Count; i++)
        {
            var (idA, idB) = FixedPairs[i];
            items.Add(CreatePairRow($"📌  {GetName(idA)}  ↔  {GetName(idB)}", i, isExclusion: false));
        }

        PairListView.Items.Clear();
        foreach (var item in items)
            PairListView.Items.Add(item);

        EmptyMessage.Visibility = items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
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

        var tb = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13
        };
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

    /// <summary>
    /// ComboBox에 표시할 학생 항목
    /// </summary>
    private record StudentItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
