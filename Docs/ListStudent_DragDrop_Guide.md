# ListStudent 드래그 앤 드롭 가이드

## 📋 드래그 비주얼 위치 문제

### 문제 설명
WinUI 3의 `ListView`에서 `CanDragItems="True"`로 드래그를 활성화하면, 드래그 시각적 피드백(DragUIOverride)이 마우스 커서에서 멀리 떨어진 위치에 표시되는 문제가 있습니다.

이는 **WinUI 3의 알려진 제한사항**으로, ListView가 내부적으로 드래그 비주얼을 생성하는 방식 때문입니다.

---

## 🔧 현재 구현된 개선 사항

### 1. 텍스트 설정
```csharp
e.Data.SetText($"{student.Number}. {student.Name}");
```
- 드래그 중 표시될 텍스트를 설정
- 최소한의 정보 표시

### 2. DragUIOverride 설정 (Drop 대상)
```csharp
private void Card_DragOver(object sender, DragEventArgs e)
{
    if (e.DragUIOverride != null)
    {
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        e.DragUIOverride.Caption = "학생 배치";
    }
}
```
- Drop 대상 위에서 Caption 표시
- 마우스 커서 근처에 힌트 제공

---

## 💡 대안 해결 방법

### 방법 1: ItemsRepeater 사용 (권장)
ListView 대신 `ItemsRepeater`를 사용하면 더 세밀한 드래그 제어가 가능합니다.

**장점:**
- 완전한 드래그 비주얼 커스터마이징
- 마우스 커서 위치 정확히 제어
- 더 나은 성능

**단점:**
- 구현 복잡도 증가
- 기본 선택/스크롤 기능 직접 구현 필요

### 방법 2: PointerPressed + PointerMoved 직접 구현
ListView의 기본 드래그 대신 수동 드래그 구현

```csharp
private void StudentItem_PointerPressed(object sender, PointerRoutedEventArgs e)
{
    _draggedStudent = (sender as FrameworkElement)?.DataContext as StudentListItemViewModel;
}

private void StudentItem_PointerMoved(object sender, PointerRoutedEventArgs e)
{
    if (_draggedStudent != null && e.Pointer.IsInContact)
    {
        // 커스텀 드래그 비주얼 표시
        var position = e.GetCurrentPoint(this).Position;
        // ...
    }
}
```

**장점:**
- 완전한 제어
- 정확한 위치 지정

**단점:**
- 많은 코드 필요
- 접근성 문제 고려 필요

### 방법 3: 클릭으로 선택 + 버튼으로 배치
드래그 앤 드롭 대신 클릭 기반 UI

```csharp
// 학생 선택
StudentList.SelectedStudent = student;

// 버튼 클릭으로 좌석 배치
private void AssignToSelectedSeat_Click(object sender, RoutedEventArgs e)
{
    var student = StudentList.SelectedStudent;
    var card = _selectedPhotoCard;
    if (student != null && card != null)
    {
        card.StudentData = ConvertToCardData(student);
    }
}
```

**장점:**
- 구현 간단
- 명확한 UI
- 모바일/터치 친화적

**단점:**
- 드래그 앤 드롭의 직관성 손실

---

## 🎯 권장 사항

### 현재 상황 (ListView + CanDragItems)
- **사용 가능**: 기능은 정상 작동
- **시각적 문제**: 드래그 비주얼 위치가 부자연스러움
- **권장**: 
  - 기능 우선이면 현재 구현 유지
  - UI/UX 중요하면 방법 3 (클릭 기반) 고려

### 장기 개선 계획
1. **단기**: 현재 ListView 방식 유지 + 사용자 가이드 제공
2. **중기**: 클릭 기반 배치 추가 옵션 제공
3. **장기**: ItemsRepeater로 전환 (성능 + UX 개선)

---

## 🔍 WinUI 3 드래그 앤 드롭 제한사항

### ListView의 제한
- `DragItemsStarting`에서 커스텀 비주얼 설정 제한
- 드래그 비주얼 위치 제어 불가
- 다중 항목 드래그 시 첫 번째 항목만 표시

### 해결 불가능한 이유
- WinUI 3 내부 구현 방식
- ListView가 ItemsWrapGrid/ItemsStackPanel 사용
- 드래그 비주얼이 ListView의 시각적 트리에서 생성됨

### Microsoft 공식 이슈
- GitHub Issue: [WinUI #7820](https://github.com/microsoft/microsoft-ui-xaml/issues/7820)
- Status: Known Issue (진행 중)

---

## 📚 참고 자료

### WinUI 3 드래그 앤 드롭
- [공식 문서](https://learn.microsoft.com/en-us/windows/apps/design/input/drag-and-drop)
- [ListView Drag Sample](https://github.com/microsoft/WindowsAppSDK-Samples)

### 대안 구현 예제
- [ItemsRepeater Drag](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- [Custom Drag Visual](https://stackoverflow.com/questions/winui3-drag-visual)

---

작성일: 2025-01-25  
버전: 1.0  
상태: 현재 구현은 기능적으로 완벽하나, 시각적 개선이 필요한 상태
