using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Electrical;

using Newtonsoft.Json.Linq;

using mcp_app.Core;

namespace mcp_app.Actions
{
    internal class MepPipeConduitActions
    {
        private class Pt2 { public double x; public double y; }

        private class PipeCreateRequest
        {
            public string level { get; set; }
            public string systemType { get; set; }      // nombre (PipingSystemType) opcional
            public string pipeType { get; set; }        // opcional
            public double elevation_m { get; set; } = 2.5;
            public Pt2 start { get; set; }
            public Pt2 end { get; set; }
            public double? diameter_mm { get; set; }    // opcional
        }

        public static Func<UIApplication, object> PipeCreate(JObject args)
        {
            var req = args.ToObject<PipeCreateRequest>() ?? throw new Exception("Invalid args for mep.pipe.create.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;
                var active = uidoc.ActiveView;

                var level = ViewHelpers.ResolveLevel(doc, req.level, active);
                double z = level.Elevation + Core.Units.MetersToFt(req.elevation_m);

                // System y Type
                var systems = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().ToList();
                var sys = string.IsNullOrWhiteSpace(req.systemType)
                    ? systems.FirstOrDefault()
                    : systems.FirstOrDefault(s =>
                        s.Name.Equals(req.systemType, StringComparison.OrdinalIgnoreCase));
                if (sys == null) throw new Exception("No PipingSystemType found.");

                var types = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().ToList();
                PipeType ptype = null;
                if (!string.IsNullOrWhiteSpace(req.pipeType))
                {
                    ptype = types.FirstOrDefault(t =>
                        t.Name.Equals(req.pipeType, StringComparison.OrdinalIgnoreCase) ||
                        $"{t.FamilyName}: {t.Name}".Equals(req.pipeType, StringComparison.OrdinalIgnoreCase));
                }
                if (ptype == null) ptype = types.FirstOrDefault();

                if (ptype == null)
                {
                    var sample = string.Join(", ", types.Take(10).Select(t => $"{t.FamilyName}: {t.Name}"));
                    throw new Exception("No PipeType found." + (sample.Length > 0 ? $" Available examples: {sample}" : ""));
                }

                double ToFt(double m) => Core.Units.MetersToFt(m);
                var p1 = new XYZ(ToFt(req.start.x), ToFt(req.start.y), z);
                var p2 = new XYZ(ToFt(req.end.x), ToFt(req.end.y), z);

                int id;
                using (var t = new Transaction(doc, "MCP: MEP.Pipe.Create"))
                {
                    t.Start();
                    var pipe = Pipe.Create(doc, sys.Id, ptype.Id, level.Id, p1, p2);
                    id = pipe.Id.IntegerValue;

                    if (req.diameter_mm.HasValue)
                    {
                        var diam = Core.Units.MetersToFt(req.diameter_mm.Value / 1000.0);
                        pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(diam);
                    }

                    t.Commit();
                }

                return new
                {
                    elementId = id,
                    used = new
                    {
                        level = level.Name,
                        systemType = sys.Name,
                        pipeType = $"{ptype.FamilyName}: {ptype.Name}",
                        elevation_m = req.elevation_m
                    }
                };
            };
        }

        private class ConduitCreateRequest
        {
            public string level { get; set; }
            public string conduitType { get; set; }     // opcional
            public double elevation_m { get; set; } = 2.4;
            public Pt2 start { get; set; }
            public Pt2 end { get; set; }
            public double? diameter_mm { get; set; }    // opcional
        }

