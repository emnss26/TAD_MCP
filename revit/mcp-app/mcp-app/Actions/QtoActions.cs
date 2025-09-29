using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;

using Newtonsoft.Json.Linq;

using mcp_app.Core;

namespace mcp_app.Actions
{
    internal class QtoActions
    {
        // ========= Helpers de conversión =========
        static double FtToM(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Meters);
        static double Ft2ToM2(double ft2) => UnitUtils.ConvertFromInternalUnits(ft2, UnitTypeId.SquareMeters);
        static double Ft3ToM3(double ft3) => UnitUtils.ConvertFromInternalUnits(ft3, UnitTypeId.CubicMeters);
        static double FtToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        static double? GetDoubleParam(Element e, BuiltInParameter bip)
        {
            var p = e?.get_Parameter(bip);
            if (p == null || p.StorageType != StorageType.Double) return null;
            try { return p.AsDouble(); } catch { return null; }
        }

        static double? GetAnyVolume(Element e)
        {
            // 1) Primero intenta con HOST_VOLUME_COMPUTED
            var v = GetDoubleParam(e, BuiltInParameter.HOST_VOLUME_COMPUTED);
            if (v.HasValue && v.Value > 1e-9) return v;

            // 2) Fallback: sumar volúmenes de sólidos en la geometría
            try
            {
                var opt = new Options
                {
                    ComputeReferences = false,
                    DetailLevel = ViewDetailLevel.Fine,
                    IncludeNonVisibleObjects = true
                };

                var ge = e.get_Geometry(opt);
                if (ge == null) return null;

                double sum = 0.0;
                foreach (var obj in ge)
                    sum += VolumeOf(obj);

                return sum > 1e-9 ? sum : (double?)null;
            }
            catch
            {
                return null;
            }
        }

        static double VolumeOf(GeometryObject go)
        {
            double acc = 0.0;

            if (go is Solid s)
            {
                if (s.Volume > 1e-9) acc += s.Volume;
            }
            else if (go is GeometryInstance gi)
            {
                var inst = gi.GetInstanceGeometry();
                if (inst != null)
                    foreach (var obj in inst)
                        acc += VolumeOf(obj);
            }
            else if (go is GeometryElement ge)
            {
                foreach (var obj in ge)
                    acc += VolumeOf(obj);
            }

            return acc;
        }

        static string GetTypeName(Element el, Document doc)
        {
            var tid = el.GetTypeId();
            if (tid == ElementId.InvalidElementId) return null;
            return doc.GetElement(tid)?.Name;
        }

        static string GetLevelName(Element el, Document doc)
        {
            ElementId levelId = ElementId.InvalidElementId;

            // Instancia de familia
            if (el is FamilyInstance fi && fi.LevelId != null && fi.LevelId != ElementId.InvalidElementId)
                levelId = fi.LevelId;
            else if (el.LevelId != null && el.LevelId != ElementId.InvalidElementId)
                levelId = el.LevelId;

            // MEP Curves suelen tener params RBS_START_LEVEL_PARAM
            if (levelId == ElementId.InvalidElementId)
            {
                var p = el.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    try { levelId = p.AsElementId(); } catch { }
                }
            }

            if (levelId == ElementId.InvalidElementId) return null;
            return doc.GetElement(levelId)?.Name;
        }

        static string GetCreatedPhaseName(Element el, Document doc)
        {
            var p = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
            if (p == null || p.StorageType != StorageType.ElementId) return null;
            try
            {
                var id = p.AsElementId();
                if (id == ElementId.InvalidElementId) return null;
                return (doc.GetElement(id) as Phase)?.Name;
            }
            catch { return null; }
        }

        static string KeyFromGroupBy(Dictionary<string, string> parts, string[] groupBy)
        {
            if (groupBy == null || groupBy.Length == 0) return "(total)";
            var vals = new List<string>();
            foreach (var g in groupBy)
            {
                parts.TryGetValue(g ?? "", out var v);
                vals.Add($"{g}:{(v ?? "-")}");
            }
            return string.Join("|", vals);
        }

