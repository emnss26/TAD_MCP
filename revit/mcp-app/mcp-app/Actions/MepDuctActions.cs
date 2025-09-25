using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using mcp_app.Core;

namespace mcp_app.Actions
{
    internal class MepDuctActions
    {
        private class Pt2 { public double x; public double y; }
        private class DuctCreateRequest
        {
            public string level { get; set; }               // opcional
            public string systemType { get; set; }          // nombre o clasificación (SupplyAir, ReturnAir, etc) opcional
            public string ductType { get; set; }            // opcional
            public double elevation_m { get; set; } = 2.7;  // altura sobre nivel
            public Pt2 start { get; set; }
            public Pt2 end { get; set; }
            public double? width_mm { get; set; }           // opcional (rectangular)
            public double? height_mm { get; set; }          // opcional (rectangular)
            public double? diameter_mm { get; set; }        // opcional (redondo)
        }

        public static Func<UIApplication, object> DuctCreate(JObject args)
        {
            var req = args.ToObject<DuctCreateRequest>() ?? throw new Exception("Invalid args for mep.duct.create.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;
                var active = uidoc.ActiveView;

                // Nivel y Z
                var level = ViewHelpers.ResolveLevel(doc, req.level, active);
                double z = level.Elevation + Core.Units.MetersToFt(req.elevation_m);

                // SystemType (MEPSystemType)
                var systems = new FilteredElementCollector(doc)
                    .OfClass(typeof(MEPSystemType)).Cast<MEPSystemType>().ToList();

                MEPSystemType sys = null;
                if (!string.IsNullOrWhiteSpace(req.systemType))
                {
                    sys = systems.FirstOrDefault(s =>
                        s.Name.Equals(req.systemType, StringComparison.OrdinalIgnoreCase) ||
                        s.SystemClassification.ToString().Equals(req.systemType, StringComparison.OrdinalIgnoreCase));
                }
                if (sys == null) sys = systems.FirstOrDefault();
                if (sys == null) throw new Exception("No MEPSystemType available for ducts.");

                if (req.start == null || req.end == null)
                    throw new Exception("Duct requires start and end points.");

                // DuctType
                var dtypes = new FilteredElementCollector(doc).OfClass(typeof(DuctType)).Cast<DuctType>().ToList();
                DuctType dtype = null;
                if (!string.IsNullOrWhiteSpace(req.ductType))
                {
                    dtype = dtypes.FirstOrDefault(t =>
                        t.Name.Equals(req.ductType, StringComparison.OrdinalIgnoreCase) ||
                        $"{t.FamilyName}: {t.Name}".Equals(req.ductType, StringComparison.OrdinalIgnoreCase));
                }
                if (dtype == null) dtype = dtypes.FirstOrDefault();

                if (dtype == null)
                {
                    var sample = string.Join(", ", dtypes.Take(10).Select(t => $"{t.FamilyName}: {t.Name}"));
                    throw new Exception("No suitable DuctType found." + (sample.Length > 0 ? $" Available examples: {sample}" : ""));
                }

                // Geometría
                double ToFt(double m) => Core.Units.MetersToFt(m);
                var p1 = new XYZ(ToFt(req.start.x), ToFt(req.start.y), z);
                var p2 = new XYZ(ToFt(req.end.x), ToFt(req.end.y), z);
                if (p1.IsAlmostEqualTo(p2)) throw new Exception("Start and end points are the same.");

                int id;
                using (var t = new Transaction(doc, "MCP: MEP.Duct.Create"))
                {
                    t.Start();
                    var duct = Duct.Create(doc, sys.Id, dtype.Id, level.Id, p1, p2);
                    id = duct.Id.IntegerValue;

                    // Tamaños opcionales
                    if (req.diameter_mm.HasValue)
                    {
                        var diam = Core.Units.MetersToFt(req.diameter_mm.Value / 1000.0);
                        duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.Set(diam);
                    }
                    else
                    {
                        if (req.width_mm.HasValue)
                        {
                            var w = Core.Units.MetersToFt(req.width_mm.Value / 1000.0);
                            duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.Set(w);
                        }
                        if (req.height_mm.HasValue)
                        {
                            var h = Core.Units.MetersToFt(req.height_mm.Value / 1000.0);
                            duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.Set(h);
                        }
                    }

                    t.Commit();
                }

                return new
                {
                    elementId = id,
                    used = new
                    {
                        level = level.Name,
                        systemType = $"{sys.SystemClassification} / {sys.Name}",
                        ductType = $"{dtype.FamilyName}: {dtype.Name}",
                        elevation_m = req.elevation_m
                    }
                };
            };
        }
    }
}
