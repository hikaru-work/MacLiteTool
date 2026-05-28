namespace SecureTokenTool.Configuration;

/// <summary>
/// 管理者権限が必要な操作(親アカウントの昇格 / 降格)で使用する
/// 共通管理者アカウントの認証情報。
///
/// ⚠ セキュリティ注意:
///   .NET アセンブリは逆コンパイル(ILSpy / dotPeek 等)で容易に解析でき、
///   ここに書いたパスワードは平文同然で抽出できます。
///   本アプリの配布先は信頼できる範囲(社内ヘルプデスク端末など)に限定してください。
///   より安全にするには Jamf Pro の LAPS でパスワードをローテーションし、
///   API 経由で取得する方式を推奨します。
/// </summary>
public static class AdminAccount
{
    /// <summary>昇格 / 降格に使用する共通管理者アカウント名。</summary>
    public const string Username = "macadmin";

    // ▼▼▼ 配布前に、実際の macadmin のパスワードへ置き換えてください ▼▼▼
    public const string Password = "REPLACE_WITH_MACADMIN_PASSWORD";
    // ▲▲▲

    /// <summary>パスワードが設定済みか(プレースホルダのままでないか)。</summary>
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Password) && !Password.StartsWith("REPLACE_");
}
