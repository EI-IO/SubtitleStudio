// 命令行入口：只做退出码与顶层异常兜底，具体子命令在 CliRunner。
using SubtitleStudio.Core.Asr;
using SubtitleStudio.Cli;

try
{
    return await CliRunner.RunAsync(args);
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"失败：{TlsDiagnostics.Describe(ex)}");
    return 1;
}
