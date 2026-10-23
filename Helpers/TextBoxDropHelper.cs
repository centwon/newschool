using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace NewSchool.Helpers;

/// <summary>
/// TextBox에 드래그 앤 드롭 기능을 자동으로 활성화하는 헬퍼
/// </summary>
public static class TextBoxDropHelper
{
    public static bool GetEnableTextDrop(DependencyObject obj)
    {
        return (bool)obj.GetValue(EnableTextDropProperty);
    }

    public static void SetEnableTextDrop(DependencyObject obj, bool value)
    {
        obj.SetValue(EnableTextDropProperty, value);
    }

    public static readonly DependencyProperty EnableTextDropProperty =
        DependencyProperty.RegisterAttached(
            "EnableTextDrop",
            typeof(bool),
            typeof(TextBoxDropHelper),
            new PropertyMetadata(false, OnEnableTextDropChanged));

    private static void OnEnableTextDropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[TextBoxDropHelper] OnEnableTextDropChanged 호출됨 - Value: {e.NewValue}");

        if (d is TextBox textBox)
        {
            System.Diagnostics.Debug.WriteLine($"[TextBoxDropHelper] TextBox 발견 - Name: {textBox.Name}");

            if ((bool)e.NewValue)
            {
                textBox.AllowDrop = true;
                textBox.DragEnter += TextBox_DragEnter;
                textBox.DragOver += TextBox_DragOver;
                textBox.Drop += TextBox_Drop;
                System.Diagnostics.Debug.WriteLine($"[TextBoxDropHelper] 드래그 앤 드롭 활성화됨");
            }
            else
            {
                textBox.AllowDrop = false;
                textBox.DragEnter -= TextBox_DragEnter;
                textBox.DragOver -= TextBox_DragOver;
                textBox.Drop -= TextBox_Drop;
                System.Diagnostics.Debug.WriteLine($"[TextBoxDropHelper] 드래그 앤 드롭 비활성화됨");
            }
        }
    }

    /// <summary>
    /// 글을 받아도 되는 칸인가. 전역 스타일로 모든 TextBox 에 붙어 있어서, 예전에는 읽기 전용·
    /// 비활성 칸(미리보기·표시용)에도 끌어 놓으면 글이 들어갔다.
    /// </summary>
    private static bool CanAccept(object sender) => sender is TextBox { IsReadOnly: false, IsEnabled: true };

    private static void TextBox_DragEnter(object sender, DragEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[TextBoxDrop] DragEnter 이벤트 발생");

        if (!CanAccept(sender))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        // 드래그 시작 시 드롭 가능 여부 표시
        e.AcceptedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;

        if (e.DragUIOverride != null)
        {
            e.DragUIOverride.Caption = "텍스트 삽입";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
    }

    private static void TextBox_DragOver(object sender, DragEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[TextBoxDrop] DragOver 이벤트 발생");

        if (!CanAccept(sender))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        // 텍스트 데이터를 포함하는 경우 드롭 허용
        e.AcceptedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;

        if (e.DragUIOverride != null)
        {
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private static async void TextBox_Drop(object sender, DragEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[TextBoxDrop] Drop 이벤트 발생");

        if (CanAccept(sender) && sender is TextBox textBox)
        {
            // 텍스트 데이터 가져오기
            if (e.DataView.Contains(StandardDataFormats.Text))
            {
                System.Diagnostics.Debug.WriteLine("[TextBoxDrop] 텍스트 데이터 감지됨");
                string text = await e.DataView.GetTextAsync();
                System.Diagnostics.Debug.WriteLine($"[TextBoxDrop] 드롭된 텍스트: {text}");

                // 텍스트 삽입 위치 결정
                int selectionStart = textBox.SelectionStart;

                if (textBox.SelectionLength > 0)
                {
                    // 선택 영역이 있으면 대체
                    textBox.SelectedText = text;
                }
                else
                {
                    // 선택 영역이 없으면 커서 위치에 삽입
                    string currentText = textBox.Text ?? string.Empty;
                    textBox.Text = currentText.Insert(selectionStart, text);
                    textBox.SelectionStart = selectionStart + text.Length;
                }

                System.Diagnostics.Debug.WriteLine("[TextBoxDrop] 텍스트 삽입 완료");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[TextBoxDrop] 텍스트 데이터 없음");
            }
        }
        e.Handled = true;
    }
}
