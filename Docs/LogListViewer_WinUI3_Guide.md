# LogListViewer UserControl - WinUI3 전환 가이드

## 📋 개요

WPF의 LogListViewer를 WinUI3로 전환한 UserControl입니다.
학생 생활 기록(StudentLog)을 목록으로 표시하고 편집할 수 있습니다.

### 생성된 파일 위치
```
C:\Users\centw\source\repos\Centwons\NewSchool\
├── Models\
│   └── LogEnums.cs                          (StudentInfoMode, LogCategory enum)
├── ViewModels\
│   └── StudentLogViewModel.cs               (학생 기록 표시용 ViewModel)
├── Controls\
│   ├── LogListViewer.xaml                   (UserControl XAML)
│   └── LogListViewer.xaml.cs                (코드비하인드)
└── Docs\
    └── LogListViewer_WinUI3_Guide.md        (이 파일)
```

---

## 🎯 주요 변경사항

| WPF | WinUI3 | 설명 |
|-----|--------|------|
| DataGrid | ItemsRepeater | 더 유연한 커스터마이징 |
| DateTimePicker (커스텀) | DatePicker (내장) | WinUI3 기본 컨트롤 |
| StudentConverter | ViewModel 직접 포함 | StudentLog + Student 조합 |
| ObservableCollection | 동일 | 변경 없음 |
| NEIS 바이트 계산 | ViewModel에 포함 | LogByteInfo 프로퍼티 |

---

## 💡 주요 기능

### 1. 학생 정보 표시 모드 (StudentInfoMode)
- **HideAll**: 학생 정보 모두 숨김 (개인별 보기)
- **ShowAll**: 학년도, 학기, 학년, 반, 번호, 이름 모두 표시
- **GradeClassNumName**: 학년, 반, 번호, 이름 표시
- **ClassNumName**: 반, 번호, 이름 표시
- **NumName**: 번호, 이름 표시
- **NameOnly**: 이름만 표시

### 2. 카테고리별 컬럼 조정 (LogCategory)
- **전체**: 모든 컬럼 표시
- **교과활동**: 카테고리 숨김, 과목 표시
- **동아리활동**: 카테고리 숨김, 동아리 표시
- **봉사활동/자율활동 등**: 과목 숨김
- **기타**: 카테고리, 과목 모두 숨김

### 3. 편집 기능
- 일시 (DatePicker)
- 주제 (TextBox)
- 기록 내용 (TextBox, AcceptsReturn=True)

### 4. 체크박스 다중 선택
- 전체 선택/해제
- 개별 선택
- 자동 선택 (텍스트 편집 시)

### 5. NEIS 바이트 계산
- 한글: 3바이트
- 영문/숫자/기호: 1바이트
- 자동 표시: "XXX Byte / YYY 자"

---

## 📝 사용 예제

### 1. XAML에서 사용

```xml
<Page
    xmlns:controls="using:NewSchool.Controls"
    xmlns:models="using:NewSchool.Models">
    
    <Grid>
        <!-- 기본 사용 (이름만 표시, 전체 카테고리) -->
        <controls:LogListViewer 
            x:Name="LogList"
            StudentInfoMode="NameOnly"
            Category="전체"/>
            
        <!-- 학년반번호이름 표시, 교과활동 모드 -->
        <controls:LogListViewer 
            x:Name="LogListSubject"
            StudentInfoMode="GradeClassNumName"
            Category="교과활동"/>
    </Grid>
</Page>
```

### 2. 학생 기록 로드

```csharp
using NewSchool.Controls;
using NewSchool.ViewModels;
using NewSchool.Repositories;
using NewSchool.Models;

// StudentLogRepository로 로그 조회
using var repo = new StudentLogRepository(SchoolDatabase.DbPath);
var logs = await repo.GetByStudentAsync(
    studentId: "S001",
    year: 2025,
    semester: 1
);

// EnrollmentRepository로 학생 정보 조회
using var enrollmentRepo = new EnrollmentRepository(SchoolDatabase.DbPath);
var enrollment = await enrollmentRepo.GetByStudentAndYearAsync(
    studentId: "S001",
    schoolCode: Settings.SchoolCode.Value,
    year: 2025,
    semester: 1
);

// ViewModel로 변환 (StudentLog + Enrollment 조합)
var viewModels = logs.Select(log => new StudentLogViewModel
{
    No = log.No,
    StudentID = log.StudentID,
    TeacherID = log.TeacherID,
    Year = log.Year,
    Semester = log.Semester,
    Date = DateTime.Parse(log.Date),
    Category = log.Category,
    CourseNo = log.CourseNo,
    SubjectName = log.SubjectName,
    Topic = "", // StudentLog에 Topic이 없다면 추가 필요
    Log = log.Log,
    Tag = log.Tag,
    IsImportant = log.IsImportant,
    
    // 학생 정보 (Enrollment에서)
    Grade = enrollment?.Grade ?? 0,
    Class = enrollment?.Class ?? 0,
    Number = enrollment?.Number ?? 0,
    Name = "학생이름" // Student 테이블에서 조회 필요
}).ToList();

// LogListViewer에 로드
LogList.LoadLogs(viewModels);
```

### 3. 표시 모드 변경

```csharp
// 학생 정보 표시 모드 변경
LogList.StudentInfoMode = StudentInfoMode.GradeClassNumName;
LogList.StudentInfoMode = StudentInfoMode.NameOnly;

// 카테고리 모드 변경
LogList.Category = LogCategory.교과활동;
LogList.Category = LogCategory.동아리활동;
```