        static object KeyObjFromGroupBy(Dictionary<string, string> parts, string[] groupBy)
        {
            if (groupBy == null || groupBy.Length == 0) return new { key = "(total)" };
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groupBy) dict[g] = parts.TryGetValue(g, out var v) ? (object)(v ?? "") : "";
            return dict;
        }

        // =========================================================
        // qto.walls
        // Args: { groupBy?: ("type"|"level"|"phase")[], includeIds?: boolean }
        // =========================================================
        public class QtoWallsReq
        {
            public string[] groupBy { get; set; } = Array.Empty<string>();
            public bool includeIds { get; set; } = false;
        }

        public static Func<UIApplication, object> QtoWalls(JObject args)
        {
            var req = args.ToObject<QtoWallsReq>() ?? new QtoWallsReq();
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                var walls = new FilteredElementCollector(doc)
                    .OfClass(typeof(Wall)).Cast<Wall>()
                    .Where(w => w.Category != null && w.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Walls)
                    .ToList();

                double totalLenM = 0, totalAreaM2 = 0, totalVolM3 = 0;
                var groups = new Dictionary<string, (double len, double area, double vol, int count, object keyObj)>();
                var rows = new List<object>();

                foreach (var w in walls)
                {
                    var len_ft = (w.Location as LocationCurve)?.Curve?.Length ?? 0.0;
                    var area_ft2 = GetDoubleParam(w, BuiltInParameter.HOST_AREA_COMPUTED) ?? 0.0;
                    var vol_ft3 = GetDoubleParam(w, BuiltInParameter.HOST_VOLUME_COMPUTED) ?? 0.0;

                    var len_m = FtToM(len_ft);
                    var area_m2 = Ft2ToM2(area_ft2);
                    var vol_m3 = Ft3ToM3(vol_ft3);

                    totalLenM += len_m;
                    totalAreaM2 += area_m2;
                    totalVolM3 += vol_m3;

                    // Metadatos
                    var type = GetTypeName(w, doc);
                    var level = GetLevelName(w, doc);
                    var phase = GetCreatedPhaseName(w, doc);
                    bool roomBounding = false;
                    try
                    {
                        var rb = w.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                        if (rb != null && rb.StorageType == StorageType.Integer) roomBounding = rb.AsInteger() == 1;
                    }
                    catch { }

                    var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["type"] = type,
                        ["level"] = level,
                        ["phase"] = phase
                    };

                    var gk = KeyFromGroupBy(parts, req.groupBy);
                    if (!groups.TryGetValue(gk, out var acc))
                    {
                        acc = (0, 0, 0, 0, KeyObjFromGroupBy(parts, req.groupBy));
                    }
                    acc.len += len_m;
                    acc.area += area_m2;
                    acc.vol += vol_m3;
                    acc.count += 1;
                    groups[gk] = acc;

                    if (req.includeIds)
                    {
                        rows.Add(new
                        {
                            id = w.Id.IntegerValue,
                            type,
                            level,
                            phase,
                            roomBounding,
                            length_m = len_m,
                            area_m2 = area_m2,
                            volume_m3 = vol_m3
                        });
                    }
                }

                var groupRows = groups.Select(kv => new
                {
                    key = kv.Value.keyObj,
                    count = kv.Value.count,
                    length_m = kv.Value.len,
                    area_m2 = kv.Value.area,
                    volume_m3 = kv.Value.vol
                }).ToList();

                return new
                {
                    summary = new
                    {
                        totalCount = walls.Count,
                        totalLength_m = totalLenM,
                        totalArea_m2 = totalAreaM2,
                        totalVolume_m3 = totalVolM3
                    },
                    groups = groupRows,
                    rows = req.includeIds ? rows : null
                };
            };
        }

        // =========================================================
        // qto.floors
        // Args: { groupBy?: ("type"|"level")[], includeIds?: boolean }
        // =========================================================
        public class QtoFloorsReq
        {
            public string[] groupBy { get; set; } = Array.Empty<string>();
            public bool includeIds { get; set; } = false;
        }

        public static Func<UIApplication, object> QtoFloors(JObject args)
        {
            var req = args.ToObject<QtoFloorsReq>() ?? new QtoFloorsReq();
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                var floors = new FilteredElementCollector(doc)
                    .OfClass(typeof(Floor)).Cast<Floor>()
                    .Where(f => f.Category != null && f.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Floors)
                    .ToList();

                double totalAreaM2 = 0, totalVolM3 = 0;
                var groups = new Dictionary<string, (double area, double vol, int count, object keyObj)>();
                var rows = new List<object>();

                foreach (var f in floors)
                {
                    var area_ft2 = GetDoubleParam(f, BuiltInParameter.HOST_AREA_COMPUTED) ?? 0.0;
                    var vol_ft3 = GetAnyVolume(f) ?? 0.0;

                    var area_m2 = Ft2ToM2(area_ft2);
                    var vol_m3 = Ft3ToM3(vol_ft3);

                    totalAreaM2 += area_m2;
                    totalVolM3 += vol_m3;

                    var type = GetTypeName(f, doc);
                    var level = GetLevelName(f, doc);

                    var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["type"] = type,
                        ["level"] = level
                    };

                    var gk = KeyFromGroupBy(parts, req.groupBy);
                    if (!groups.TryGetValue(gk, out var acc))
                    {
                        acc = (0, 0, 0, KeyObjFromGroupBy(parts, req.groupBy));
                    }
                    acc.area += area_m2;
                    acc.vol += vol_m3;
                    acc.count += 1;
                    groups[gk] = acc;

                    if (req.includeIds)
                    {
                        rows.Add(new
                        {
                            id = f.Id.IntegerValue,
                            type,
                            level,
                            area_m2 = area_m2,
                            volume_m3 = vol_m3
                        });
                    }
                }

                var groupRows = groups.Select(kv => new
                {
                    key = kv.Value.keyObj,
                    count = kv.Value.count,
                    area_m2 = kv.Value.area,
                    volume_m3 = kv.Value.vol
                }).ToList();

                return new
                {
                    summary = new
                    {
                        totalCount = floors.Count,
                        totalArea_m2 = totalAreaM2,
                        totalVolume_m3 = totalVolM3
                    },
                    groups = groupRows,
                    rows = req.includeIds ? rows : null
                };
            };
        }

        // =========================================================
        // qto.struct.concrete
        // Args: { includeBeams?:bool, includeColumns?:bool, includeFoundation?:bool, groupBy?: ("type"|"level")[] }
        // Volumen y m.l. donde aplique (beams).
        // =========================================================
        public class QtoStructConcreteReq
        {
            public bool includeBeams { get; set; } = true;
            public bool includeColumns { get; set; } = true;
            public bool includeFoundation { get; set; } = true;
            public string[] groupBy { get; set; } = Array.Empty<string>();
            public bool includeIds { get; set; } = false;
        }

        public static Func<UIApplication, object> QtoStructConcrete(JObject args)
        {
            var req = args.ToObject<QtoStructConcreteReq>() ?? new QtoStructConcreteReq();
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                var rows = new List<object>();
                var groups = new Dictionary<string, (double vol, double ml, int count, object keyObj)>();

                void Acc(string type, string level, int id, double? vol_ft3, double? ml_ft, bool addMl)
                {
                    var vol_m3 = Ft3ToM3(vol_ft3 ?? 0.0);
                    var ml_m = FtToM(addMl ? (ml_ft ?? 0.0) : 0.0);

                    var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["type"] = type,
                        ["level"] = level
                    };
                    var gk = KeyFromGroupBy(parts, req.groupBy);
                    if (!groups.TryGetValue(gk, out var acc))
                    {
                        acc = (0, 0, 0, KeyObjFromGroupBy(parts, req.groupBy));
                    }
                    acc.vol += vol_m3;
                    acc.ml += ml_m;
                    acc.count += 1;
                    groups[gk] = acc;

                    if (req.includeIds)
                    {
                        rows.Add(new { id, type, level, volume_m3 = vol_m3, ml_m = ml_m });
                    }
                }

                if (req.includeBeams)
                {
                    var beams = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WhereElementIsNotElementType()
                        .Cast<Element>().ToList();

                    foreach (var b in beams)
                    {
                        var type = GetTypeName(b, doc);
                        var level = GetLevelName(b, doc);
                        var vol = GetAnyVolume(b);
                        var len = (b.Location as LocationCurve)?.Curve?.Length; // m.l. sí aplica
                        Acc(type, level, b.Id.IntegerValue, vol, len, addMl: true);
                    }
                }

                if (req.includeColumns)
                {
                    var cols = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_StructuralColumns)
                        .WhereElementIsNotElementType()
                        .Cast<Element>().ToList();

                    foreach (var c in cols)
                    {
                        var type = GetTypeName(c, doc);
                        var level = GetLevelName(c, doc);
                        var vol = GetAnyVolume(c);
                        // m.l. de columnas es ambiguo; no lo sumamos por defecto
                        Acc(type, level, c.Id.IntegerValue, vol, ml_ft: null, addMl: false);
                    }
                }

                if (req.includeFoundation)
                {
                    var fnds = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                        .WhereElementIsNotElementType()
                        .Cast<Element>().ToList();

                    foreach (var f in fnds)
                    {
                        var type = GetTypeName(f, doc);
                        var level = GetLevelName(f, doc);
                        var vol = GetAnyVolume(f);
                        // m.l. para zapatas/corridas varía: lo omitimos en totales
                        Acc(type, level, f.Id.IntegerValue, vol, ml_ft: null, addMl: false);
                    }
                }

                var groupRows = groups.Select(kv => new
                {
                    key = kv.Value.keyObj,
                    count = kv.Value.count,
                    volume_m3 = kv.Value.vol,
                    ml_m = kv.Value.ml
                }).ToList();

                return new
                {
                    groups = groupRows,
                    rows = req.includeIds ? rows : null
                };
            };
        }

        // =========================================================
        // qto.mep.pipes
        // Args: { groupBy?: ("system"|"type"|"level"), diameterBucketsMm?: number[] }
        // m.l. totales y por bucket
        // =========================================================
        public class QtoPipesReq
        {
            public string[] groupBy { get; set; } = Array.Empty<string>();
            public double[] diameterBucketsMm { get; set; } = Array.Empty<double>(); // limites superiores
            public bool includeIds { get; set; } = false;
        }

        public static Func<UIApplication, object> QtoMepPipes(JObject args)
        {
            var req = args.ToObject<QtoPipesReq>() ?? new QtoPipesReq();
            var buckets = (req.diameterBucketsMm ?? Array.Empty<double>()).OrderBy(x => x).ToArray();

            string BucketName(double mm)
            {
                if (buckets.Length == 0) return "all";
                foreach (var b in buckets)
                    if (mm <= b) return $"≤{b}mm";
                return $">{buckets.Last()}mm";
            }

            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                var pipes = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe)).Cast<Pipe>()
                    .ToList();

                var groups = new Dictionary<string, (double ml, Dictionary<string, double> byBucket, int count, object keyObj)>();
                var rows = new List<object>();

                foreach (var p in pipes)
                {
                    var len_ft = (p.Location as LocationCurve)?.Curve?.Length ?? 0.0;
                    var len_m = FtToM(len_ft);

                    // diámetro (mm)
                    var d_ft = GetDoubleParam(p, BuiltInParameter.RBS_PIPE_DIAMETER_PARAM) ?? 0.0;
                    var d_mm = FtToMm(d_ft);
                    var bucket = BucketName(d_mm);

                    var sysName = p.MEPSystem?.Name; // puede ser null
                    var type = GetTypeName(p, doc);
                    var level = GetLevelName(p, doc);

                    var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["system"] = sysName,
                        ["type"] = type,
                        ["level"] = level
                    };

                    var gk = KeyFromGroupBy(parts, req.groupBy);
                    if (!groups.TryGetValue(gk, out var acc))
                    {
                        acc = (0, new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase), 0, KeyObjFromGroupBy(parts, req.groupBy));
                    }
                    acc.ml += len_m;
                    acc.count += 1;
                    acc.byBucket[bucket] = (acc.byBucket.TryGetValue(bucket, out var old) ? old : 0) + len_m;
                    groups[gk] = acc;

                    if (req.includeIds)
                    {
                        rows.Add(new
                        {
                            id = p.Id.IntegerValue,
                            system = sysName,
                            type,
                            level,
                            diameter_mm = d_mm,
                            length_m = len_m
                        });
                    }
                }

                var groupRows = groups.Select(kv => new
                {
                    key = kv.Value.keyObj,
                    count = kv.Value.count,
                    length_m = kv.Value.ml,
                    buckets = kv.Value.byBucket.Select(b => new { bucket = b.Key, length_m = b.Value }).OrderBy(x => x.bucket).ToList()
                }).ToList();

                return new
                {
                    groups = groupRows,
                    rows = req.includeIds ? rows : null
                };
            };
        }

        // =========================================================
        // qto.mep.ducts
        // Args: { groupBy?: ("system"|"type"|"level"), roundVsRect?: boolean }
        // m.l. y área superficial estimada (opcional)
        // =========================================================
        public class QtoDuctsReq
        {
            public string[] groupBy { get; set; } = Array.Empty<string>();
            public bool roundVsRect { get; set; } = false;
            public bool includeIds { get; set; } = false;
        }

        public static Func<UIApplication, object> QtoMepDucts(JObject args)
        {
            var req = args.ToObject<QtoDuctsReq>() ?? new QtoDuctsReq();

            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                var ducts = new FilteredElementCollector(doc)
                    .OfClass(typeof(Duct)).Cast<Duct>()
                    .ToList();

                var groups = new Dictionary<string, (double ml, double areaSurf, int count, object keyObj)>();
                var rows = new List<object>();

                foreach (var d in ducts)
                {
                    var len_ft = (d.Location as LocationCurve)?.Curve?.Length ?? 0.0;
                    var len_m = FtToM(len_ft);

                    // tamaños
                    var diam_ft = GetDoubleParam(d, BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                    var w_ft = GetDoubleParam(d, BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                    var h_ft = GetDoubleParam(d, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);

                    bool isRound = diam_ft.HasValue && diam_ft.Value > 1e-9;
                    double areaSurf_m2 = 0.0;

                    if (isRound)
                    {
                        var d_m = FtToM(diam_ft.Value);
                        areaSurf_m2 = Math.PI * d_m * len_m; // Área lateral ~ pi*d*L
                    }
                    else if (w_ft.HasValue && h_ft.HasValue && w_ft.Value > 0 && h_ft.Value > 0)
                    {
                        var w_m = FtToM(w_ft.Value);
                        var h_m = FtToM(h_ft.Value);
                        var per_m = 2 * (w_m + h_m);
                        areaSurf_m2 = per_m * len_m;
                    }

                    var sysName = d.MEPSystem?.Name;
                    var type = GetTypeName(d, doc);
                    var level = GetLevelName(d, doc);

                    var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["system"] = sysName,
                        ["type"] = type,
                        ["level"] = level
                    };
                    if (req.roundVsRect)
                        parts["shape"] = isRound ? "round" : "rect";

                    var gk = KeyFromGroupBy(parts, req.groupBy.Concat(req.roundVsRect ? new[] { "shape" } : Array.Empty<string>()).ToArray());
                    if (!groups.TryGetValue(gk, out var acc))
                    {
                        acc = (0, 0, 0, KeyObjFromGroupBy(parts, req.groupBy.Concat(req.roundVsRect ? new[] { "shape" } : Array.Empty<string>()).ToArray()));
                    }
                    acc.ml += len_m;
                    acc.areaSurf += areaSurf_m2;
                    acc.count += 1;
                    groups[gk] = acc;

                    if (req.includeIds)
                    {
                        rows.Add(new
                        {
                            id = d.Id.IntegerValue,
                            system = sysName,
                            type,
                            level,
                            shape = isRound ? "round" : "rect",
                            length_m = len_m,
                            surface_area_m2 = areaSurf_m2
                        });
                    }
                }

                var groupRows = groups.Select(kv => new
                {
                    key = kv.Value.keyObj,
                    count = kv.Value.count,
                    length_m = kv.Value.ml,
                    surface_area_m2 = kv.Value.areaSurf
                }).ToList();

                return new
                {
                    groups = groupRows,
                    rows = req.includeIds ? rows : null
                };
            };
        }
    }
}
