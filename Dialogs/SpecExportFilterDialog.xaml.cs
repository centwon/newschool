using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Dialogs;

public sealed partial class SpecExportFilterDialog : ContentDialog
{
    public SpecExportFilterDialog()
    {
        this.InitializeComponent();

        // 영역 목록은 NeisHelper.Areas 정의표에서 생성 — XAML 에 고정하면 정의표와 어긋난다
        // (예전에는 이 목록에만 "봉사활동"이 있고 한도·설정 화면에는 빠져 있었다).
        CBoxType.Items.Add(new ComboBoxItem { Content = "전체", Tag = "전체" });
        foreach (var area in Helpers.NeisHelper.Areas)
            CBoxType.Items.Add(new ComboBoxItem { Content = area.Label, Tag = area.Key });
        CBoxType.SelectedIndex = 0;
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
