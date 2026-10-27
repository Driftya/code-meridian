using System.Text.Json;
using CodeMeridian.PowerShellIndexer;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (args is ["--describe-worker"])
    {
        Console.WriteLine(JsonSerializer.Serialize(PowerShellWorkerIdentity.Read(), options));
        return 0;
    }
    if (args is not ["analyze", var requestPath])
    {
        Console.Error.WriteLine("Usage: CodeMeridian.PowerShellIndexer --describe-worker | analyze <request.json>");
        return 1;
    }
    await using var requestStream = File.OpenRead(requestPath);
    var requestBytes = new byte[1048577];
    var count = 0;
    while (count < requestBytes.Length)
    {
        var read = await requestStream.ReadAsync(requestBytes.AsMemory(count), cancellation.Token);
        if (read == 0) break;
        count += read;
    }
    if (count > 1048576) throw new ArgumentException("Request exceeds the 1 MiB protocol budget.");
    // UTF-8 request JSON has no source-code decoding or evaluation step.
    var offset = requestBytes.AsSpan(0, count).StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
    var request = JsonSerializer.Deserialize<PowerShellAnalysisRequest>(requestBytes.AsSpan(offset, count - offset), options)
        ?? throw new ArgumentException("An analysis request is required.");
    var report = await new PowerShellAnalyzer().AnalyzeAsync(request, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(report, options));
    return report.Status == "complete" ? 0 : 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("PowerShell analysis cancelled.");
    return 2;
}
catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
{
    Console.Error.WriteLine("PowerShell analysis request failed validation or could not be read.");
    return 1;
}
