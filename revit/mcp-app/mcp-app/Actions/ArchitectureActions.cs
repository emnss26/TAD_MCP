using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using mcp_app.Actions;
using mcp_app.Contracts;
using mcp_app.Core;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace mcp_app.Actions
{
    internal class ArchitectureActions
    {
        public static Func<UIApplication, object> WallCreate(JObject args)
        {
            var req = args.ToObject<CreateWallRequest>(); // level/wallType pueden venir nulos
            if (req == null) throw new Exception("Invalid args for wall.create.");

            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;

                // -------- Nivel (default: nivel de la vista activa si es ViewPlan; si no, el de menor elevación)
                Level level = null;
                if (!string.IsNullOrWhiteSpace(req.level))
                {
                    level = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .FirstOrDefault(l => l.Name.Equals(req.level, StringComparison.OrdinalIgnoreCase));
                    if (level == null) throw new Exception($"Level '{req.level}' not found.");
                }
                else
                {
                    var vp = uidoc.ActiveView as ViewPlan;
                    level = vp?.GenLevel
                         ?? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                              .OrderBy(l => l.Elevation).FirstOrDefault();
                    if (level == null) throw new Exception("No levels available in the document.");
                }

                // -------- Tipo de muro (default: primer básico no-cortina; prioriza nombres con "Generic")
                WallType wtype = null;
                if (!string.IsNullOrWhiteSpace(req.wallType))
                {
                    wtype = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType)).Cast<WallType>()
                        .FirstOrDefault(t =>
                            t.Name.Equals(req.wallType, StringComparison.OrdinalIgnoreCase) ||
                            $"{t.FamilyName}: {t.Name}".Equals(req.wallType, StringComparison.OrdinalIgnoreCase));
                    if (wtype == null) throw new Exception($"WallType '{req.wallType}' not found.");
                }
                else
                {
                    var candidates = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType)).Cast<WallType>()
                        .Where(t => t.Kind != WallKind.Curtain) // evita cortinas como default
                        .ToList();

                    wtype = candidates
                        .OrderByDescending(t => t.Name.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) >= 0)
                        .ThenBy(t => t.Name)
                        .FirstOrDefault();

                    if (wtype == null) throw new Exception("No suitable WallType found.");
                }

                // -------- Altura (default 3.0 m si falta o <= 0)
                var height_m = (req.height_m > 0) ? req.height_m : 3.0;

                // -------- Geometría (m → ft) y validación simple
                double ToFt(double m) => Core.Units.MetersToFt(m);
                var p1 = new XYZ(ToFt(req.start.x), ToFt(req.start.y), 0);
                var p2 = new XYZ(ToFt(req.end.x), ToFt(req.end.y), 0);
                if (p1.IsAlmostEqualTo(p2)) throw new Exception("Start and end points are the same.");
                var line = Line.CreateBound(p1, p2);

                int id;
                using (var t = new Transaction(doc, "MCP: Wall.Create"))
                {
                    t.Start();

                    var wall = Wall.Create(doc, line, level.Id, req.structural);
                    wall.WallType = wtype;

                    var hParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (hParam == null || hParam.IsReadOnly)
                        throw new Exception("Cannot set wall height (WALL_USER_HEIGHT_PARAM).");

                    hParam.Set(ToFt(height_m));
                    id = wall.Id.IntegerValue;

                    t.Commit();
                }

                // Devolvemos también qué defaults se usaron (para que el MCP los muestre)
                return new
                {
                    elementId = id,
                    used = new
                    {
                        level = level.Name,
                        wallType = $"{wtype.FamilyName}: {wtype.Name}",
                        height_m
                    }
                };
            };
        }
    }
}
