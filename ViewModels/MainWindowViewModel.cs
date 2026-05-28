using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureTokenTool.Configuration;
using SecureTokenTool.Models;
using SecureTokenTool.Services;

namespace SecureTokenTool.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly SecureTokenService _service = new();

    /// <summary>親アカウント候補(Secure Token 保有者)。</summary>
    public ObservableCollection<LocalUser> TokenHolders { get; } = new();

    /// <summary>子アカウント候補(Secure Token 未保有者)。</summary>
    public ObservableCollection<LocalUser> NonTokenUsers { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GrantCommand))]
    [NotifyPropertyChangedFor(nameof(ParentNeedsPromotion))]
    private LocalUser? _selectedParent;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GrantCommand))]
    private LocalUser? _selectedChild;

    [ObservableProperty] private string _parentPassword = string.Empty;
    [ObservableProperty] private string _childPassword = string.Empty;
    [ObservableProperty] private string _log = string.Empty;
    [ObservableProperty] private string _statusText = "ユーザ一覧を読み込み中...";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadUsersCommand))]
    [NotifyCanExecuteChangedFor(nameof(GrantCommand))]
    private bool _isBusy;

    /// <summary>選択中の親アカウントが標準ユーザ(=昇格が必要)か。</summary>
    public bool ParentNeedsPromotion => SelectedParent is { IsAdmin: false };

    private bool CanLoad => !IsBusy;
    private bool CanGrant => !IsBusy && SelectedParent is not null && SelectedChild is not null;

    public MainWindowViewModel()
    {
        _ = LoadUsersAsync();
    }

    private void AppendLog(string line)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        Log += $"[{ts}] {line}\n";
    }

    /// <summary>端末上のユーザを再列挙し、Token保有/未保有のコンボへ振り分ける。</summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadUsersAsync()
    {
        IsBusy = true;
        StatusText = "ユーザ一覧を取得中...";
        try
        {
            AppendLog("=== ユーザ一覧を取得 ===");
            var users = await RefreshUserListsAsync();

            AppendLog($"検出: 全{users.Count}件 / Token保有 {TokenHolders.Count} / 未保有 {NonTokenUsers.Count}");
            foreach (var u in users)
                AppendLog($"  {u.Username} | Token={(u.HasSecureToken ? "有効" : "無効")} | 管理者={(u.IsAdmin ? "はい" : "いいえ")}");

            if (TokenHolders.Count == 0)
                AppendLog("⚠ Secure Token 保有者が0件です。この端末には付与元に使えるアカウントがありません。");
            if (NonTokenUsers.Count == 0)
                AppendLog("ℹ Token 未保有のユーザはいません。付与対象がありません。");

            AppendLog(string.Empty);
            StatusText = TokenHolders.Count > 0 ? "準備完了" : "付与元アカウントなし";
        }
        catch (Exception ex)
        {
            AppendLog($"エラー: {ex.Message}");
            StatusText = "エラー";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 親アカウントの認証情報で子アカウントに Secure Token を付与する。
    /// 実処理の前にパスワードを事前検証し、誤りがあれば昇格に入る前に中止する。
    /// 親が標準ユーザの場合は macadmin 権限で一時的に管理者へ昇格し、
    /// 完了後(失敗時も)標準ユーザへ戻す。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGrant))]
    private async Task GrantAsync()
    {
        if (SelectedParent is null || SelectedChild is null)
            return;
        if (string.IsNullOrEmpty(ParentPassword) || string.IsNullOrEmpty(ChildPassword))
        {
            AppendLog("⚠ 親・子アカウントのパスワードを入力してください。");
            return;
        }

        var parent = SelectedParent.Username;
        var child = SelectedChild.Username;
        var parentWasStandard = !SelectedParent.IsAdmin;
        var promoted = false;
        var verified = false;

        IsBusy = true;
        StatusText = "パスワード確認中...";
        try
        {
            // --- 0. パスワード事前チェック(認証のみ。状態は変更しない) ---
            AppendLog("=== パスワード事前チェック ===");

            if (parentWasStandard && !AdminAccount.IsConfigured)
            {
                AppendLog($"❌ 管理者アカウント({AdminAccount.Username})のパスワードが未設定です。Configuration/AdminAccount.cs を編集してください。");
                StatusText = "設定エラー";
                return;
            }

            var allOk = true;

            // 親が標準ユーザのときだけ macadmin の検証が必要(昇格に使うため)
            if (parentWasStandard)
            {
                var okAdmin = await _service.VerifyPasswordAsync(
                    AdminAccount.Username, AdminAccount.Password);
                AppendLog($"  管理者 [{AdminAccount.Username}]: {(okAdmin ? "OK" : "NG")}");
                if (!okAdmin) allOk = false;
            }

            var okParent = await _service.VerifyPasswordAsync(parent, ParentPassword);
            AppendLog($"  親 [{parent}]: {(okParent ? "OK" : "NG")}");
            if (!okParent) allOk = false;

            var okChild = await _service.VerifyPasswordAsync(child, ChildPassword);
            AppendLog($"  子 [{child}]: {(okChild ? "OK" : "NG")}");
            if (!okChild) allOk = false;

            if (!allOk)
            {
                AppendLog("❌ パスワードに誤りがあります。NG の項目を修正して再実行してください。");
                StatusText = "認証エラー";
                return;   // 昇格・付与には一切入らない(無駄な昇格/降格も発生しない)
            }

            verified = true;
            AppendLog("✓ すべてのパスワードを確認しました。");

            // --- 1. 親が標準ユーザなら macadmin 権限で一時的に管理者へ昇格 ---
            StatusText = "Secure Token 付与中...";
            if (parentWasStandard)
            {
                AppendLog($"親 [{parent}] は標準ユーザです。管理者アカウント({AdminAccount.Username})の権限で一時的に管理者へ昇格します。");
                var promote = await _service.PromoteToAdminAsync(parent);
                if (promote.ExitCode != 0)
                {
                    AppendLog($"❌ 管理者への昇格に失敗しました: {promote.Combined}");
                    StatusText = "昇格失敗";
                    return;
                }
                promoted = true;
                AppendLog($"✓ [{parent}] を管理者へ昇格しました。");
            }

            // --- 2. Secure Token 付与 ---
            AppendLog($"=== Secure Token 付与: [{child}] ← 付与元 [{parent}] ===");
            var result = await _service.GrantSecureTokenAsync(
                child, ChildPassword, parent, ParentPassword);

            if (!string.IsNullOrWhiteSpace(result.Combined))
                AppendLog(result.Combined);
            AppendLog($"sysadminctl 終了コード: {result.ExitCode}");

            // --- 3. 検証(終了コードは信頼できないため状態を再確認) ---
            var (state, raw) = await _service.GetSecureTokenStatusAsync(child);
            AppendLog($"付与後の [{child}] の Secure Token: {state}");

            if (state == TokenState.Enabled)
            {
                AppendLog("✅ Secure Token の付与に成功しました。");
                var fv = await _service.GetFileVaultStatusAsync();
                AppendLog($"FileVault 状態: {fv}");
                AppendLog("ℹ Jamf Pro へ反映するには、対象Macで 'sudo jamf recon' を実行してください。");
                StatusText = "付与成功";
            }
            else
            {
                AppendLog("❌ 付与に失敗しました(パスワードは事前検証済みのため別要因の可能性があります)。");
                AppendLog($"詳細: {raw}");
                StatusText = "付与失敗";
            }
        }
        catch (Exception ex)
        {
            AppendLog($"エラー: {ex.Message}");
            StatusText = "エラー";
        }
        finally
        {
            // --- 4. 昇格していたら必ず標準ユーザへ戻す ---
            if (promoted)
            {
                try
                {
                    AppendLog($"親 [{parent}] を標準ユーザへ戻します。");
                    var demote = await _service.DemoteFromAdminAsync(parent);
                    if (demote.ExitCode == 0)
                    {
                        AppendLog($"✓ [{parent}] を標準ユーザへ戻しました。");
                    }
                    else
                    {
                        AppendLog($"⚠ 標準ユーザへの復帰に失敗しました: {demote.Combined}");
                        AppendLog($"  手動で確認してください: dseditgroup -o checkmember -m {parent} admin");
                        StatusText = "要確認: 親が管理者のまま";
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"⚠ 復帰処理でエラー: {ex.Message}");
                    StatusText = "要確認: 親が管理者のまま";
                }
            }

            // 事前検証を通過し実処理に入った場合のみパスワードをクリアする。
            // 認証エラーで中止した場合は、NG項目だけ直して再実行できるよう保持する。
            if (verified)
            {
                ParentPassword = string.Empty;
                ChildPassword = string.Empty;
            }

            // 一覧を再読み込み(付与済みの子は Token 保有側へ移動)
            try { await RefreshUserListsAsync(); }
            catch { /* 再読み込み失敗は無視 */ }

            IsBusy = false;
            AppendLog(string.Empty);
        }
    }

    /// <summary>
    /// ユーザを再列挙して2つのコンボへ振り分ける。
    /// 直前の選択は可能な限り維持し、無ければ既定値を設定する。
    /// </summary>
    private async Task<IReadOnlyList<LocalUser>> RefreshUserListsAsync()
    {
        var prevParent = SelectedParent?.Username;
        var prevChild = SelectedChild?.Username;

        var users = await _service.ListLocalUsersAsync();

        TokenHolders.Clear();
        NonTokenUsers.Clear();
        foreach (var u in users)
        {
            if (u.HasSecureToken) TokenHolders.Add(u);
            else NonTokenUsers.Add(u);
        }

        SelectedParent =
            TokenHolders.FirstOrDefault(u => u.Username == prevParent)
            ?? TokenHolders.FirstOrDefault(u => u.IsAdmin)
            ?? TokenHolders.FirstOrDefault();

        SelectedChild =
            NonTokenUsers.FirstOrDefault(u => u.Username == prevChild)
            ?? NonTokenUsers.FirstOrDefault();

        return users;
    }
}
