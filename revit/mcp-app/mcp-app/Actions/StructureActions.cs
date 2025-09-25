using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

using mcp_app.Core;
using Newtonsoft.Json.Linq;

namespace mcp_app.Actions
{
    internal class StructureActions
    {
        private class Pt2 { public double x; public double y; }

        private class BeamCreateRequest
        {
            public string level { get; set; }         // opcional
            public string familyType { get; set; }    // e.g. "W-Wide Flange : W12x26"
            public double elevation_m { get; set; } = 3.0;
            public Pt2 start { get; set; }
            public Pt2 end { get; set; }
        }

        public static Func<UIApplication, object> BeamCreate(JObject args)
        {
            var req = args.ToObject<BeamCreateRequest>() ?? throw new Exception("Invalid args for struct.beam.create.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;
                var active = uidoc.ActiveView;

                var level = ViewHelpers.ResolveLevel(doc, req.level, active);
                double z = level.Elevation + Core.Units.MetersToFt(req.elevation_m);

                // FamilySymbol de Structural Framing
                var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Where(fs => fs.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming).ToList();
                var sym = string.IsNullOrWhiteSpace(req.familyType)
                    ? symbols.FirstOrDefault()
                    : symbols.FirstOrDefault(fs =>
                        fs.Name.Equals(req.familyType, StringComparison.OrdinalIgnoreCase) ||
                        $"{fs.FamilyName}: {fs.Name}".Equals(req.familyType, StringComparison.OrdinalIgnoreCase));
                if (sym == null) throw new Exception("No Structural Framing FamilySymbol found.");

                double ToFt(double m) => Core.Units.MetersToFt(m);
                var p1 = new XYZ(ToFt(req.start.x), ToFt(req.start.y), z);
                var p2 = new XYZ(ToFt(req.end.x), ToFt(req.end.y), z);
                var line = Line.CreateBound(p1, p2);

                int id;
                using (var t = new Transaction(doc, "MCP: Struct.Beam.Create"))
                {
                    t.Start();
                    if (!sym.IsActive) sym.Activate();
                    var fi = doc.Create.NewFamilyInstance(line, sym, level, StructuralType.Beam);
                    id = fi.Id.IntegerValue;
                    t.Commit();
                }
                return new { elementId = id, used = new { level = level.Name, familyType = $"{sym.FamilyName}: {sym.Name}" } };
            };
        }

        private class ColumnCreateRequest
        {
            public string level { get; set; }
            public string familyType { get; set; } // e.g. "Concrete-Rectangular Column : 300 x 600"
            public double elevation_m { get; set; } = 0.0; // base offset
            public Pt2 point { get; set; }                 // XY en m
        }

        public static Func<UIApplication, object> ColumnCreate(JObject args)
        {
            var req = args.ToObject<ColumnCreateRequest>() ?? throw new Exception("Invalid args for struct.column.create.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;
                var active = uidoc.ActiveView;

                var level = ViewHelpers.ResolveLevel(doc, req.level, active);
                double z = level.Elevation + Core.Units.MetersToFt(req.elevation_m);

                var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Where(fs => fs.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns).ToList();
                var sym = string.IsNullOrWhiteSpace(req.familyType)
                    ? symbols.FirstOrDefault()
                    : symbols.FirstOrDefault(fs =>
                        fs.Name.Equals(req.familyType, StringComparison.OrdinalIgnoreCase) ||
                        $"{fs.FamilyName}: {fs.Name}".Equals(req.familyType, StringComparison.OrdinalIgnoreCase));
                if (sym == null) throw new Exception("No Structural Column FamilySymbol found.");

                double ToFt(double m) => Core.Units.MetersToFt(m);
                var p = new XYZ(ToFt(req.point.x), ToFt(req.point.y), z);

                int id;
                using (var t = new Transaction(doc, "MCP: Struct.Column.Create"))
                {
                    t.Start();
                    if (!sym.IsActive) sym.Activate();
                    var fi = doc.Create.NewFamilyInstance(p, sym, level, StructuralType.Column);
                    id = fi.Id.IntegerValue;
                    t.Commit();
                }
                return new { elementId = id, used = new { level = level.Name, familyType = $"{sym.FamilyName}: {sym.Name}" } };
            };
        }

        private class SFloorCreateRequest
        {
            public string level { get; set; }
            public string floorType { get; set; } // opcional
            public Pt2[] profile { get; set; }    // polígono cerrado en XY (m)
        }

        public static Func<UIApplication, object> StructuralFloorCreate(JObject args)
        {
            var req = args.ToObject<SFloorCreateRequest>() ?? throw new Exception("Invalid args for struct.floor.create.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;
                var level = ViewHelpers.ResolveLevel(doc, req.level, uidoc.ActiveView);

                var ftypes = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().ToList();
                var ftype = string.IsNullOrWhiteSpace(req.floorType)
                    ? ftypes.FirstOrDefault()
                    : ftypes.FirstOrDefault(t =>
                        t.Name.Equals(req.floorType, StringComparison.OrdinalIgnoreCase) ||
                        $"{t.FamilyName}: {t.Name}".Equals(req.floorType, StringComparison.OrdinalIgnoreCase));
                if (ftype == null) throw new Exception("FloorType not found.");

                if (req.profile == null || req.profile.Length < 3)
                    throw new Exception("Floor profile requires at least 3 points.");

                double ToFt(double m) => Core.Units.MetersToFt(m);
                var loop = new CurveLoop();
                for (int i = 0; i < req.profile.Length; i++)
                {
                    var a = req.profile[i];
                    var b = req.profile[(i + 1) % req.profile.Length];
                    loop.Append(Line.CreateBound(
                        new XYZ(ToFt(a.x), ToFt(a.y), level.Elevation),
                        new XYZ(ToFt(b.x), ToFt(b.y), level.Elevation)
                    ));
                }

                int id;
                using (var t = new Transaction(doc, "MCP: Struct.Floor.Create"))
                {
                    t.Start();
                    var fl = Floor.Create(doc, new System.Collections.Generic.List<CurveLoop> { loop }, ftype.Id, level.Id);
                    id = fl.Id.IntegerValue;

                    // Marcar como estructural si el parámetro existe
                    var p = fl.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                    if (p != null && !p.IsReadOnly) p.Set(1);

                    t.Commit();
                }
                return new { elementId = id, used = new { level = level.Name, floorType = $"{ftype.FamilyName}: {ftype.Name}" } };
            };
        }
    }
}
