using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

// A tiny in-memory mock of the HTTP API that DataMonger's upload commands target. It accepts
// well-formed JSON, records every request, and can inject failures and latency, so the docs and
// samples can be run end to end without any real service or credentials.
//
//   POST /ver2/{siteId}/activities               track an event (UploadJsonlEvents, PurchaseAnonymous, ...)
//   POST /ver2/{siteId}/subscribers/identify     identify a user (UploadJsonlIdentifies, PurchaseKnown)
//   PUT  /ver2/{siteId}/resources/{resourceId}   create or update a resource (UploadJsonlResources)
//   GET  /ver2/{siteId}/ping                     any verb/URL is also usable from ApiBulkRunner
//   GET  /mock/requests                          everything received so far
//   GET  /mock/stats                             counts per route and status
//   DELETE /mock/requests                        clear the recorded requests
//
// Configuration (appsettings.json, environment variables, or command line):
//   MockApi:FailEveryNth    return 500 for every Nth request (0 = never)            default 0
//   MockApi:ThrottleEveryNth return 429 for every Nth request (0 = never)           default 0
//   MockApi:LatencyMs       artificial delay per request                            default 0
//   MockApi:RequestLogPath  if set, append each request as one JSON line to this file

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.WriteIndented = true);
var app = builder.Build();

var cfg = app.Configuration.GetSection("MockApi");
var failEvery = cfg.GetValue("FailEveryNth", 0);
var throttleEvery = cfg.GetValue("ThrottleEveryNth", 0);
var latencyMs = cfg.GetValue("LatencyMs", 0);
var logPath = cfg["RequestLogPath"];

var received = new ConcurrentQueue<RecordedRequest>();
long counter = 0;

async Task Handle(HttpContext ctx, string route, bool requireJsonBody)
{
    var n = Interlocked.Increment(ref counter);
    if (latencyMs > 0)
    {
        await Task.Delay(latencyMs);
    }

    string body;
    using (var reader = new StreamReader(ctx.Request.Body))
    {
        body = await reader.ReadToEndAsync();
    }

    JsonNode? parsed = null;
    var jsonError = default(string);
    if (!string.IsNullOrWhiteSpace(body))
    {
        try { parsed = JsonNode.Parse(body); }
        catch (JsonException e) { jsonError = e.Message; }
    }
    else if (requireJsonBody)
    {
        jsonError = "request body is empty";
    }

    int status;
    object result;
    if (jsonError is not null)
    {
        status = 400;
        result = new { status = "error", error = "invalid_json", detail = jsonError };
    }
    else if (throttleEvery > 0 && n % throttleEvery == 0)
    {
        status = 429;
        ctx.Response.Headers.RetryAfter = "1";
        result = new { status = "error", error = "rate_limited" };
    }
    else if (failEvery > 0 && n % failEvery == 0)
    {
        status = 500;
        result = new { status = "error", error = "injected_failure" };
    }
    else
    {
        status = ctx.Request.Method == "PUT" ? 200 : 202;
        result = new { status = "accepted", requestId = n, route };
    }

    var rec = new RecordedRequest(n, DateTimeOffset.UtcNow, ctx.Request.Method, ctx.Request.Path + ctx.Request.QueryString,
        route, ctx.Request.Headers.Authorization.Count > 0, status, parsed);
    received.Enqueue(rec);
    if (!string.IsNullOrEmpty(logPath))
    {
        lock (received)
        {
            File.AppendAllText(logPath, JsonSerializer.Serialize(rec) + Environment.NewLine);
        }
    }
    await Results.Json(result, statusCode: status).ExecuteAsync(ctx);
}

app.MapPost("/ver2/{siteId}/activities", (HttpContext c) => Handle(c, "activities", true));
app.MapPost("/ver2/{siteId}/subscribers/identify", (HttpContext c) => Handle(c, "identify", true));
app.MapPut("/ver2/{siteId}/resources/{resourceId}", (HttpContext c) => Handle(c, "resources", true));
app.MapMethods("/ver2/{siteId}/ping", ["GET", "POST", "PUT", "DELETE", "PATCH"], (HttpContext c) => Handle(c, "ping", false));

app.MapGet("/mock/requests", () => received.ToArray());
app.MapDelete("/mock/requests", () => { received.Clear(); Interlocked.Exchange(ref counter, 0); return Results.NoContent(); });
app.MapGet("/mock/stats", () => received
    .GroupBy(r => new { r.Route, r.Status })
    .Select(g => new { g.Key.Route, g.Key.Status, Count = g.Count() })
    .OrderBy(x => x.Route).ThenBy(x => x.Status));
app.MapGet("/mock/health", () => Results.Ok(new { status = "ok" }));

app.Run();

record RecordedRequest(long Id, DateTimeOffset At, string Method, string Path, string Route, bool HadAuthorizationHeader, int Status, JsonNode? Body);
