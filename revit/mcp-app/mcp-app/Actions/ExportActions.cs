using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using mcp_app.Contracts;
using mcp_app.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace mcp_app.Actions
{
    internal class ExportActions
    {
        public static Func<UIApplication, object> ExportNwc(JObject args)
        {
            var req = args.ToObject<ExportNwcRequest>() ?? throw new Exception("Invalid args for export.nwc.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;

                var opts = new NavisworksExportOptions
                {
                    ExportElementIds = true,
                    ConvertElementProperties = req.convertElementProperties
                    // ExportLinkedFiles: muchos SDK no lo exponen
                };

                var folder = req.folder ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                Directory.CreateDirectory(folder);

                // --- Resolver conjunto de vistas 3D ---
                var targets = new List<View3D>();

                // 1) Por viewId (single)
                if (req.viewId.HasValue && req.viewId.Value > 0)
                {
                    var v = doc.GetElement(new ElementId(req.viewId.Value)) as View3D;
                    if (v != null && !v.IsTemplate) targets.Add(v);
                }

                // 2) Por viewIds (multi)
                if (req.viewIds != null && req.viewIds.Length > 0)
                {
                    foreach (var id in req.viewIds)
                    {
                        var v = doc.GetElement(new ElementId(id)) as View3D;
                        if (v != null && !v.IsTemplate && !targets.Any(x => x.Id.IntegerValue == v.Id.IntegerValue))
                            targets.Add(v);
                    }
                }

                // 3) Por viewName (single)
                if (!string.IsNullOrWhiteSpace(req.viewName))
                {
                    var v = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                            .FirstOrDefault(x => !x.IsTemplate && x.Name.Equals(req.viewName, StringComparison.OrdinalIgnoreCase));
                    if (v != null && !targets.Any(x => x.Id.IntegerValue == v.Id.IntegerValue)) targets.Add(v);
                }

                // 4) Por viewNames (multi)
                if (req.viewNames != null && req.viewNames.Length > 0)
                {
                    var all3d = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                                 .Where(x => !x.IsTemplate).ToList();
                    foreach (var name in req.viewNames)
                    {
                        var v = all3d.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (v != null && !targets.Any(x => x.Id.IntegerValue == v.Id.IntegerValue))
                            targets.Add(v);
                    }
                }

                // 5) Fallback: activa o primera 3D del modelo
                if (targets.Count == 0)
                {
                    var av3d = uidoc.ActiveView as View3D;
                    if (av3d != null && !av3d.IsTemplate) targets.Add(av3d);
                    else
                    {
                        var any3d = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                                     .FirstOrDefault(x => !x.IsTemplate)
                                     ?? throw new Exception("No 3D view available for NWC export.");
                        targets.Add(any3d);
                    }
                }

                // --- Exportar una por una (nombra por vista y/o prefijo) ---
                var results = new List<object>();

                foreach (var v in targets)
                {
                    try
                    {
                        uidoc.RequestViewChange(v);

                        // Si hay varias vistas y se dio filename, úsalo como prefijo
                        // Si no, usa el nombre de la vista directamente
                        var baseName =
                            (targets.Count > 1 && !string.IsNullOrWhiteSpace(req.filename))
                            ? req.filename + "_" + SafeFileName(v.Name)
                            : (!string.IsNullOrWhiteSpace(req.filename) ? req.filename : SafeFileName(v.Name));

                        var path = Path.Combine(folder, baseName + ".nwc");

                        doc.Export(folder, baseName, opts);

                        results.Add(new { ok = true, viewId = v.Id.IntegerValue, viewName = v.Name, path = path });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { ok = false, viewId = v.Id.IntegerValue, viewName = v.Name, error = ex.Message });
                    }
                }

                return new { count = results.Count, results = results };
            };
        }

        // Reemplazar caracteres inválidos en nombres de archivo (Windows)
        private static string SafeFileName(string name)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            return sb.ToString();
        }

        public static Func<UIApplication, object> ExportDwg(JObject args)
        {
            var req = args.ToObject<ExportDwgRequest>() ?? throw new Exception("Invalid args for export.dwg.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;
                var folder = req.folder ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                Directory.CreateDirectory(folder);
                var name = req.filename ?? "export";

                var views = new List<ElementId>();
                if (req.viewIds != null && req.viewIds.Length > 0)
                    views.AddRange(req.viewIds.Select(id => new ElementId(id)));
                else
                    views.Add(uidoc.ActiveView.Id);

                var opts = new DWGExportOptions();
                if (!string.IsNullOrWhiteSpace(req.exportSetupName))
                {
                    // Si el usuario configuró un setup, se puede cargar por nombre con API extendidas;
                    // aquí dejamos las defaults por simplicidad.
                }

                var ok = doc.Export(folder, name, views, opts);
                return new { ok, folder, name };
            };
        }

        public static Func<UIApplication, object> ExportPdf(JObject args)
        {
            var req = args.ToObject<ExportPdfRequest>() ?? throw new Exception("Invalid args for export.pdf.");
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
#if REVIT2022_OR_GREATER
                var folder = req.folder ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                Directory.CreateDirectory(folder);
                var name = (req.filename ?? "set") + ".pdf";
                var path = Path.Combine(folder, name);

                var pdfOpts = new PDFExportOptions
                {
                    Combine = req.combine,
                };

                var ids = (req.viewOrSheetIds != null && req.viewOrSheetIds.Length > 0)
                    ? req.viewOrSheetIds.Select(i => new ElementId(i)).ToList()
                    : new List<ElementId> { doc.ActiveView.Id };

                var ok = doc.Export(folder, (req.filename ?? "set"), ids, pdfOpts);
                return new { ok, path };
#else
                throw new Exception("PDF export requires Revit 2022 or newer.");
#endif
            };
        }
    }
}

