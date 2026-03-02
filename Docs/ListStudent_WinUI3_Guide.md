# ListStudent UserControl - WinUI3 전환 가이드

## 📋 개요

WPF의 ListStudent를 WinUI3로 전환한 UserControl입니다.

### 생성된 파일 위치
```
C:\Users\centw\source\repos\Centwons\NewSchool\
├── ViewModels\
│   └── StudentListItemViewModel.cs        (학생 목록 표시용 ViewModel)
├── Controls\
│   ├── ListStudent.xaml                   (UserControl XAML)
│   └── ListStudent.xaml.cs                (코드비하인드)
├── Models\
│   └── StudentDragEventArgs.cs            (드래그 이벤트 인자)
└── Docs\
    └── ListStudent_WinUI3_Guide.md         (이 파일)
```

---

## 🎯 주요 변경사항

| WPF | WinUI3 | 설명 |
|-----|--------|------|
| DataGrid | ItemsRepeater | 더 유연하고 성능 좋은 리스트 표시 |
| AsignClass | StudentListItemViewModel | Enrollment + Student 조합 |
| DragDrop.DoDragDrop | DragStarting 이벤트 | WinUI3 드래그앤드롭 API |
| DataObject | DataPackage | WinUI3 데이터 전송 객체 |

---

## 💡 사용 예제

### 1. XAML에서 사용

```xml
<Page
    xmlns:controls="using:NewSchool.Controls">
    
    <Grid>
        <!-- 기본 사용 -->
        <controls:ListStudent 
            x:Name="StudentList"
            ViewMode="NumName"
            ShowCheckBox="False"/>
    </Grid>
</Page>
```

### 2. 학생 목록 로드

```csharp
using NewSchool.Controls;
using NewSchool.ViewModels;
using NewSchool.Services;

// EnrollmentService로 학생 조회
var studentInfos = await _enrollmentService.GetStudentsByClassAsync(
    Settings.SchoolCode.Value,
    Settings.WorkYear.Value,
    Settings.WorkSemester.Value,
    grade: 1,
    classNo: 1
);

// ViewModel로 변환
var viewModels = studentInfos.Select(s => new StudentListItemViewModel
{
    EnrollmentNo = s.EnrollmentNo,
    StudentID = s.StudentID,
    Year = s.Year,
    Semester = s.Semester,
    Grade = s.Grade,
    Class = s.Class,
    Number = s.Number,
    Name = s.Name,
    Sex = s.Sex,
    Status = s.Status
}).ToList();

// ListStudent에 로드
StudentList.LoadStudents(viewModels);
```

### 3. 표시 모드 변경

```csharp
// 4가지 표시 모드
StudentList.ViewMode = ListStudent.View.Full;          // 학년+반+번호+이름
StudentList.ViewMode = ListStudent.View.ClassNumName;  // 반+번호+이름
StudentList.ViewMode = ListStudent.View.NumName;       // 번호+이름 (기본값)
StudentList.ViewMode = ListStudent.View.NameOnly;      // 이름만
```

### 4. 다중 선택 모드

```csharp
// 체크박스 표시
StudentList.ShowCheckBox = true;

// 선택된 학생 가져오기
var selected = StudentList.SelectedStudents;
int count = StudentList.SelectedCount;

// 선택 해제
StudentList.ClearSelection();
```

### 5. 드래그 앤 드롭 받기

```xml
<Border 
    AllowDrop="True"
    Drop="OnStudentDrop">
    <TextBlock Text="여기에 학생을 드롭하세요"/>
</Border>
```

```csharp
private void OnStudentDrop(object sender, DragEventArgs e)
{
    if (e.DataView.Properties.TryGetValue("StudentData", out object data))
    {
        if (data is StudentListItemViewModel student)
        {
            // 드롭된 학생 정보 사용
            System.Diagnostics.Debug.WriteLine($"드롭됨: {student.Name}");
        }
    }
}
```

---

## 📌 다음 작업

이 파일들을 프로젝트에 통합하려면:

1. **Visual Studio에서 프로젝트 새로고침**
   - Solution Explorer에서 프로젝트 우클릭 → Reload Project

2. **빌드 테스트**
   - Ctrl + Shift + B로 빌드 확인

3. **사용 예제 페이지 작성**
   - ListStudent를 사용하는 테스트 페이지 생성

---

작성일: 2025-01-25
버전: 1.0
