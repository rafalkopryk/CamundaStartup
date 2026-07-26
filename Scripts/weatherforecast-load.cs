#!/usr/bin/env dotnet
#:property TargetFramework=net10.0

using System.Diagnostics;
using System.Net;
using System.Text.Json;

var baseUrl = args.Length > 0 ? args[0] : "https://localhost:7230";
var totalInstances = args.Length > 1 ? int.Parse(args[1]) : 50_000;
var waitForCompletion = args.Length <= 2 || bool.Parse(args[2]);
var instancesPerRequest = args.Length > 3 ? int.Parse(args[3]) : 10;
var batchDelayMs = args.Length > 4 ? int.Parse(args[4]) : 50;

if (instancesPerRequest <= 1)
    throw new ArgumentOutOfRangeException(nameof(instancesPerRequest), "Instances per request must be greater than one.");
if (batchDelayMs < 0)
    throw new ArgumentOutOfRangeException(nameof(batchDelayMs), "Batch delay must not be negative.");

var totalRequests = (int)Math.Ceiling((double)totalInstances / instancesPerRequest);

var handler = new SocketsHttpHandler
{
    SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
};

using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };

Console.WriteLine($"Target:        {baseUrl}");
Console.WriteLine($"Instances:     {totalInstances:N0}");
Console.WriteLine($"Requests:      {totalRequests:N0}");
Console.WriteLine($"Per request:   {instancesPerRequest:N0}");
Console.WriteLine($"Batch delay:   {batchDelayMs:N0} ms");
Console.WriteLine($"E2E verify:    {waitForCompletion}");
Console.WriteLine();

var success = 0;
var failed = 0;
var completed = 0;
var loadTestId = Guid.CreateVersion7().ToString();
var sw = Stopwatch.StartNew();
var startDate = new DateOnly(2025, 1, 1);

for (var i = 0; i < totalRequests; i++)
{
    var count = Math.Min(instancesPerRequest, totalInstances - i * instancesPerRequest);
    var date = startDate.AddDays(i % 3650);
    try
    {
        using var resp = await http.PostAsync(
            $"/jobs/weatherforecast/{date:yyyy-MM-dd}?loadTestId={loadTestId}&count={count}",
            content: null);
        if (resp.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK)
        {
            success += count;
        }
        else
            failed += count;
    }
    catch
    {
        failed += count;
    }

    var done = success + failed;
    if (done % 1000 == 0)
        Console.WriteLine($"{done,8:N0} / {totalInstances:N0}  ok={success:N0}  fail={failed:N0}  elapsed={sw.Elapsed:mm\\:ss}");

    if (batchDelayMs > 0)
        await Task.Delay(batchDelayMs);
}

if (waitForCompletion && failed == 0)
{
    Console.WriteLine("All instances accepted. Waiting for end-to-end completion...");
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));

    while (completed < totalInstances)
    {
        using var response = await http.GetAsync($"/load-tests/{loadTestId}", timeout.Token);
        response.EnsureSuccessStatusCode();
        await using var responseBody = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var status = await JsonDocument.ParseAsync(responseBody, cancellationToken: timeout.Token);
        var started = status.RootElement.GetProperty("started").GetInt32();
        completed = status.RootElement.GetProperty("completed").GetInt32();

        Console.WriteLine(
            $"started={started,8:N0}  completed={completed,8:N0} / {totalInstances:N0}  elapsed={sw.Elapsed:mm\\:ss}");

        if (completed < totalInstances)
            await Task.Delay(TimeSpan.FromSeconds(5), timeout.Token);
    }
}

sw.Stop();

Console.WriteLine();
Console.WriteLine($"Done in {sw.Elapsed:mm\\:ss\\.fff}");
Console.WriteLine($"  ok      = {success:N0}");
Console.WriteLine($"  failed  = {failed:N0}");
Console.WriteLine($"  complete= {completed:N0}");
Console.WriteLine($"  instances/s = {totalInstances / sw.Elapsed.TotalSeconds:N0}");

return failed == 0 && (!waitForCompletion || completed == totalInstances) ? 0 : 1;