### 4. 선택된 로그 가져오기

```csharp
// 선택된 로그 목록
var selectedLogs = LogList.SelectedLogs;

foreach (var log in selectedLogs)
{
    System.Diagnostics.Debug.WriteLine($"선택됨: {log.Name} - {log.Topic}");
}

// 선택된 로그 수
int count = LogList.SelectedCount;

// 선택 해제
LogList.ClearSelection();
```

### 5. ClassDiaryBox에서 사용 (예제)

```xml
<Border Grid.Row="3" AllowDrop="True" Drop="OnStudentDrop">
    <Grid Background="AliceBlue">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition/>
        </Grid.RowDefinitions>
        
        <TextBlock Grid.Row="0" Text="학생 생활" FontWeight="Bold"/>
        
        <controls:LogListViewer 
            x:Name="LogList"
            Grid.Row="1"
            StudentInfoMode="NameOnly"
            Category="전체"
            Margin="2"/>
    </Grid>
</Border>
```

```csharp
// 학생 드롭 시 로그 추가
private async void OnStudentDrop(object sender, DragEventArgs e)
{
    if (e.DataView.Properties.TryGetValue("StudentData", out object data))
    {
        if (data is StudentListItemViewModel student)
        {
            // 새 StudentLog 생성
            var newLog = new StudentLogViewModel
            {
                StudentID = student.StudentID,
                Year = Settings.WorkYear.Value,
                Semester = Settings.WorkSemester.Value,
                Date = DateTime.Now,
                Category = LogCategory.기타.ToString(),
                Grade = student.Grade,
                Class = student.Class,
                Number = student.Number,
                Name = student.Name,
                Topic = "",
                Log = ""
            };
            
            // 목록에 추가
            LogList.Logs.Add(newLog);
        }
    }
}

// 저장 버튼 클릭
private async void OnSaveLogsClick(object sender, RoutedEventArgs e)
{
    using var repo = new StudentLogRepository(SchoolDatabase.DbPath);
    
    foreach (var logVm in LogList.SelectedLogs)
    {
        var log = new StudentLog
        {
            No = logVm.No,
            StudentID = logVm.StudentID,
            TeacherID = Settings.User.Value,
            Year = logVm.Year,
            Semester = logVm.Semester,
            Date = logVm.Date.ToString("yyyy-MM-dd"),
            Category = logVm.Category,
            CourseNo = logVm.CourseNo,
            SubjectName = logVm.SubjectName,
            Log = logVm.Log,
            Tag = logVm.Tag,
            IsImportant = logVm.IsImportant
        };
        
        if (log.No > 0)
            await repo.UpdateAsync(log);
        else
            await repo.InsertAsync(log);
    }
    
    LogList.ClearSelection();
}
```

---

## 🔧 WPF에서 전환 시 주요 변경점

### 1. StudentConverter 제거
```csharp
// WPF: Converter로 Student ID → 학생 정보
Binding="{Binding Student, Converter={StaticResource StudentConverter}, ConverterParameter=Name}"

// WinUI3: ViewModel에 직접 포함
Text="{x:Bind Name}"
```

### 2. DateTimePicker → DatePicker
```xml
<!-- WPF: 커스텀 DateTimePicker -->
<Control:DateTimePicker 
    SelectedDateTime="{Binding Date, Mode=TwoWay}"/>

<!-- WinUI3: 내장 DatePicker -->
<DatePicker 
    Date="{x:Bind Date, Mode=TwoWay}"/>
```

### 3. DataGrid → ItemsRepeater
```xml
<!-- WPF -->
<DataGrid ItemsSource="{Binding Logs}">
    <DataGrid.Columns>
        <DataGridTextColumn Binding="{Binding Name}"/>
    </DataGrid.Columns>
</DataGrid>

<!-- WinUI3 -->
<ItemsRepeater ItemsSource="{x:Bind Logs}">
    <ItemsRepeater.ItemTemplate>
        <DataTemplate x:DataType="vm:StudentLogViewModel">
            <TextBlock Text="{x:Bind Name}"/>
        </DataTemplate>
    </ItemsRepeater.ItemTemplate>
</ItemsRepeater>
```

---

## 📌 주의사항

### 1. StudentLog 모델 확장 필요
현재 NewSchool의 StudentLog 모델에 **Topic** 필드가 없습니다.
필요한 경우 StudentLog 모델에 Topic 필드를 추가하세요.

```csharp
// StudentLog.cs에 추가
private string _topic = string.Empty;
public string Topic
{
    get => _topic;
    set => SetProperty(ref _topic, value);
}
```

### 2. 학생 정보 조합
LogListViewer는 StudentLog + Enrollment + Student를 조합해야 합니다.
Repository에서 JOIN 쿼리를 사용하거나, 코드에서 조합하세요.

### 3. NEIS 바이트 계산
한글 3바이트, 영문 1바이트로 계산됩니다.
특수문자나 이모지는 3바이트로 처리됩니다.

---

## 🚀 다음 단계

1. **프로젝트에 통합**
   - Visual Studio에서 프로젝트 새로고침
   - 빌드 테스트

2. **ClassDiaryBox 전환**
   - LogListViewer 사용
   - 드롭 이벤트 연결
   - 저장/삭제 기능 구현

3. **테스트**
   - 로그 표시 테스트
   - 편집 기능 테스트
   - 선택 및 저장 테스트

---

작성일: 2025-01-25
버전: 1.0
