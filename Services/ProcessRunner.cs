using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecureTokenTool.Services;

/// <summary>外部コマンドの実行結果。</summary>
public sealed class ProcessResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;

    /// <summary>
    /// sysadminctl は出力の多くを stderr に書くため、
    /// stdout と stderr を結合して扱う。
    /// </summary>
    public string Combined =>
        string.Join('\n',
            new[] { StdOut.Trim(), StdErr.Trim() }
                .Where(s => s.Length > 0));
}

public static class ProcessRunner
{
    /// <summary>
    /// コマンドを実行する。
    /// stdinLines を渡すと標準入力へ1行ずつ書き込む。
    /// パスワードを引数(プロセス一覧から見える場所)に出さず、
    /// stdin 経由で渡すために使用する。
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        IEnumerable<string>? stdinLines = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinLines is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderr.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (stdinLines is not null)
        {
            foreach (var line in stdinLines)
                await process.StandardInput.WriteLineAsync(line);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync();

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString(),
        };
    }
}
