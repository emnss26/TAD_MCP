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
        public static System.Func<UIApplication, object> WallCreate(JObject args)
        {
            var req = args.ToObject<CreateWallRequest>();

            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new System.Exception("No active document.");
                var doc = uidoc.Document;

                // Nivel
                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(l => l.Name.Equals(req.level, System.StringComparison.OrdinalIgnoreCase))
                    ?? throw new System.Exception($"Level '{req.level}' not found.");

                // Tipo de muro
                var wtype = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType)).Cast<WallType>()
                    .FirstOrDefault(t =>
                        t.Name.Equals(req.wallType, System.StringComparison.OrdinalIgnoreCase) ||
                        $"{t.FamilyName}: {t.Name}".Equals(req.wallType, System.StringComparison.OrdinalIgnoreCase))
                    ?? throw new System.Exception($"WallType '{req.wallType}' not found.");

                // Geometría (m -> ft)
                double ToFt(double m) => Core.Units.MetersToFt(m);
                var p1 = new XYZ(ToFt(req.start.x), ToFt(req.start.y), 0);
                var p2 = new XYZ(ToFt(req.end.x), ToFt(req.end.y), 0);
                var line = Line.CreateBound(p1, p2);

                int id;
                using (var t = new Transaction(doc, "MCP: Wall.Create"))
                {
                    t.Start();
                    var wall = Wall.Create(doc, line, level.Id, req.structural);
                    wall.WallType = wtype;

                    var hParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (hParam == null || hParam.IsReadOnly)
                        throw new System.Exception("Cannot set wall height (WALL_USER_HEIGHT_PARAM).");

                    hParam.Set(ToFt(req.height_m));
                    id = wall.Id.IntegerValue;
                    t.Commit();
                }

                return new CreateWallResponse { elementId = id };
            };
        }
    }
}