        public static Func<UIApplication, object> ConduitCreate(JObject args)
        {
            var req = args.ToObject<ConduitCreateRequest>() ?? throw new Exception("Invalid args for mep.conduit.create.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;
                var active = uidoc.ActiveView;

                var level = ViewHelpers.ResolveLevel(doc, req.level, active);
                double z = level.Elevation + Core.Units.MetersToFt(req.elevation_m);

                var ctypes = new FilteredElementCollector(doc).OfClass(typeof(ConduitType)).Cast<ConduitType>().ToList();
                ConduitType ctype = null;
                if (!string.IsNullOrWhiteSpace(req.conduitType))
                {
                    ctype = ctypes.FirstOrDefault(t =>
                        t.Name.Equals(req.conduitType, StringComparison.OrdinalIgnoreCase) ||
                        $"{t.FamilyName}: {t.Name}".Equals(req.conduitType, StringComparison.OrdinalIgnoreCase));
                }
                if (ctype == null) ctype = ctypes.FirstOrDefault();

                if (ctype == null)
                {
                    var sample = string.Join(", ", ctypes.Take(10).Select(t => t.Name));
                    throw new Exception("No ConduitType found." + (sample.Length > 0 ? $" Available examples: {sample}" : ""));
                }

                double ToFt(double m) => Core.Units.MetersToFt(m);
                var p1 = new XYZ(ToFt(req.start.x), ToFt(req.start.y), z);
                var p2 = new XYZ(ToFt(req.end.x), ToFt(req.end.y), z);

                int id;
                using (var t = new Transaction(doc, "MCP: MEP.Conduit.Create"))
                {
                    t.Start();
                    var conduit = Conduit.Create(doc, ctype.Id, p1, p2, level.Id);
                    id = conduit.Id.IntegerValue;

                    if (req.diameter_mm.HasValue)
                    {
                        var diam = Core.Units.MetersToFt(req.diameter_mm.Value / 1000.0);
                        conduit.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM)?.Set(diam);
                    }

                    t.Commit();
                }

                return new
                {
                    elementId = id,
                    used = new
                    {
                        level = level.Name,
                        conduitType = ctype.Name,
                        elevation_m = req.elevation_m
                    }
                };
            };
        }

        private class CableTrayCreateRequest
        {
            public string level { get; set; }
            public string cableTrayType { get; set; }   // opcional
            public double elevation_m { get; set; } = 2.7;
            public Pt2 start { get; set; }
            public Pt2 end { get; set; }
        }

        public static Func<UIApplication, object> CableTrayCreate(JObject args)
        {
            var req = args.ToObject<CableTrayCreateRequest>() ?? throw new Exception("Invalid args for mep.cabletray.create.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;

                // Tipo de charola
                var ctypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(CableTrayType)).Cast<CableTrayType>().ToList();

                CableTrayType ctype = null;
                if (string.IsNullOrWhiteSpace(req.cableTrayType))
                    ctype = ctypes.FirstOrDefault();
                else
                    ctype = ctypes.FirstOrDefault(t =>
                        t.Name.Equals(req.cableTrayType, StringComparison.OrdinalIgnoreCase));

                if (ctype == null) throw new Exception("No CableTrayType found.");

                // Nivel (usa nivel de vista activa o el primero)
                var level = ViewHelpers.ResolveLevel(doc, req.level, uidoc.ActiveView);

                // Geometría (Z relativo al nivel)
                double ToFt(double m) => mcp_app.Core.Units.MetersToFt(m);
                var z = level.Elevation + ToFt(req.elevation_m);
                var p1 = new XYZ(ToFt(req.start.x), ToFt(req.start.y), z);
                var p2 = new XYZ(ToFt(req.end.x), ToFt(req.end.y), z);
                if (p1.IsAlmostEqualTo(p2)) throw new Exception("Start and end points are the same.");

                int id;
                using (var t = new Transaction(doc, "MCP: MEP.CableTray.Create"))
                {
                    t.Start();
                    var tray = CableTray.Create(doc, ctype.Id, p1, p2, level.Id);
                    id = tray.Id.IntegerValue;
                    t.Commit();
                }

                return new
                {
                    elementId = id,
                    used = new { cableTrayType = ctype.Name, level = level.Name, elevation_m = req.elevation_m }
                };
            };
        }
    }
}
