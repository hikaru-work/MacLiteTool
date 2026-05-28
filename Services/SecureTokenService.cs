using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SecureTokenTool.Configuration;
using SecureTokenTool.Models;

namespace SecureTokenTool.Services;

public enum TokenState { Enabled, Disabled, Unknown }

/// <summary>
/// macOS の Secure Token / FileVault / ユーザ管理コマンドをラップするサービス。
/// 管理者権限が必要な操作は、共通管理者アカウント(macadmin)の認証情報で実行する。
/// </summary>
public sealed class SecureTokenService
{
    private const string Sysadminctl = "/usr/sbin/sysadminctl";
    private const string Diskutil = "/usr/sbin/diskutil";
    private const string Fdesetup = "/usr/bin/fdesetup";
    private const string Dscl = "/usr/bin/dscl";
    private const string Dseditgroup = "/usr/sbin/dseditgroup";
    private const string Osascript = "/usr/bin/osascript";

    /// <summary>
    /// 端末上のローカルユーザ(UID 500 以上、システムユーザを除く)を列挙し、
    /// 各ユーザの Secure Token 状態・管理者権限を取得する。
    /// </summary>
    public async Task<IReadOnlyList<LocalUser>> ListLocalUsersAsync()
    {
        var r = await ProcessRunner.RunAsync(
            Dscl, new[] { ".", "list", "/Users", "UniqueID" });

        var names = new List<string>();
        foreach (var line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            var name = parts[0];
            if (name.StartsWith('_')) continue;
            if (!int.TryParse(parts[^1], out var uid)) continue;
            if (uid < 500) continue;

            names.Add(name);
        }

        var tasks = names.Select(async name =>
        {
            var (state, _) = await GetSecureTokenStatusAsync(name);
            var isAdmin = await IsAdminAsync(name);
            return new LocalUser(name, state == TokenState.Enabled, isAdmin);
        });

        var users = await Task.WhenAll(tasks);
        return users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>指定ユーザが admin グループに属するか判定する。</summary>
    private static async Task<bool> IsAdminAsync(string user)
    {
        var r = await ProcessRunner.RunAsync(
            Dseditgroup, new[] { "-o", "checkmember", "-m", user, "admin" });

        return r.Combined.TrimStart()
            .StartsWith("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>指定ユーザの Secure Token 状態を取得する。</summary>
    public async Task<(TokenState State, string Raw)> GetSecureTokenStatusAsync(string user)
    {
        var r = await ProcessRunner.RunAsync(
            Sysadminctl, new[] { "-secureTokenStatus", user });

        var text = r.Combined;
        if (text.Contains("ENABLED", StringComparison.OrdinalIgnoreCase))
            return (TokenState.Enabled, text);
        if (text.Contains("DISABLED", StringComparison.OrdinalIgnoreCase))
            return (TokenState.Disabled, text);
        return (TokenState.Unknown, text);
    }

    public async Task<string> GetFileVaultStatusAsync()
    {
        var r = await ProcessRunner.RunAsync(Fdesetup, new[] { "status" });
        return r.Combined;
    }

    public async Task<string> GetVolumeUsersAsync()
    {
        var r = await ProcessRunner.RunAsync(
            Diskutil, new[] { "apfs", "listUsers", "/" });
        return r.Combined;
    }

    /// <summary>
    /// dscl -authonly でアカウントのパスワードを検証する(認証のみ、変更は行わない)。
    /// 終了コード 0 = パスワード一致。
    ///
    /// 注: パスワードは引数で渡す。dscl -authonly の「パスワード省略 → 標準入力」
    ///     形式は制御端末を持たないプロセスで挙動が不安定(ハングの恐れ)なため、
    ///     確実に動作する引数渡しを採用している。検証は数百ミリ秒で完了し、
    ///     ps への露出はその間のみ。実際の付与(sysadminctl)は stdin 渡しを維持。
    /// </summary>
    public async Task<bool> VerifyPasswordAsync(string user, string password)
    {
        if (!IsValidUsername(user) || string.IsNullOrEmpty(password))
            return false;

        var r = await ProcessRunner.RunAsync(
            Dscl, new[] { "/Local/Default", "-authonly", user, password });
        return r.ExitCode == 0;
    }

    /// <summary>
    /// 子アカウントに Secure Token を付与する。
    /// パスワードは引数に出さず stdin 経由で渡す
    /// (順序は -password(子) → -adminPassword(親))。
    /// </summary>
    public async Task<ProcessResult> GrantSecureTokenAsync(
        string childUser, string childPassword,
        string parentUser, string parentPassword)
    {
        var args = new[]
        {
            "-secureTokenOn", childUser,
            "-password", "-",
            "-adminUser", parentUser,
            "-adminPassword", "-",
        };
        var stdin = new[] { childPassword, parentPassword };
        return await ProcessRunner.RunAsync(Sysadminctl, args, stdin);
    }

    /// <summary>指定ユーザを admin グループへ追加する(macadmin 権限で実行)。</summary>
    public Task<ProcessResult> PromoteToAdminAsync(string user) =>
        RunDseditgroupAsAdminAsync(user, add: true);

    /// <summary>指定ユーザを admin グループから削除する(macadmin 権限で実行)。</summary>
    public Task<ProcessResult> DemoteFromAdminAsync(string user) =>
        RunDseditgroupAsAdminAsync(user, add: false);

    private static async Task<ProcessResult> RunDseditgroupAsAdminAsync(string user, bool add)
    {
        if (!IsValidUsername(user))
        {
            return new ProcessResult
            {
                ExitCode = 1,
                StdErr = $"不正なユーザ名のため処理を中止しました: {user}",
            };
        }

        if (!AdminAccount.IsConfigured)
        {
            return new ProcessResult
            {
                ExitCode = 1,
                StdErr = "管理者アカウント(macadmin)のパスワードが未設定です。" +
                         "Configuration/AdminAccount.cs を編集してください。",
            };
        }

        var op = add ? "-a" : "-d";
        var cmd = $"{Dseditgroup} -o edit {op} {user} -t user admin";
        return await RunShellAsAdminAsync(cmd);
    }

    /// <summary>
    /// 共通管理者アカウント(macadmin)の認証情報で root 権限のシェルコマンドを実行する。
    /// AppleScript は osascript の標準入力経由で渡すため、
    /// パスワードはプロセス引数(ps で見える場所)には出ない。
    /// 認証情報を渡しているため、macOS の認証ダイアログは表示されない。
    /// </summary>
    private static async Task<ProcessResult> RunShellAsAdminAsync(string shellCommand)
    {
        var script =
            $"do shell script \"{EscapeForAppleScript(shellCommand)}\" " +
            $"user name \"{EscapeForAppleScript(AdminAccount.Username)}\" " +
            $"password \"{EscapeForAppleScript(AdminAccount.Password)}\" " +
            $"with administrator privileges";

        // -e ではなく stdin でスクリプトを渡す(パスワードを ps に出さないため)
        return await ProcessRunner.RunAsync(
            Osascript, Array.Empty<string>(), new[] { script });
    }

    /// <summary>AppleScript 文字列リテラル用のエスケープ。</summary>
    private static string EscapeForAppleScript(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>シェルへ渡す前のユーザ名検証(コマンドインジェクション対策)。</summary>
    private static bool IsValidUsername(string user) =>
        !string.IsNullOrEmpty(user) &&
        user.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');
}
