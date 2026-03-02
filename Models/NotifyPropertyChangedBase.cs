using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewSchool.Models;

/// <summary>
/// INotifyPropertyChanged 구현 기반 클래스
/// </summary>
public abstract class NotifyPropertyChangedBase : INotifyPropertyChanged
{
    /// <summary>
    /// 속성 값 변경 이벤트
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

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
