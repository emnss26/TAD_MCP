using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public static Func<UIApplication, object> ViewsList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate)
                    .Select(v => new {
                        id = v.Id.IntegerValue,
                        v.Name,
                        v.ViewType,
                        template = (v.ViewTemplateId != ElementId.InvalidElementId)
                    })
                    .OrderBy(x => x.ViewType.ToString()).ThenBy(x => x.Name)
                    .ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> SchedulesList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                    .Select(vs => new { id = vs.Id.IntegerValue, vs.Name })
                    .OrderBy(x => x.Name).ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> MaterialsList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                    .Select(m => new { id = m.Id.IntegerValue, m.Name })
                    .OrderBy(x => x.Name).ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> CategoriesList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = doc.Settings.Categories.Cast<Category>()
                    .Select(c => new { id = c.Id.IntegerValue, c.Name, bic = (BuiltInCategory)c.Id.IntegerValue })
                    .OrderBy(x => x.Name).ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> FamiliesTypesList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Select(fs => new {
                        id = fs.Id.IntegerValue,
                        family = fs.FamilyName,
                        type = fs.Name,
                        category = fs.Category?.Name
                    })
                    .OrderBy(x => x.category).ThenBy(x => x.family).ThenBy(x => x.type).ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> LinksList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                    .Select(li => new { id = li.Id.IntegerValue, name = li.Name, pinned = li.Pinned })
                    .OrderBy(x => x.name).ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> ImportsList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
                    .Select(ii => new { id = ii.Id.IntegerValue, ii.Name })
                    .OrderBy(x => x.Name).ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> WorksetsList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                var all = new FilteredWorksetCollector(doc).ToWorksets(); // IEnumerable<Workset>
                var items = all
                    .Select(ws => new
                    {
                        id = ws.Id.IntegerValue,
                        ws.Name,
                        kind = ws.Kind.ToString()
                    })
                    .OrderBy(x => x.kind).ThenBy(x => x.Name)
                    .ToList();

                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> TextNotesFind(JObject _)
        {
            return (UIApplication app) =>
            {
                var v = app.ActiveUIDocument?.ActiveView ?? throw new Exception("No active view.");
                var items = new FilteredElementCollector(v.Document, v.Id)
                    .OfClass(typeof(TextNote)).Cast<TextNote>()
                    .Select(tn => new { id = tn.Id.IntegerValue, text = tn.Text })
                    .ToList();
                return new { viewId = v.Id.IntegerValue, count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> DuctTypesList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(DuctType)).Cast<DuctType>()
                    .Select(t => new { id = t.Id.IntegerValue, family = t.FamilyName, name = t.Name })
                    .OrderBy(x => x.family).ThenBy(x => x.name).ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> PipeTypesList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>()
                    .Select(t => new { id = t.Id.IntegerValue, family = t.FamilyName, name = t.Name })
                    .OrderBy(x => x.family).ThenBy(x => x.name).ToList();
                return new { count = items.Count, items };
            };
        }

        public static Func<UIApplication, object> CableTrayTypesList(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var items = new FilteredElementCollector(doc).OfClass(typeof(CableTrayType)).Cast<CableTrayType>()
                    .Select(t => new { id = t.Id.IntegerValue, name = t.Name })
                    .OrderBy(x => x.name).ToList();
                return new { count = items.Count, items };
            };
        }
    }
}
