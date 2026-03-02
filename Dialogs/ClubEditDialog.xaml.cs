using System;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Repositories;

namespace NewSchool.Dialogs;

/// <summary>
/// 동아리 추가/수정 다이얼로그
/// </summary>
public sealed partial class ClubEditDialog : ContentDialog
{
    private readonly Club? _existingClub;
    private readonly string _schoolCode;
    private readonly string _teacherId;
    private readonly int _year;
    private readonly bool _isEditMode;

    /// <summary>
    /// 새 동아리 추가
    /// </summary>
    public ClubEditDialog(string schoolCode, string teacherId, int year)
    {
        this.InitializeComponent();

        _schoolCode = schoolCode;
        _teacherId = teacherId;
        _year = year;
        _isEditMode = false;

        Title = "동아리 추가";
    }

    /// <summary>
    /// 기존 동아리 수정
    /// </summary>
    public ClubEditDialog(Club club)
    {
        this.InitializeComponent();

        _existingClub = club;
        _schoolCode = club.SchoolCode;
        _teacherId = club.TeacherID;
        _year = club.Year;
        _isEditMode = true;

        Title = "동아리 수정";

        // 기존 데이터 로드
        TxtClubName.Text = club.ClubName;
        TxtActivityRoom.Text = club.ActivityRoom;
        TxtRemark.Text = club.Remark;
    }

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        try
        {
            // 유효성 검사
            if (string.IsNullOrWhiteSpace(TxtClubName.Text))
            {
                ShowError("동아리명을 입력해주세요.");
                args.Cancel = true;
                return;
            }

            // Club 객체 생성/업데이트
            var club = _isEditMode ? _existingClub! : new Club();

            club.SchoolCode = _schoolCode;
            club.TeacherID = _teacherId;
            club.Year = _year;
            club.ClubName = TxtClubName.Text.Trim();
            club.ActivityRoom = TxtActivityRoom.Text.Trim();
            club.Remark = TxtRemark.Text.Trim();
            club.UpdatedAt = DateTime.Now;

            // 저장
            using var repo = new ClubRepository(SchoolDatabase.DbPath);

            if (_isEditMode)
            {
                await repo.UpdateAsync(club);
            }
            else
            {
                club.CreatedAt = DateTime.Now;
                await repo.CreateAsync(club);
            }
        }
        catch (Exception ex)
        {
            ShowError($"저장 중 오류가 발생했습니다.\n{ex.Message}");
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// 에러 메시지 표시
    /// </summary>
    private void ShowError(string message)
    {
        ErrorInfoBar.Message = message;
        ErrorInfoBar.IsOpen = true;
    }
}
