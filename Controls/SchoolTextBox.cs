using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace NewSchool.Controls;

/// <summary>
/// 한국어 IME 자동 전환 및 Drag & Drop을 지원하는 커스텀 TextBox
/// </summary>
public partial class SchoolTextBox : TextBox
{
    #region Win32 API

    [LibraryImport("imm32.dll")]
    private static partial IntPtr ImmGetContext(IntPtr hWnd);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmSetConversionStatus(IntPtr hIMC, uint fdwConversion, uint fdwSentence);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImmGetConversionStatus(IntPtr hIMC, out uint lpfdwConversion, out uint lpfdwSentence);

    // IME Conversion Mode 상수
    private const uint IME_CMODE_NATIVE = 0x0001;      // 한글 모드
    private const uint IME_CMODE_KATAKANA = 0x0002;    // 카타카나
    private const uint IME_CMODE_FULLSHAPE = 0x0008;   // 전각
    private const uint IME_CMODE_ROMAN = 0x0010;       // 로마자
    private const uint IME_CMODE_HANJACONVERT = 0x0040; // 한자 변환

    #endregion

    #region Dependency Properties

    /// <summary>
    /// 포커스 시 한국어 IME로 전환할지 여부
    /// </summary>
    public static readonly DependencyProperty UseKoreanImeProperty =
        DependencyProperty.Register(
            nameof(UseKoreanIme),
            typeof(bool),
            typeof(SchoolTextBox),
            new PropertyMetadata(true));

    public bool UseKoreanIme
    {
        get => (bool)GetValue(UseKoreanImeProperty);
        set => SetValue(UseKoreanImeProperty, value);
    }

    /// <summary>
    /// 텍스트 드래그 허용 여부
    /// </summary>
    public static readonly DependencyProperty AllowTextDragProperty =
        DependencyProperty.Register(
            nameof(AllowTextDrag),
            typeof(bool),
            typeof(SchoolTextBox),
            new PropertyMetadata(true));

    public bool AllowTextDrag
    {
        get => (bool)GetValue(AllowTextDragProperty);
        set => SetValue(AllowTextDragProperty, value);
    }

    #endregion

    #region Constructor

    public SchoolTextBox()
    {
        // 기본 설정
        this.AllowDrop = true;
        this.CanBeScrollAnchor = true;

        // 이벤트 연결
        this.GotFocus += OnGotFocus;
        this.DragOver += OnDragOver;
        this.Drop += OnDrop;
        this.DragStarting += OnDragStarting;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 포커스 획득 시 한국어 IME로 전환
    /// </summary>
    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (!UseKoreanIme) return;

        try
        {
            SetKoreanIme();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SchoolTextBox] IME 전환 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 드래그 오버 처리
    /// </summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "여기에 놓기";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    /// <summary>
    /// 드롭 처리
    /// </summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            try
            {
                var text = await e.DataView.GetTextAsync();
                
                // 현재 선택 위치에 텍스트 삽입
                var selectionStart = this.SelectionStart;
                var currentText = this.Text ?? string.Empty;
                
                // 선택된 텍스트가 있으면 대체, 없으면 삽입
                if (this.SelectionLength > 0)
                {
                    this.Text = currentText.Remove(selectionStart, this.SelectionLength)
                                          .Insert(selectionStart, text);
                }
                else
                {
                    this.Text = currentText.Insert(selectionStart, text);
                }
                
                // 커서를 삽입된 텍스트 뒤로 이동
                this.SelectionStart = selectionStart + text.Length;
                this.SelectionLength = 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SchoolTextBox] Drop 오류: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 드래그 시작 처리 (선택된 텍스트 드래그)
    /// </summary>
    private void OnDragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if (!AllowTextDrag) return;

        var selectedText = this.SelectedText;
        if (!string.IsNullOrEmpty(selectedText))
        {
            e.Data.SetText(selectedText);
            e.Data.RequestedOperation = DataPackageOperation.Copy;
        }
    }

    #endregion

    #region IME Methods

    /// <summary>
    /// IME를 한국어 모드로 설정
    /// </summary>
    private void SetKoreanIme()
    {
        var hwnd = GetWindowHandle();
        if (hwnd == IntPtr.Zero) return;

        var hIMC = ImmGetContext(hwnd);
        if (hIMC == IntPtr.Zero) return;

        try
        {
            // 현재 상태 가져오기
            if (ImmGetConversionStatus(hIMC, out uint currentConversion, out uint currentSentence))
            {
                // 한글 네이티브 모드로 설정
                uint newConversion = IME_CMODE_NATIVE;
                ImmSetConversionStatus(hIMC, newConversion, currentSentence);
            }
        }
        finally
        {
            ImmReleaseContext(hwnd, hIMC);
        }
    }

    /// <summary>
    /// 현재 윈도우 핸들 가져오기
    /// </summary>
    private IntPtr GetWindowHandle()
    {
        try
        {
            var window = App.MainWindow;
            if (window != null)
            {
                return WindowNative.GetWindowHandle(window);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SchoolTextBox] GetWindowHandle 오류: {ex.Message}");
        }
        
        return IntPtr.Zero;
    }

    #endregion
}
