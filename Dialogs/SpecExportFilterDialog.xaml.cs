using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Dialogs;

public sealed partial class SpecExportFilterDialog : ContentDialog
{
    public SpecExportFilterDialog()
    {
        this.InitializeComponent();
    }

    /// <summary>선택한 영역 (빈 문자열=전체)</summary>
    public string SelectedType
    {
        get
        {
            if (CBoxType.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                return tag == "전체" ? string.Empty : tag;
            return string.Empty;
        }
    }

    /// <summary>상태 필터: "all", "draft", "finalized"</summary>
    public string StatusFilter
    {
        get
        {
            if (CBoxStatus.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                return tag;
            return "all";
        }
    }

    /// <summary>빈 항목 제외 여부</summary>
    public bool ExcludeEmpty => ChkExcludeEmpty.IsChecked == true;

    /// <summary>PDF 형식 여부</summary>
    public bool IsPdf => RbPdf.IsChecked == true;
}
