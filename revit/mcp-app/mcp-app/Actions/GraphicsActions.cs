using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using mcp_app.Actions;
using mcp_app.Contracts;
using mcp_app.Core;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace mcp_app.Actions
{
    internal class GraphicsActions
    {
        public static System.Func<UIApplication, object> SetVisibility(JObject args)
        {
            var categories = args["categories"]?.ToObject<List<string>>() ?? new List<string>();
            bool visible = args["visible"]?.ToObject<bool>() ?? true;
            bool forceDetach = args["forceDetachTemplate"]?.ToObject<bool>() ?? false;
            int? viewId = args["viewId"]?.ToObject<int?>();

            return (UIApplication app) =>
            {
                var v = ResolveView(app, viewId);
                EnsureTemplateWritable(v, forceDetach);

                using (var t = new Transaction(v.Document, "MCP: Set Category Visibility"))
                {
                    t.Start();
                    foreach (var tok in categories)
                    {
                        var cat = CategoryLookup.Require(v.Document, tok);
                        v.SetCategoryHidden(cat.Id, !visible);
                    }
                    t.Commit();
                }
                return new { changed = categories.Count };
            };
        }

        public static System.Func<UIApplication, object> ClearOverrides(JObject args)
        {
            var categories = args["categories"]?.ToObject<List<string>>() ?? new List<string>();
            bool forceDetach = args["forceDetachTemplate"]?.ToObject<bool>() ?? false;
            int? viewId = args["viewId"]?.ToObject<int?>();

            return (UIApplication app) =>
            {
                var v = ResolveView(app, viewId);
                EnsureTemplateWritable(v, forceDetach);

                using (var t = new Transaction(v.Document, "MCP: Clear Category Overrides"))
                {
                    t.Start();
                    foreach (var tok in categories)
                    {
                        var cat = CategoryLookup.Require(v.Document, tok);
                        v.SetCategoryOverrides(cat.Id, new OverrideGraphicSettings());
                    }
                    t.Commit();
                }
                return new { cleared = categories.Count };
            };
        }

        public static System.Func<UIApplication, object> OverrideColor(JObject args)
        {
            var categories = args["categories"]?.ToObject<List<string>>() ?? new List<string>();
            var colorAny = args["color"]?.ToObject<object>();
            int transparency = args["transparency"]?.ToObject<int?>() ?? 0;
            bool halftone = args["halftone"]?.ToObject<bool?>() ?? false;
            bool surfaceSolid = args["surfaceSolid"]?.ToObject<bool?>() ?? true;
            bool projectionLines = args["projectionLines"]?.ToObject<bool?>() ?? false;
            bool forceDetach = args["forceDetachTemplate"]?.ToObject<bool?>() ?? false;
            int? viewId = args["viewId"]?.ToObject<int?>();

            return (UIApplication app) =>
            {
                var v = ResolveView(app, viewId);
                EnsureTemplateWritable(v, forceDetach);
                var color = ColorParser.FromAny(colorAny);

                using (var t = new Transaction(v.Document, "MCP: Override Category Color"))
                {
                    t.Start();

                    var ogs = new OverrideGraphicSettings();

                    // Líneas
                    if (projectionLines)
                        ogs.SetProjectionLineColor(color);

                    // Superficie (solid fill)
                    if (surfaceSolid)
                    {
                        var solid = FindSolidFill(v.Document);
                        if (solid != null)
                        {
                            ogs.SetSurfaceForegroundPatternId(solid.Id);
                            ogs.SetSurfaceForegroundPatternColor(color);
                        }
                    }

                    if (transparency >= 0 && transparency <= 100)
                        ogs.SetSurfaceTransparency(transparency);

                    ogs.SetHalftone(halftone);

                    foreach (var tok in categories)
                    {
                        var cat = CategoryLookup.Require(v.Document, tok);
                        v.SetCategoryOverrides(cat.Id, ogs);
                    }
                    t.Commit();
                }
                return new { overridden = categories.Count, color = new { } };
            };
        }

        // ===== Helpers =====
        private static View ResolveView(UIApplication app, int? viewId)
        {
            var uidoc = app.ActiveUIDocument ?? throw new System.Exception("No active document.");
            if (viewId is null) return uidoc.ActiveView;
            var v = uidoc.Document.GetElement(new ElementId(viewId.Value)) as View;
            return v ?? throw new System.Exception($"View {viewId} not found.");
        }

        private static void EnsureTemplateWritable(View v, bool forceDetach)
        {
            if (v.ViewTemplateId != ElementId.InvalidElementId)
            {
                if (!forceDetach)
                    throw new System.Exception("View has a template; set forceDetachTemplate=true to detach.");
                v.ViewTemplateId = ElementId.InvalidElementId;
            }
        }

        private static FillPatternElement FindSolidFill(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern()?.IsSolidFill == true
                    || f.Name.Equals("Solid fill", System.StringComparison.OrdinalIgnoreCase)
                    || f.Name.Equals("Relleno sólido", System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
