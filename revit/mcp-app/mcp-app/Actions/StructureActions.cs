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

        private class ColumnsPlaceOnGridRequest
        {
            public string baseLevel { get; set; }                   // requerido
            public string topLevel { get; set; }                    // opcional (usa baseLevel si falta)
            public string familyType { get; set; }                  // "Family: Type" o solo Type (requerido)

            public string[] gridX { get; set; }                     // ej. ["A-E"] o ["A","B","C"]
            public string[] gridY { get; set; }                     // ej. ["1-8"] o ["1","2","3"]
            public string[] gridNames { get; set; }                 // alternativa: lista plana a auto-clasificar

            public double? baseOffset_m { get; set; }               // offset base en m (default 0)
            public double? topOffset_m { get; set; }                // offset tope en m (default 0)

            public bool? onlyIntersectionsInsideActiveCrop { get; set; } // filtra por crop de vista activa
            public double? tolerance_m { get; set; }                // para evitar duplicados/validaciones (default 0.05 m)
            public bool? skipIfColumnExistsNearby { get; set; }     // true = no crear si hay columna cerca

            public string worksetName { get; set; }                 // opcional
            public bool? pinned { get; set; }                       // opcional

            public string orientationRelativeTo { get; set; }       // "X"|"Y"|"None" (rota respecto a Z)
        }

        public static Func<UIApplication, object> ColumnsPlaceOnGrid(JObject args)
        {
            var req = args.ToObject<ColumnsPlaceOnGridRequest>() ?? throw new Exception("Invalid args for struct.columns.place_on_grid.");

            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;
                var active = uidoc.ActiveView;

                double ToFt(double m) => Core.Units.MetersToFt(m);

                // --- 1) FamilySymbol de columna ---
                var symbols = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Where(fs => fs.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns)
                    .ToList();

                var sym = symbols.FirstOrDefault(fs =>
                               (!string.IsNullOrWhiteSpace(req.familyType)) &&
                               (fs.Name.Equals(req.familyType, StringComparison.OrdinalIgnoreCase) ||
                                (fs.FamilyName + ": " + fs.Name).Equals(req.familyType, StringComparison.OrdinalIgnoreCase)))
                          ?? symbols.FirstOrDefault();

                if (sym == null) throw new Exception("No Structural Column FamilySymbol found.");

                if (!sym.IsActive)
                {
                    using (var t0 = new Transaction(doc, "Activate Column Type"))
                    { t0.Start(); sym.Activate(); t0.Commit(); }
                }

                // --- 2) Niveles (base/top) ---
                var levelBase = ViewHelpers.ResolveLevel(doc, req.baseLevel, active);
                var levelTop = string.IsNullOrWhiteSpace(req.topLevel) ? levelBase : ViewHelpers.ResolveLevel(doc, req.topLevel, active);

                // --- 3) Grids disponibles ---
                var allGrids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().ToList();

                IEnumerable<Grid> MatchToken(string token)
                {
                    token = (token ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(token)) return Enumerable.Empty<Grid>();

                    // Rangos "A-E" o "1-8"
                    if (token.IndexOf('-') >= 0)
                    {
                        var parts = token.Split(new[] { '-' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2)
                        {
                            var a = parts[0].Trim();
                            var b = parts[1].Trim();

                            int na, nb;
                            if (int.TryParse(a, out na) && int.TryParse(b, out nb))
                            {
                                var from = Math.Min(na, nb); var to = Math.Max(na, nb);
                                var set = new HashSet<string>(Enumerable.Range(from, to - from + 1).Select(i => i.ToString()), StringComparer.OrdinalIgnoreCase);
                                return allGrids.Where(g => set.Contains(g.Name));
                            }

                            // alfabético simple (una letra)
                            if (a.Length == 1 && b.Length == 1 && char.IsLetter(a[0]) && char.IsLetter(b[0]))
                            {
                                var ca = char.ToUpperInvariant(a[0]); var cb = char.ToUpperInvariant(b[0]);
                                if (ca > cb) { var tmp = ca; ca = cb; cb = tmp; }
                                var list = new List<string>();
                                for (var c = ca; c <= cb; c++) list.Add(((char)c).ToString());
                                var set = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
                                return allGrids.Where(g => set.Contains(g.Name));
                            }
                        }
                    }

                    // Igualdad exacta
                    return allGrids.Where(g => g.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
                }

                var gridsX = new List<Grid>();
                var gridsY = new List<Grid>();

                if (req.gridX != null) foreach (var t in req.gridX) gridsX.AddRange(MatchToken(t));
                if (req.gridY != null) foreach (var t in req.gridY) gridsY.AddRange(MatchToken(t));

                // Si no hay X/Y pero sí gridNames, clasificar por dirección
                if ((gridsX.Count == 0 || gridsY.Count == 0) && req.gridNames != null && req.gridNames.Length > 0)
                {
                    var wanted = new HashSet<String>(req.gridNames, StringComparer.OrdinalIgnoreCase);
                    var sel = allGrids.Where(g => wanted.Contains(g.Name)).ToList();

                    foreach (var g in sel)
                    {
                        XYZ dir = null;
                        var line = g.Curve as Line;
                        var arc = g.Curve as Arc;

                        if (line != null) dir = line.Direction;
                        else if (arc != null) dir = arc.XDirection;

                        if (dir == null) dir = XYZ.BasisX; // fallback para C# 7.3

                        // heurística: X-like si |X| >= |Y|
                        if (Math.Abs(dir.X) >= Math.Abs(dir.Y)) gridsX.Add(g);
                        else gridsY.Add(g);
                    }
                }

                if (gridsX.Count == 0 || gridsY.Count == 0)
                    throw new Exception("No grids resolved for X/Y. Provide gridX/gridY or gridNames.");

                // --- 4) Crop de vista activa (opcional) ---
                BoundingBoxXYZ crop = null;
                if (req.onlyIntersectionsInsideActiveCrop == true && active != null && active.CropBoxActive)
                    crop = active.CropBox;

                bool InsideCrop(XYZ p, double tol)
                {
                    if (crop == null) return true;
                    return !(p.X < crop.Min.X - tol || p.X > crop.Max.X + tol ||
                             p.Y < crop.Min.Y - tol || p.Y > crop.Max.Y + tol);
                }

                // --- 5) Tolerancia y duplicados ---
                var tolFtVal = ToFt(req.tolerance_m ?? 0.05); // 5 cm por defecto
                bool skipNearby = req.skipIfColumnExistsNearby == true;

                var existingCols = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .WhereElementIsNotElementType()
                    .Cast<Element>()
                    .ToList();

                bool HasColNearby(XYZ p)
                {
                    if (!skipNearby) return false;
                    foreach (var e in existingCols)
                    {
                        var lp = (e.Location as LocationPoint)?.Point;
                        if (lp != null && lp.DistanceTo(p) <= tolFtVal) return true;
                    }
                    return false;
                }

                // --- 6) Workset destino (opcional) ---
                Workset targetWs = null;
                if (!string.IsNullOrWhiteSpace(req.worksetName) && doc.IsWorkshared)
                {
                    // C# 7.3 compatible: usar FilteredWorksetCollector
                    var ws = new FilteredWorksetCollector(doc)
                        .OfKind(WorksetKind.UserWorkset)
                        .FirstOrDefault(w => w.Name.Equals(req.worksetName, StringComparison.OrdinalIgnoreCase));

                    if (ws != null) targetWs = ws;
                }

                // --- 7) Offsets y orientación ---
                var baseOffFt = ToFt(req.baseOffset_m ?? 0.0);
                var topOffFt = ToFt(req.topOffset_m ?? 0.0);

                double? orientAngle = null; // 0 = paralelo a Y; pi/2 = paralelo a X
                if (!string.IsNullOrWhiteSpace(req.orientationRelativeTo))
                {
                    if (req.orientationRelativeTo.Equals("Y", StringComparison.OrdinalIgnoreCase)) orientAngle = 0.0;
                    else if (req.orientationRelativeTo.Equals("X", StringComparison.OrdinalIgnoreCase)) orientAngle = Math.PI / 2.0;
                }

                // --- 8) Intersecciones + creación ---
                var created = new List<int>();
                var skipped = new List<object>();

                using (var tg = new TransactionGroup(doc, "MCP: Struct.Columns.PlaceOnGrid"))
                {
                    tg.Start();

                    using (var t = new Transaction(doc, "Place Columns on Grid"))
                    {
                        t.Start();

                        foreach (var gx in gridsX)
                        {
                            foreach (var gy in gridsY)
                            {
                                var c1 = gx.Curve; var c2 = gy.Curve;
                                if (c1 == null || c2 == null) continue;

                                IntersectionResultArray ira;
                                var result = c1.Intersect(c2, out ira);
                                if (result != SetComparisonResult.Overlap || ira == null || ira.Size == 0) continue;

                                var p = ira.get_Item(0).XYZPoint;

                                if (!InsideCrop(p, tolFtVal))
                                {
                                    skipped.Add(new { at = new { x = p.X, y = p.Y }, reason = "outside_crop" });
                                    continue;
                                }

                                if (HasColNearby(p))
                                {
                                    skipped.Add(new { at = new { x = p.X, y = p.Y }, reason = "near_existing" });
                                    continue;
                                }

                                var inst = doc.Create.NewFamilyInstance(p, sym, levelBase, StructuralType.Column);

                                // Set de parámetros base/top + offsets
                                Action<Element, BuiltInParameter, ElementId> SetBip = (e, bip, id) =>
                                {
                                    var par = e.get_Parameter(bip);
                                    if (par != null && !par.IsReadOnly) par.Set(id);
                                };
                                Action<Element, BuiltInParameter, double> SetBipD = (e, bip, val) =>
                                {
                                    var par = e.get_Parameter(bip);
                                    if (par != null && !par.IsReadOnly) par.Set(val);
                                };

                                SetBip(inst, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, levelBase.Id);
                                SetBip(inst, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, levelTop.Id);
                                SetBipD(inst, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM, baseOffFt);
                                SetBipD(inst, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM, topOffFt);

                                // Orientación relativa si se pidió
                                if (orientAngle.HasValue)
                                {
                                    var lp = inst.Location as LocationPoint;
                                    if (lp != null) lp.Rotate(Line.CreateBound(p, p + XYZ.BasisZ), orientAngle.Value);
                                }

                                // Workset (si aplica)
                                if (targetWs != null)
                                {
                                    var wp = inst.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                                    if (wp != null && !wp.IsReadOnly) wp.Set(targetWs.Id.IntegerValue);
                                }

                                if (req.pinned == true) inst.Pinned = true;

                                created.Add(inst.Id.IntegerValue);
                            }
                        }

                        t.Commit();
                    }

                    tg.Assimilate();
                }

                return new
                {
                    createdCount = created.Count,
                    created = created,
                    skipped = skipped,
                    used = new
                    {
                        baseLevel = levelBase.Name,
                        topLevel = levelTop.Name,
                        familyType = sym.FamilyName + ": " + sym.Name,
                        baseOffset_m = req.baseOffset_m ?? 0.0,
                        topOffset_m = req.topOffset_m ?? 0.0,
                        tolerance_m = req.tolerance_m ?? 0.05,
                        orientation = string.IsNullOrWhiteSpace(req.orientationRelativeTo) ? "None" : req.orientationRelativeTo
                    }
                };
            };
        }


    }

}
