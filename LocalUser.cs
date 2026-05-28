namespace SecureTokenTool.Models;

/// <summary>端末上のローカルユーザ1件の情報。</summary>
public sealed record LocalUser(
    string Username,
    bool HasSecureToken,
    bool IsAdmin);
