using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using NodeTunnel.TCP;

namespace NodeTunnel.HTTP;

public class StatusServer(TCPHandler tcp) {
    private readonly HttpListener _http = new();
    private readonly CancellationTokenSource _ct = new();
    
    public async Task StartAsync() {
        _http.Prefixes.Add("http://*:8080/");
        _http.Start();

        while (!_ct.Token.IsCancellationRequested) {
            try {
                var ctx = await _http.GetContextAsync();
                _ = Task.Run(() => HandleRequest(ctx));
            }
            catch (ObjectDisposedException) {
                break;
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx) {
        var req = ctx.Request;
        var res = ctx.Response;
        
        res.Headers.Add("Access-Control-Allow-Origin", "*");
        res.ContentType = "application/json";

        try {
            var stats = GetServerStats();
            var json = JsonSerializer.Serialize(stats);
            var buffer = Encoding.UTF8.GetBytes(json);

            res.ContentLength64 = buffer.Length;
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        catch (Exception ex) {
            res.StatusCode = 500;
            var err = Encoding.UTF8.GetBytes($"{{\"error\": \"{ex.Message}\"}}");
            await res.OutputStream.WriteAsync(err, 0, err.Length);
        }
        finally {
            res.OutputStream.Close();
        }
    }

    private object GetServerStats() {
        var process = Process.GetCurrentProcess();
        var memUsage = process.WorkingSet64 / (1024 * 1024);
        var cpuUsage = GetCpuUsage();
        
        return new {
            status = "online",
            timestamp = DateTime.UtcNow,
            totalRooms = tcp.GetTotalRooms(),
            totalPeers = tcp.GetTotalPeers(),
            memoryUsageMB = memUsage,
            cpuUsagePercent = Math.Round(cpuUsage, 1)
        };
    }

    private double GetCpuUsage() {
        var process = Process.GetCurrentProcess();

        var startTime = DateTime.UtcNow;
        var startCpuUsage = process.TotalProcessorTime;
        
        Thread.Sleep(100);

        var endTime = DateTime.UtcNow;
        var endCpuUsage = process.TotalProcessorTime;

        var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
        var totalMsPassed = (endTime - startTime).TotalMilliseconds;
        var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

        return cpuUsageTotal * 100;
    }
}