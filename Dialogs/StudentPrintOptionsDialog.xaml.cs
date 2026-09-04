using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Dialogs;

public sealed partial class StudentPrintOptionsDialog : ContentDialog
{
    public StudentPrintOptionsDialog()
    {
        this.InitializeComponent();
    }

    /// <summary>
    /// 세부 정보 포함 여부
    /// </summary>
    public bool IncludeDetailInfo => ChkDetailInfo.IsChecked == true;

    /// <summary>
    /// 학생 생활 기록 포함 여부
    /// </summary>
    public bool IncludeStudentLogs => ChkStudentLogs.IsChecked == true;

    /// <summary>
    /// 전체 기록 출력 여부 (false면 선택한 기록만)
    /// </summary>
    public bool AllLogs => RbAllLogs.IsChecked == true;

    /// <summary>화면의 기본값(XAML 의 <c>Value="50"</c>)과 같아야 한다.</summary>
    private const int DefaultMaxLogCount = 50;

    /// <summary>
    /// 최대 출력 개수.
    ///
    /// <para>⚠ 예전에는 <c>(int)(MaxLogCount?.Value ?? 50)</c> 였다. <c>NumberBox</c> 는 칸을
    /// 비우면 <c>Value</c> 가 <see cref="double.NaN"/> 이라 <c>??</c> 로는 걸러지지 않고,
    /// 그것을 <c>(int)</c> 로 자르면 0 이 되어 <b>누가기록이 한 건도 인쇄되지 않았다</b>
    /// — "전체 기록" 을 골라 두고도(53차 실측).</para>
    /// </summary>
    public int MaxLogCountValue =>
        Helpers.PrintCount.Resolve(MaxLogCount?.Value ?? double.NaN, DefaultMaxLogCount);
}
