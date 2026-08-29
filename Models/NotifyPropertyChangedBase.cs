using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models;

/// <summary>
/// INotifyPropertyChanged 구현 기반 클래스
///
/// <para><b>규칙: 계산 속성은 자기 입력이 바뀔 때 알림을 낸다.</b>
/// <c>DisplayName => $"{Grade}학년 {Subject}"</c> 같은 속성은 <c>Grade</c>·<c>Subject</c> 의
/// 세터에서 <see cref="Notify"/> 로 함께 알려야 한다.</para>
///
/// <para><b>왜 정해 두는가.</b> <c>x:Bind</c> 는 <c>Mode</c> 를 빼면 <b>OneTime</b> 이라,
/// 알림을 안 내도 지금 화면은 멀쩡해 보인다. 그래서 규칙이 없으면 어긋난 줄도 모르고 지나가고,
/// 나중에 누가 <c>Mode=OneWay</c> 를 붙이는 순간 <b>조용히 안 돈다</b> — 오류도 경고도 없이
/// 값만 안 바뀌므로 찾기 어렵다. 실제로 <c>ClassTimetableEditDialog</c> 는 계산 속성
/// <c>DayName</c> 에 이미 <c>Mode=OneWay</c> 를 걸어 두었다(입력이 안 바뀌어 드러나지 않았을 뿐).</para>
///
/// <para>반대 방향(모델은 알리지 않고 화면이 목록을 다시 만든다)도 가능했지만, 그러면
/// <c>Mode=OneWay</c> 가 언제 안전한지를 화면마다 따져야 한다. 알리는 쪽으로 맞추면
/// <b>"OneWay 를 붙이면 그냥 된다"</b> 가 성립한다.</para>
/// </summary>
public abstract class NotifyPropertyChangedBase : INotifyPropertyChanged
{
    /// <summary>
    /// 속성 값 변경 이벤트
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 계산 속성들이 바뀌었다고 함께 알린다 — 값을 바꾼 세터에서 부른다.
    ///
    /// <para><c>if (SetProperty(ref _grade, value)) Notify(nameof(DisplayName));</c> 처럼
    /// <b>실제로 바뀌었을 때만</b> 부를 것. 그러지 않으면 같은 값을 다시 넣어도 화면이 돈다.</para>
    /// </summary>
    protected void Notify(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
            OnPropertyChanged(name);
    }

    /// <summary>
    /// 속성 변경 알림
    /// </summary>
    /// <param name="propertyName">속성 이름 (자동으로 설정됨)</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 속성 값 설정 및 변경 알림
    /// </summary>
    /// <typeparam name="T">속성 타입</typeparam>
    /// <param name="field">백킹 필드 참조</param>
    /// <param name="value">새 값</param>
    /// <param name="propertyName">속성 이름 (자동으로 설정됨)</param>
    /// <returns>값이 변경되었으면 true</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        // 값이 같으면 변경하지 않음
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
