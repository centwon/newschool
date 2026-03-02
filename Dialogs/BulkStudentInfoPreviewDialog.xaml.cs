using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace NewSchool.Dialogs;

/// <summary>
/// 학생 정보 일괄 입력 미리보기 다이얼로그
/// </summary>
public sealed partial class BulkStudentInfoPreviewDialog : ContentDialog
{
    public BulkStudentInfoPreviewDialog()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// 미리보기 데이터 설정
    /// </summary>
    public void SetPreviewData(List<StudentImportPreviewItem> items)
    {
        int matchedCount = items.Count(i => i.IsMatched);
        int unmatchedCount = items.Count(i => !i.IsMatched);
        int changedCount = items.Count(i => i.IsMatched && i.Changes.Count > 0);
        int noChangeCount = items.Count(i => i.IsMatched && i.Changes.Count == 0);

        TxtSummary.Text = $"전체 {items.Count}명 중 매칭 {matchedCount}명";
        if (changedCount > 0) TxtSummary.Text += $" (변경 {changedCount}명)";
        if (noChangeCount > 0) TxtSummary.Text += $" / 변경 없음 {noChangeCount}명";
        if (unmatchedCount > 0) TxtSummary.Text += $" / 미매칭 {unmatchedCount}명";

        // PrimaryButton 활성화 조건: 변경 있는 학생이 1명 이상
        IsPrimaryButtonEnabled = changedCount > 0;

        // ListView에 직접 아이템 추가
        PreviewListView.Items.Clear();
        foreach (var item in items)
        {
            var grid = new Grid
            {
                Padding = new Thickness(4),
                ColumnSpacing = 8
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new FontIcon { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!item.IsMatched)
            {
                icon.Glyph = "\uE7BA"; // Warning
                icon.Foreground = new SolidColorBrush(Colors.Red);
                text.Text = $"{item.Number}번 {item.Name} — 매칭 실패 (학급 명부에 없음)";
                text.Foreground = new SolidColorBrush(Colors.Red);
            }
            else if (item.Changes.Count == 0)
            {
                icon.Glyph = "\uE73E"; // Check
                icon.Foreground = new SolidColorBrush(Colors.Gray);
                text.Text = $"{item.Number}번 {item.Name} — 변경 없음";
                text.Foreground = new SolidColorBrush(Colors.Gray);
            }
            else
            {
                icon.Glyph = "\uE70F"; // Edit
                icon.Foreground = new SolidColorBrush(Colors.DodgerBlue);
                text.Text = $"{item.Number}번 {item.Name} — {string.Join(", ", item.Changes)}";
            }

            Grid.SetColumn(icon, 0);
            Grid.SetColumn(text, 1);
            grid.Children.Add(icon);
            grid.Children.Add(text);

            PreviewListView.Items.Add(grid);
        }
    }
}

/// <summary>
/// 미리보기 아이템 데이터
/// </summary>
public class StudentImportPreviewItem
{
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? MatchedStudentID { get; set; }
    public bool IsMatched => !string.IsNullOrEmpty(MatchedStudentID);
    public List<string> Changes { get; set; } = new();

    // 변경할 Student 필드들
    public Dictionary<string, string?> StudentFields { get; set; } = new();

    // 변경할 StudentDetail 필드들
    public Dictionary<string, string?> DetailFields { get; set; } = new();
}
