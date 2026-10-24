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
