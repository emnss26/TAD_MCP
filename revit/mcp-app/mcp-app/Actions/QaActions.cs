using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace mcp_app.Actions
{
    internal class QaActions
    {
        // Pin all Revit links
        public static Func<UIApplication, object> FixPinAllLinks(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                int pinned = 0;
                using (var t = new Transaction(doc, "MCP: QA.PinAllLinks"))
                {
                    t.Start();
                    foreach (var li in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                    {
                        if (!li.Pinned) { li.Pinned = true; pinned++; }
                    }
                    t.Commit();
                }
                return new { pinned };
            };
        }

        // Delete all CAD imports
        public static Func<UIApplication, object> FixDeleteImports(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var ids = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).ToElementIds();
                int deleted = 0;
                using (var t = new Transaction(doc, "MCP: QA.DeleteImports"))
                {
                    t.Start();
                    if (ids.Count > 0) { deleted = doc.Delete(ids).Count; }
                    t.Commit();
                }
                return new { deleted };
            };
        }

        private class ApplyViewTemplatesReq
        {
            public string templateName { get; set; }   // opcional
            public int? templateId { get; set; }       // opcional
            public bool onlyWithoutTemplate { get; set; } = true;
            public int[] viewIds { get; set; }         // opcional; si no, todas las no-template

            public bool? autoPickFirst { get; set; }   //         // opcional; si no, todas las no-template
        }

        public static Func<UIApplication, object> FixApplyViewTemplates(JObject args)
        {
            var req = args.ToObject<ApplyViewTemplatesReq>() ?? new ApplyViewTemplatesReq();
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                // Todas las plantillas disponibles (ordenadas por nombre)
                var allTemplates = new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .Where(v => v.IsTemplate)
                    .OrderBy(v => v.Name)
                    .ToList();

                if (allTemplates.Count == 0)
                    throw new Exception("No view templates available in this model.");

                // Resolver la plantilla a usar
                View viewTemplate = null;
                bool autoPicked = false;

                if (req.templateId is int tid && tid > 0)
                {
                    viewTemplate = doc.GetElement(new ElementId(tid)) as View;
                    if (viewTemplate == null || !viewTemplate.IsTemplate)
                        throw new Exception($"Template id {tid} not found or not a template.");
                }
                else if (!string.IsNullOrWhiteSpace(req.templateName))
                {
                    viewTemplate = allTemplates.FirstOrDefault(v =>
                        v.Name.Equals(req.templateName, StringComparison.OrdinalIgnoreCase));
                    if (viewTemplate == null)
                        throw new Exception($"Template '{req.templateName}' not found.");
                }
                else
                {
                    // No se especificó plantilla: decidir si autoseleccionamos o "preguntamos"
                    if (allTemplates.Count == 1 || req.autoPickFirst == true)
                    {
                        viewTemplate = allTemplates[0];
                        autoPicked = true;
                    }
                    else
                    {
                        // Devolver lista para que el usuario elija (no es error)
                        return new
                        {
                            needTemplate = true,
                            message = "Multiple view templates found. Please specify templateName or templateId.",
                            availableTemplates = allTemplates
                                .Select(t => new { id = t.Id.IntegerValue, name = t.Name })
                                .ToArray()
                        };
                    }
                }

                // Resolver vistas objetivo
                var targets = (req.viewIds != null && req.viewIds.Length > 0)
                    ? req.viewIds
                        .Select(id => doc.GetElement(new ElementId(id)) as View)
                        .Where(v => v != null && !v.IsTemplate)
                        .ToList()
                    : new FilteredElementCollector(doc)
                        .OfClass(typeof(View)).Cast<View>()
                        .Where(v => !v.IsTemplate)
                        .ToList();

                int applied = 0;
                using (var t = new Transaction(doc, "MCP: QA.ApplyViewTemplates"))
                {
                    t.Start();
                    foreach (var v in targets)
                    {
                        var hasTemplate = v.ViewTemplateId != ElementId.InvalidElementId;
                        if (req.onlyWithoutTemplate && hasTemplate) continue;
                        v.ViewTemplateId = viewTemplate.Id;
                        applied++;
                    }
                    t.Commit();
                }

                // Puedes devolver una muestra de templates para UI (p.ej. primeros 20)
                var sample = allTemplates
                    .Take(20)
                    .Select(t => new { id = t.Id.IntegerValue, name = t.Name })
                    .ToArray();

                return new
                {
                    applied,
                    total = targets.Count,
                    template = viewTemplate.Name,
                    templateId = viewTemplate.Id.IntegerValue,
                    autoPicked,
                    availableTemplatesSample = sample
                };
            };
        }

        // Remove text notes from active or given view
        private class RemoveTextNotesReq { public int? viewId { get; set; } }

        public static Func<UIApplication, object> FixRemoveTextNotes(JObject args)
        {
            var req = args.ToObject<RemoveTextNotesReq>() ?? new RemoveTextNotesReq();
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var v = (req.viewId is int vid) ? uidoc.Document.GetElement(new ElementId(vid)) as View : uidoc.ActiveView;
                if (v == null) throw new Exception("Target view not found.");

                var ids = new FilteredElementCollector(v.Document, v.Id).OfClass(typeof(TextNote)).ToElementIds();
                int deleted = 0;
                using (var t = new Transaction(v.Document, "MCP: QA.RemoveTextNotes"))
                {
                    t.Start();
                    if (ids.Count > 0) deleted = v.Document.Delete(ids).Count;
                    t.Commit();
                }
                return new { viewId = v.Id.IntegerValue, deleted };
            };
        }

        // Delete unused ElementTypes (best-effort)
        public static Func<UIApplication, object> FixDeleteUnusedTypes(JObject _)
        {
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                var types = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .Where(et =>
                        !(et is ViewFamilyType) &&            // no vistas
                        et.Category != null &&
                        et.Category.Id.IntegerValue != (int)BuiltInCategory.OST_TitleBlocks) // no títulos
                    .ToList();

                int deleted = 0, failed = 0;
                using (var t = new Transaction(doc, "MCP: QA.DeleteUnusedTypes"))
                {
                    t.Start();
                    foreach (var et in types)
                    {
                        try { doc.Delete(et.Id); deleted++; }
                        catch { failed++; }
                    }
                    t.Commit();
                }
                return new { deleted, failed };
            };
        }

        // Rename views (prefix y/o find/replace)
        private class RenameViewsReq
        {
            public string prefix { get; set; }   // opcional
            public string find { get; set; }     // opcional
            public string replace { get; set; }  // opcional
            public int[] viewIds { get; set; }   // opcional
        }

        public static Func<UIApplication, object> FixRenameViews(JObject args)
        {
            var req = args.ToObject<RenameViewsReq>() ?? new RenameViewsReq();
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var targets = (req.viewIds != null && req.viewIds.Length > 0)
                    ? req.viewIds.Select(id => doc.GetElement(new ElementId(id)) as View).Where(v => v != null && !v.IsTemplate).ToList()
                    : new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).ToList();

                int renamed = 0;
                using (var t = new Transaction(doc, "MCP: QA.RenameViews"))
                {
                    t.Start();
                    foreach (var v in targets)
                    {
                        var name = v.Name;
                        if (!string.IsNullOrEmpty(req.find))
                            name = name.Replace(req.find, req.replace ?? string.Empty);
                        if (!string.IsNullOrEmpty(req.prefix) && !name.StartsWith(req.prefix))
                            name = req.prefix + name;

                        if (!string.Equals(name, v.Name, StringComparison.Ordinal))
                        {
                            try { v.Name = name; renamed++; } catch { /* puede colisionar con nombres duplicados */ }
                        }
                    }
                    t.Commit();
                }
                return new { renamed, total = targets.Count };
            };
        }
    }
}
