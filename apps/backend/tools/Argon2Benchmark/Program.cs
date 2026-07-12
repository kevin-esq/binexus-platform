using System.Diagnostics;
using Binexus.Modules.Identity.Application;
using Binexus.Modules.Identity.Infrastructure;

/// <summary>
/// Manual Argon2 benchmark — not a CI unit test. Run:
///   dotnet run --project apps/backend/tools/Argon2Benchmark -c Release
/// </summary>
var password = IdentitySeedDefaults.KnownInsecureDemoPassword;
var hasher = new Argon2PasswordHasher();
var hash = await hasher.HashAsync(password);

Console.WriteLine("Argon2id benchmark (Isopoh)");
Console.WriteLine($"parameters: m={Argon2PasswordHasher.MemoryCost} t={Argon2PasswordHasher.TimeCost} p={Argon2PasswordHasher.Parallelism}");
Console.WriteLine($"hash prefix: {hash[..Math.Min(48, hash.Length)]}...");
Console.WriteLine();

foreach (var concurrency in new[] { 1, 5, 10, 20 })
{
    // Warmup
    _ = await hasher.VerifyAsync(hash, password);

    var samples = new List<long>(concurrency);
    var sw = Stopwatch.StartNew();
    var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
    {
        var local = Stopwatch.StartNew();
        await hasher.VerifyAsync(hash, password);
        local.Stop();
        lock (samples)
        {
            samples.Add(local.ElapsedMilliseconds);
        }
    })).ToArray();
    await Task.WhenAll(tasks);
    sw.Stop();

    samples.Sort();
    var p50 = samples[samples.Count / 2];
    var p95Index = (int)Math.Clamp(Math.Ceiling(samples.Count * 0.95) - 1, 0, samples.Count - 1);
    var p95 = samples[p95Index];
    Console.WriteLine(
        $"concurrency={concurrency,2} wall={sw.ElapsedMilliseconds,5}ms p50={p50,4}ms p95={p95,4}ms peakWorkingSet≈{Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)}MB");
}

Console.WriteLine();
using (var cts = new CancellationTokenSource())
{
    cts.Cancel();
    try
    {
        await hasher.VerifyAsync(hash, password, cts.Token);
        Console.WriteLine("cancellation: unexpected success");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("cancellation: OperationCanceledException (ok)");
    }
}

Console.WriteLine($"malformed hash verify => {await hasher.VerifyAsync("%%%", password)}");
Console.WriteLine("done");
