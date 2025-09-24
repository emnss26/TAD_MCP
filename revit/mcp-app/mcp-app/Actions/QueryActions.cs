using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Newtonsoft.Json.Linq;

namespace mcp_app.Actions
{
    internal class QueryActions
    {
        public static Func<UIApplication, object> LevelsList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .Select(l => new {
                        id = l.Id.IntegerValue,
                        name = l.Name,
                        elevation_ft = l.Elevation,
                        elevation_m = UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Meters)
                    })
                    .OrderBy(x => x.elevation_ft)
                    .ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> WallTypesList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                    .Select(t => new {
                        id = t.Id.IntegerValue,
                        family = t.FamilyName,
                        name = t.Name,
                        kind = t.Kind.ToString()
                    })
                    .OrderBy(x => x.family).ThenBy(x => x.name)
                    .ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> ActiveViewInfo(JObject _)
        {
            return (UIApplication app) =>
            {
                var v = app.ActiveUIDocument?.ActiveView ?? throw new Exception("No active view.");
                var vp = v as ViewPlan;
                string levelName = vp?.GenLevel?.Name;
                return new
                {
                    id = v.Id.IntegerValue,
                    v.Name,
                    v.ViewType,
                    level = levelName
                };
            };
        }
    }
}
