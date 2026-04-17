using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;

namespace NewSchool.Dialogs;

public sealed partial class BatchExportFilterDialog : ContentDialog
{
    public BatchExportFilterDialog()
    {
        this.InitializeComponent();

        // 카테고리 목록 (전체 포함)
        var categories = Enum.GetValues<LogCategory>().Cast<LogCategory>().ToList();
        CBoxCategory.ItemsSource = categories;
        CBoxCategory.SelectedIndex = 0; // 전체
    }

    /// <summary>선택한 카테고리</summary>
    public LogCategory SelectedCategory =>
        CBoxCategory.SelectedItem is LogCategory cat ? cat : LogCategory.전체;

    /// <summary>선택한 학기 (0=전체)</summary>
    public int SelectedSemester
    {
        get
        {
            if (CBoxSemester.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                return int.TryParse(tag, out int s) ? s : 0;
            return 0;
        }
    }

    /// <summary>키워드 필터</summary>
    public string Keyword => TbKeyword.Text?.Trim() ?? string.Empty;

    /// <summary>PDF 형식 여부</summary>
    public bool IsPdf => RbPdf.IsChecked == true;
}
