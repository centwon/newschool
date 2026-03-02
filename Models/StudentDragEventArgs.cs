using NewSchool.ViewModels;

namespace NewSchool.Models
{
    /// <summary>
    /// 학생 드래그앤드롭 이벤트 인자
    /// WPF EventStudentArgs를 WinUI3용으로 전환
    /// </summary>
    public class StudentDragEventArgs
    {
        /// <summary>드래그된 학생 정보</summary>
        public StudentListItemViewModel? Student { get; set; }

        /// <summary>드래그 시작 행 인덱스 (옵션)</summary>
        public int RowIndex { get; set; } = -1;

        /// <summary>드래그 시작 열 인덱스 (옵션)</summary>
        public int ColumnIndex { get; set; } = -1;

        public StudentDragEventArgs()
        {
        }

        public StudentDragEventArgs(StudentListItemViewModel student)
        {
            Student = student;
        }

        public StudentDragEventArgs(StudentListItemViewModel student, int rowIndex, int columnIndex)
        {
            Student = student;
            RowIndex = rowIndex;
            ColumnIndex = columnIndex;
        }
    }
}
