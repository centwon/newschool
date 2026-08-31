using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Controls;

/// <summary>
/// 아무것도 없을 때 띄우는 안내판.
///
/// <para>첫 설치 직후에는 학생도 수업도 하나도 없다. 그런데 학급·수업 화면들은 빈 목록만
/// 보여 주고 "그래서 무엇을 해야 하는지"는 말하지 않아서, 초기 설정을 마친 사용자가
/// 어디로 가야 학생을 넣는지 스스로 찾아내야 했다. 이 컨트롤은 그 자리에 다음 할 일을
/// 적고 그 화면으로 가는 버튼을 하나 놓는다.</para>
///
/// <para>버튼을 누르면 <see cref="ActionInvoked"/> 가 뜬다 — 어디로 보낼지는 쓰는 쪽이 정한다.</para>
/// </summary>
public sealed partial class EmptyStateView : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(""));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(EmptyStateView),
            new PropertyMetadata(string.Empty));

    /// <summary>Segoe Fluent Icons 글리프.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>굵게 나오는 한 줄. 예: "등록된 학생이 없습니다".</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>무엇을 하면 되는지 설명하는 줄.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>버튼에 적을 말. 예: "학생 추가".</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>버튼을 눌렀을 때.</summary>
    public event EventHandler? ActionInvoked;

    public EmptyStateView()
    {
        this.InitializeComponent();
    }

    private void OnActionClick(object sender, RoutedEventArgs e)
    {
        ActionInvoked?.Invoke(this, EventArgs.Empty);
    }
}
