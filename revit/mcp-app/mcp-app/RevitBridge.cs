using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace mcp_app
{
    internal class RevitBridge : IDisposable
    {
        // ===== DTOs =====
        private class Point2D { public double x; public double y; }
        private class CreateWallRequest
        {
            public string level { get; set; }
            public string wallType { get; set; }
            public Point2D start { get; set; }
            public Point2D end { get; set; }
            public double height_m { get; set; }
            public bool structural { get; set; } = false;
        }
        private class CreateWallResponse
        {
            public bool ok { get; set; }
            public int elementId { get; set; }
            public string message { get; set; }
        }
        private class MCPEnvelope
        {
            public string action { get; set; }
            public JObject args { get; set; }
        }

        // ===== Handler que ejecuta en el UI thread de Revit =====
        private class WallJobHandler : IExternalEventHandler
        {
            private CreateWallRequest _pending;
            private TaskCompletionSource<CreateWallResponse> _tcs;
            private readonly object _lock = new object();

            public void SetJob(CreateWallRequest req, TaskCompletionSource<CreateWallResponse> tcs)
            {
                lock (_lock) { _pending = req; _tcs = tcs; }
            }

            public void Execute(UIApplication app)
            {
                CreateWallRequest req;
                TaskCompletionSource<CreateWallResponse> tcs;
                lock (_lock) { req = _pending; tcs = _tcs; _pending = null; _tcs = null; }

                try
                {
                    var uidoc = app.ActiveUIDocument;
                    if (uidoc == null) throw new Exception("No active document.");
                    var doc = uidoc.Document;

                    // 1) Nivel
                    var level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .FirstOrDefault(l => l.Name.Equals(req.level, StringComparison.OrdinalIgnoreCase));
                    if (level == null) throw new Exception($"Level '{req.level}' not found.");

                    // 2) Tipo de muro
                    var wtype = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType)).Cast<WallType>()
                        .FirstOrDefault(t =>
                            t.Name.Equals(req.wallType, StringComparison.OrdinalIgnoreCase) ||
                            $"{t.FamilyName}: {t.Name}".Equals(req.wallType, StringComparison.OrdinalIgnoreCase));
                    if (wtype == null) throw new Exception($"WallType '{req.wallType}' not found.");

                    // 3) Geometría y unidades (m → ft internos)
                    double ToFt(double m) => UnitUtils.ConvertToInternalUnits(m, UnitTypeId.Meters);
                    var p1 = new XYZ(ToFt(req.start.x), ToFt(req.start.y), 0);
                    var p2 = new XYZ(ToFt(req.end.x), ToFt(req.end.y), 0);
                    var line = Line.CreateBound(p1, p2);

                    int id;
                    using (var t = new Transaction(doc, "MCP: Create Wall"))
                    {
                        t.Start();
                        var wall = Wall.Create(doc, line, level.Id, req.structural);
                        wall.WallType = wtype;

                        var hParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                        if (hParam == null || hParam.IsReadOnly)
                            throw new Exception("Cannot set wall height (WALL_USER_HEIGHT_PARAM).");

                        hParam.Set(ToFt(req.height_m));
                        id = wall.Id.IntegerValue;
                        t.Commit();
                    }

                    tcs?.TrySetResult(new CreateWallResponse { ok = true, elementId = id, message = "Wall created." });
                }
                catch (Exception ex)
                {
                    tcs?.TrySetResult(new CreateWallResponse { ok = false, elementId = -1, message = ex.Message });
                }
            }

            public string GetName() => "MCP RevitBridge WallJobHandler";
        }

        // ===== HTTP listener + ExternalEvent =====
        private readonly HttpListener _http = new HttpListener();
        private readonly WallJobHandler _handler = new WallJobHandler();
        private readonly ExternalEvent _evt;
        private bool _running;

        public RevitBridge()
        {
            _evt = ExternalEvent.Create(_handler);
        }

        public void Start(string prefix)
        {
            if (_running) return;
            _http.Prefixes.Clear();
            _http.Prefixes.Add(prefix);
            _http.Start();
            _running = true;
            _ = ListenLoop();
        }

        private async Task ListenLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx = null;
                try { ctx = await _http.GetContextAsync(); }
                catch { if (!_running) break; }
                if (ctx != null) _ = Handle(ctx);
            }
        }

        private async Task Handle(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod != "POST")
                {
                    await WriteJson(ctx, 405, new { ok = false, message = "Method not allowed" });
                    return;
                }

                string body;
                using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    body = await sr.ReadToEndAsync();

                var path = ctx.Request.Url.AbsolutePath;

                if (path == "/mcp")
                {
                    var env = JsonConvert.DeserializeObject<MCPEnvelope>(body);
                    if (env == null || string.IsNullOrWhiteSpace(env.action))
                        throw new Exception("Invalid MCP envelope. Expecting { action, args }.");

                    switch (env.action.ToLowerInvariant())
                    {
                        case "wall.create":
                        case "create_wall":
                            await RunWallCreate(ctx, env.args?.ToObject<CreateWallRequest>());
                            return;
                        default:
                            throw new Exception($"Unknown action '{env.action}'.");
                    }
                }
                else if (path == "/wall.create") // compat opcional
                {
                    var req = JsonConvert.DeserializeObject<CreateWallRequest>(body);
                    await RunWallCreate(ctx, req);
                    return;
                }
                else
                {
                    await WriteJson(ctx, 404, new { ok = false, message = "Not found" });
                }
            }
            catch (Exception ex)
            {
                try { await WriteJson(ctx, 500, new { ok = false, message = ex.Message }); } catch { }
            }
        }

        private async Task RunWallCreate(HttpListenerContext ctx, CreateWallRequest req)
        {
            if (req == null)
            {
                await WriteJson(ctx, 400, new { ok = false, message = "Missing args for wall.create." });
                return;
            }

            var tcs = new TaskCompletionSource<CreateWallResponse>();
            _handler.SetJob(req, tcs);
            _evt.Raise();                      // Ejecuta en el hilo de UI de Revit
            var result = await tcs.Task;       // Espera el resultado
            await WriteJson(ctx, result.ok ? 200 : 500, result);
        }

        private static async Task WriteJson(HttpListenerContext ctx, int code, object obj)
        {
            var json = JsonConvert.SerializeObject(obj);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        public void Dispose()
        {
            _running = false;
            try { _http?.Stop(); } catch { }
        }
    }
}
