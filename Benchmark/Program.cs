using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Dapper;

if (args.Length < 1)
{
    Console.WriteLine("Usage: Benchmark <db_path>");
    return;
}

var dbPath = args[0];
var connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";

Console.WriteLine($"Database: {dbPath}");
Console.WriteLine();

// Get scan ID
using var conn = new SqliteConnection(connectionString);
conn.Open();
var scanId = conn.QuerySingle<long>("SELECT MAX(scan_id) FROM files");
Console.WriteLine($"Scan ID: {scanId}");
Console.WriteLine();

// Benchmark 1: Get duplicate sizes
Console.WriteLine("=== Benchmark 1: Find duplicate sizes ===");
var sw = Stopwatch.StartNew();
var sizes = await conn.QueryAsync<long>(@"
    SELECT size
    FROM files
    WHERE scan_id = @scanId
    GROUP BY size
    HAVING COUNT(*) > 1
", new { scanId });
sw.Stop();

var sizeList = sizes.ToList();
Console.WriteLine($"Found: {sizeList.Count:N0} duplicate size groups");
Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
Console.WriteLine();

// Limit to first 1000 for faster testing
var testSizes = sizeList.Take(1000).ToList();
Console.WriteLine($"Testing with first {testSizes.Count} sizes for comparison...");
Console.WriteLine();

// Benchmark 2: Sequential fetch
Console.WriteLine("=== Benchmark 2: Fetch files SEQUENTIALLY ===");
sw.Restart();
var sequentialGroups = 0;
foreach (var size in testSizes)
{
    var files = await conn.QueryAsync<dynamic>(
        "SELECT * FROM files WHERE size = @size AND scan_id = @scanId",
        new { size, scanId });

    if (files.Count() > 1)
    {
        sequentialGroups++;
    }
}
sw.Stop();
var sequentialTime = sw.ElapsedMilliseconds;

Console.WriteLine($"Processed: {testSizes.Count:N0} queries");
Console.WriteLine($"Found: {sequentialGroups:N0} groups");
Console.WriteLine($"Time: {sequentialTime}ms ({sw.Elapsed.TotalSeconds:F2}s)");
Console.WriteLine($"Avg per query: {(double)sequentialTime / testSizes.Count:F2}ms");
Console.WriteLine();

// Benchmark 3: Parallel fetch
Console.WriteLine("=== Benchmark 3: Fetch files IN PARALLEL ===");
sw.Restart();
var tasks = testSizes.Select(async size =>
{
    using var connection = new SqliteConnection(connectionString);
    connection.Open();
    var files = await connection.QueryAsync<dynamic>(
        "SELECT * FROM files WHERE size = @size AND scan_id = @scanId",
        new { size, scanId });

    return files.Count() > 1 ? 1 : 0;
});
var results = await Task.WhenAll(tasks);
var parallelGroups = results.Sum();
sw.Stop();
var parallelTime = sw.ElapsedMilliseconds;

Console.WriteLine($"Processed: {testSizes.Count:N0} queries");
Console.WriteLine($"Found: {parallelGroups:N0} groups");
Console.WriteLine($"Time: {parallelTime}ms ({sw.Elapsed.TotalSeconds:F2}s)");
Console.WriteLine($"Avg per query: {(double)parallelTime / testSizes.Count:F2}ms");
Console.WriteLine();

// Summary
Console.WriteLine("=== Summary ===");
Console.WriteLine($"Speedup: {(double)sequentialTime / parallelTime:F1}x faster");
Console.WriteLine();
Console.WriteLine("For all {0:N0} duplicate sizes:", sizeList.Count);
Console.WriteLine($"  Sequential (estimated): {(double)sequentialTime / testSizes.Count * sizeList.Count / 1000:F1}s");
Console.WriteLine($"  Parallel (estimated): {(double)parallelTime / testSizes.Count * sizeList.Count / 1000:F1}s");
