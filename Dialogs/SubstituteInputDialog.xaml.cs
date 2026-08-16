using System;
using Microsoft.UI.Xaml.Controls;

namespace NewSchool.Dialogs;

/// <summary>
/// 대강 입력 — 남의 수업에 대신 들어가는 경우 과목명을 직접 받는다.
/// 내가 개설한 수업이 아니라 <c>Course</c> 에 없으므로 FK 로는 가리킬 수 없다.
/// </summary>
public sealed partial class SubstituteInputDialog : ContentDialog
{
    public string Subject { get; private set; } = string.Empty;
    public string Room { get; private set; } = string.Empty;
    public string Memo { get; private set; } = string.Empty;

    public SubstituteInputDialog(DateTime date, int period, string? subject = null, string? room = null, string? memo = null)
    {
        this.InitializeComponent();

        TxtSlot.Text = $"{date:M월 d일(ddd)} {period}교시";
        TxtSubject.Text = subject ?? string.Empty;
        TxtRoom.Text = room ?? string.Empty;
        TxtMemo.Text = memo ?? string.Empty;

        PrimaryButtonClick += OnPrimaryButtonClick;
        Opened += (_, _) => TxtSubject.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var subject = TxtSubject.Text?.Trim() ?? string.Empty;

        // 과목명이 없으면 저장해 봐야 휴강과 구분되지 않는다.
        if (subject.Length == 0)
        {
            ErrorBar.Message = "과목명을 적어 주세요. 비워 두면 휴강과 구분되지 않습니다.";
            ErrorBar.IsOpen = true;
            args.Cancel = true;
            return;
        }

        Subject = subject;
        Room = TxtRoom.Text?.Trim() ?? string.Empty;
        Memo = TxtMemo.Text?.Trim() ?? string.Empty;
    }
}
