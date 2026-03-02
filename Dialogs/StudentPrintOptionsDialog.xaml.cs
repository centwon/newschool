using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Dialogs
{
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

        /// <summary>
        /// 최대 출력 개수
        /// </summary>
        public int MaxLogCountValue => (int)(MaxLogCount?.Value ?? 50);
    }
}
