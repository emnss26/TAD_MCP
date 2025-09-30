using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using mcp_app.Actions;
using mcp_app.Contracts;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
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
        
        private class UiJob
        {
            public Func<UIApplication, object> Execute { get; set; }
            public TaskCompletionSource<object> Tcs { get; set; }
        }

        private class ActionRunner : IExternalEventHandler
        {
            private readonly ConcurrentQueue<UiJob> _queue = new ConcurrentQueue<UiJob>();
            public void Enqueue(UiJob job) => _queue.Enqueue(job);

            public void Execute(UIApplication app)
            {
                while (_queue.TryDequeue(out var job))
                {
                    try
                    {
                        var result = job.Execute(app);
                        job.Tcs.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        job.Tcs.TrySetException(ex);
                    }
                }
            }
            public string GetName() => "MCP ActionRunner";
        }

        // ====== HTTP ======
        private readonly HttpListener _http = new HttpListener();
        private bool _running;

        private readonly ActionRunner _runner = new ActionRunner();
        private readonly ExternalEvent _evt;

        private readonly Dictionary<string, Func<JObject, Func<UIApplication, object>>> _registry;

        public RevitBridge()
        {
            _evt = ExternalEvent.Create(_runner);

            _registry = new Dictionary<string, Func<JObject, Func<UIApplication, object>>>(StringComparer.OrdinalIgnoreCase)
            {
                // --- CORE / ARCH ---
                ["wall.create"] = ArchitectureActions.WallCreate,
                ["level.create"] = ArchitectureActions.LevelCreate,
                ["grid.create"] = ArchitectureActions.GridCreate,
                ["floor.create"] = ArchitectureActions.FloorCreate,
                ["ceiling.create"] = ArchitectureActions.CeilingCreate, 
                ["door.place"] = ArchitectureActions.DoorPlace,
                ["window.place"] = ArchitectureActions.WindowPlace,
                ["rooms.create_on_levels"] = ArchitectureActions.RoomsCreateOnLevels,
                ["floors.from_rooms"] = ArchitectureActions.FloorsFromRooms,
                ["ceilings.from_rooms"] = ArchitectureActions.CeilingsFromRooms,
                ["roof.create_footprint"] = ArchitectureActions.RoofFootprintCreate,

                // --- Estructura ---
                ["struct.beam.create"] = StructureActions.BeamCreate,
                ["struct.column.create"] = StructureActions.ColumnCreate,
                ["struct.floor.create"] = StructureActions.StructuralFloorCreate,
                
                // --- MEP ---
                ["mep.duct.create"] = MepDuctActions.DuctCreate,
                ["mep.pipe.create"] = MepPipeConduitActions.PipeCreate,
                ["mep.conduit.create"] = MepPipeConduitActions.ConduitCreate,
                ["mep.cabletray.create"] = MepPipeConduitActions.CableTrayCreate,

                // --- GRAPHICS ---
                ["view.category.set_visibility"] = GraphicsActions.SetVisibility,
                ["view.category.override_color"] = GraphicsActions.OverrideColor,
                ["view.category.clear_overrides"] = GraphicsActions.ClearOverrides,
                ["view.apply_template"] = GraphicsActions.ApplyTemplate,
                ["view.set_scale"] = GraphicsActions.SetScale,
                ["view.set_detail_level"] = GraphicsActions.SetDetailLevel,
                ["view.set_discipline"] = GraphicsActions.SetDiscipline,
                ["view.set_phase"] = GraphicsActions.SetPhase,
                ["views.duplicate"] = GraphicsActions.ViewsDuplicate,
                ["imports.hide"] = GraphicsActions.HideImports,

                // --- PARAMS ---
                ["params.get"] = ParamsActions.ParamsGet,
                ["params.set"] = ParamsActions.ParamsSet,
                ["params.bulk_from_table"] = ParamsActions.ParamsBulkFromTable,
                ["params.set_where"] = ParamsActions.ParamsSetWhere,

                // --- DOCUMENTS ---
                ["sheets.create"] = DocActions.SheetsCreate,
                ["sheets.add_views"] = DocActions.SheetsAddViews,
                ["sheets.create_bulk"] = DocActions.SheetsCreateBulk,      
                ["sheets.assign_revisions"] = DocActions.SheetsAssignRevisions,
                ["views.set_scope_box"] = DocActions.ViewsSetScopeBox,

                // --- QUERY ---
                ["levels.list"] = QueryActions.LevelsList,
                ["walltypes.list"] = QueryActions.WallTypesList,
                ["views.list"] = QueryActions.ViewsList,
                ["schedules.list"] = QueryActions.SchedulesList,
                ["materials.list"] = QueryActions.MaterialsList,
                ["categories.list"] = QueryActions.CategoriesList,
                ["families.types.list"] = QueryActions.FamiliesTypesList,
                ["links.list"] = QueryActions.LinksList,
                ["imports.list"] = QueryActions.ImportsList,
                ["worksets.list"] = QueryActions.WorksetsList,
                ["textnotes.find"] = QueryActions.TextNotesFind,
                ["view.active"] = QueryActions.ActiveViewInfo,
                ["selection.info"] = QueryActions.SelectionInfo,
                ["element.info"] = QueryActions.ElementInfo,
                ["ducttypes.list"] = QueryActions.DuctTypesList,
                ["pipetypes.list"] = QueryActions.PipeTypesList,
                ["cabletraytypes.list"] = QueryActions.CableTrayTypesList,

                // --- EXPORT ---
                ["export.nwc"] = ExportActions.ExportNwc,
                ["export.dwg"] = ExportActions.ExportDwg,
                ["export.pdf"] = ExportActions.ExportPdf,

                // --- QA ---
                ["qa.fix.pin_all_links"] = QaActions.FixPinAllLinks,
                ["qa.fix.delete_imports"] = QaActions.FixDeleteImports,
                ["qa.fix.apply_view_templates"] = QaActions.FixApplyViewTemplates,
                ["qa.fix.remove_textnotes"] = QaActions.FixRemoveTextNotes,
                ["qa.fix.delete_unused_types"] = QaActions.FixDeleteUnusedTypes,
                ["qa.fix.rename_views"] = QaActions.FixRenameViews,

                // --- QTO ---
                ["qto.walls"] = QtoActions.QtoWalls,
                ["qto.floors"] = QtoActions.QtoFloors,
                ["qto.struct.concrete"] = QtoActions.QtoStructConcrete,
                ["qto.mep.pipes"] = QtoActions.QtoMepPipes,
                ["qto.mep.ducts"] = QtoActions.QtoMepDucts
            };
        }

        public void Start(string prefix)
        {
            if (_running) return;
            _http.Prefixes.Clear();
            _http.Prefixes.Add(prefix); // e.g. http://127.0.0.1:55234/
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
                    await WriteJson(ctx, 405, new MCPResponse { ok = false, message = "Method not allowed" });
                    return;
                }

                string body;
                using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    body = await sr.ReadToEndAsync();

                var path = ctx.Request.Url.AbsolutePath;
                if (path != "/mcp")
                {
                    await WriteJson(ctx, 404, new MCPResponse { ok = false, message = "Not found" });
                    return;
                }

                var env = JsonConvert.DeserializeObject<MCPEnvelope>(body);
                if (env == null || string.IsNullOrWhiteSpace(env.action))
                    throw new Exception("Invalid MCP envelope. Expecting { action, args }.");

                if (!_registry.TryGetValue(env.action, out var builder))
                    throw new Exception($"Unknown action '{env.action}'.");

                var exec = builder(env.args ?? new JObject());

                var tcs = new TaskCompletionSource<object>();
                _runner.Enqueue(new UiJob { Execute = exec, Tcs = tcs });
                _evt.Raise();

                object result = await tcs.Task;
                await WriteJson(ctx, 200, new MCPResponse { ok = true, data = result });
            }
            catch (Exception ex)
            {
                try { await WriteJson(ctx, 500, new MCPResponse { ok = false, message = ex.Message }); } catch { }
            }
        }

        private static async Task WriteJson(HttpListenerContext ctx, int code, MCPResponse obj)
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
