using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using mcp_app.Actions;
using mcp_app.Contracts;
using mcp_app.Core;

namespace mcp_app.Actions
{
    internal class DocActions
    {
        // ---------- modelos de request ----------
        private class SheetItem { public string number; public string name; }
        private class SheetsCreateBulkReq
        {
            public string titleBlockType { get; set; }   // opcional ("Family: Type" o solo Type)
            public SheetItem[] items { get; set; } = Array.Empty<SheetItem>();
        }

        private class SheetsAddViewsReq
        {
            public int sheetId { get; set; }                  // opcional si sheetName
            public string sheetName { get; set; }             // opcional si sheetId
            public int[] viewIds { get; set; }                // opcional si viewNames
            public string[] viewNames { get; set; }           // opcional si viewIds
        }

        private class SheetsAssignRevisionsReq
        {
            public int[] sheetIds { get; set; }
            public string[] sheetNames { get; set; }
            public string revisionName { get; set; }          // requerido (usaremos Description)
            public string description { get; set; }           // opcional (si viene, override del anterior)
            public string date { get; set; }                  // opcional (string tal cual)
        }

        private class ViewsSetScopeBoxReq
        {
            public int[] viewIds { get; set; }
            public string[] viewNames { get; set; }
            public string scopeBoxName { get; set; }          // requerido
            public bool? cropActive { get; set; }             // opcional
        }

        // ---------- helpers ----------

        public static Func<UIApplication, object> SheetsCreate(JObject args)
        {
            var req = args.ToObject<SheetsCreateRequest>() ?? throw new Exception("Invalid args for sheets.create.");
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                FamilySymbol titleblock = null;
                if (!string.IsNullOrWhiteSpace(req.titleBlockType))
                {
                    titleblock = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                        .Where(fs => fs.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_TitleBlocks)
                        .FirstOrDefault(fs => fs.Name.Equals(req.titleBlockType, StringComparison.OrdinalIgnoreCase)
                                           || $"{fs.FamilyName}: {fs.Name}".Equals(req.titleBlockType, StringComparison.OrdinalIgnoreCase));
                }

                int sid;
                using (var t = new Transaction(doc, "MCP: Sheets.Create"))
                {
                    t.Start();
                    if (titleblock != null && !titleblock.IsActive) titleblock.Activate();
                    var sheet = ViewSheet.Create(doc, titleblock?.Id ?? ElementId.InvalidElementId);
                    if (!string.IsNullOrWhiteSpace(req.number)) sheet.SheetNumber = req.number;
                    if (!string.IsNullOrWhiteSpace(req.name)) sheet.Name = req.name;
                    sid = sheet.Id.IntegerValue;
                    t.Commit();
                }
                return new { sheetId = sid };
            };
        }

        private static FamilySymbol FindTitleblock(Document doc, string tokenOrNull)
        {
            if (string.IsNullOrWhiteSpace(tokenOrNull)) return null;

            var q = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(fs => fs.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_TitleBlocks)
                .ToList();

            // "Family: Type" o solo Type
            var tb = q.FirstOrDefault(fs =>
                        fs.Name.Equals(tokenOrNull, StringComparison.OrdinalIgnoreCase) ||
                        ($"{fs.FamilyName}: {fs.Name}").Equals(tokenOrNull, StringComparison.OrdinalIgnoreCase));
            return tb;
        }

        private static ViewSheet FindSheet(Document doc, int id, string nameOrNumber)
        {
            if (id > 0)
            {
                var s = doc.GetElement(new ElementId(id)) as ViewSheet;
                if (s != null) return s;
            }

            if (!string.IsNullOrWhiteSpace(nameOrNumber))
            {
                return new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .FirstOrDefault(s =>
                        s.Name.Equals(nameOrNumber, StringComparison.OrdinalIgnoreCase) ||
                        s.SheetNumber.Equals(nameOrNumber, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private static View FindViewByName(Document doc, string name)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // ---------- acciones ----------

        // sheets.create_bulk
        public static Func<UIApplication, object> SheetsCreateBulk(JObject args)
        {
            var req = args.ToObject<SheetsCreateBulkReq>() ?? new SheetsCreateBulkReq();

            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                var tb = FindTitleblock(doc, req.titleBlockType);
                var tbId = tb?.Id ?? ElementId.InvalidElementId;
                if (tb != null && !tb.IsActive)
                {
                    using (var t0 = new Transaction(doc, "Activate Titleblock"))
                    { t0.Start(); tb.Activate(); t0.Commit(); }
                }

                var created = new List<object>();
                var skipped = new List<object>();
                var failed = new List<object>();

                using (var t = new Transaction(doc, "MCP: Sheets.CreateBulk"))
                {
                    t.Start();
                    foreach (var it in req.items ?? Array.Empty<SheetItem>())
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(it?.number))
                            {
                                failed.Add(new { number = it?.number, name = it?.name, error = "Missing sheet number" });
                                continue;
                            }

                            // Evitar duplicados por SheetNumber
                            var existing = new FilteredElementCollector(doc)
                                .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                                .FirstOrDefault(s => s.SheetNumber.Equals(it.number, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                skipped.Add(new { it.number, it.name, reason = "Already exists" });
                                continue;
                            }

                            var sheet = ViewSheet.Create(doc, tbId);
                            sheet.SheetNumber = it.number;
                            if (!string.IsNullOrWhiteSpace(it.name)) sheet.Name = it.name;

                            created.Add(new { id = sheet.Id.IntegerValue, it.number, it.name });
                        }
                        catch (Exception ex)
                        {
                            failed.Add(new { it?.number, it?.name, error = ex.Message });
                        }
                    }
                    t.Commit();
                }

                return new { created = created.Count, skipped = skipped.Count, failed = failed.Count, items = created, skippedItems = skipped, failedItems = failed };
            };
        }

        // sheets.add_views  (movido desde GraphicsActions)
        public static Func<UIApplication, object> SheetsAddViews(JObject args)
        {
            var req = args.ToObject<SheetsAddViewsReq>() ?? throw new Exception("Invalid args for sheets.add_views.");

            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                var sheet = FindSheet(doc, req.sheetId, req.sheetName);
                if (sheet == null)
                    throw new Exception((req.sheetId > 0) ? $"Sheet {req.sheetId} not found." : $"Sheet '{req.sheetName}' not found.");

                var targetViewIds = new List<ElementId>();
                if (req.viewIds != null && req.viewIds.Length > 0)
                    targetViewIds.AddRange(req.viewIds.Select(i => new ElementId(i)));

                if (req.viewNames != null && req.viewNames.Length > 0)
                {
                    var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                        .Where(v => !v.IsTemplate).ToList();
                    foreach (var vn in req.viewNames)
                    {
                        var v = allViews.FirstOrDefault(x => x.Name.Equals(vn, StringComparison.OrdinalIgnoreCase));
                        if (v != null) targetViewIds.Add(v.Id);
                    }
                }

                if (targetViewIds.Count == 0) throw new Exception("No valid views to place on sheet.");

                var added = new List<int>();
                var skipped = new List<int>();

                using (var t = new Transaction(doc, "MCP: Sheets.AddViews"))
                {
                    t.Start();
                    foreach (var vid in targetViewIds)
                    {
                        var v = doc.GetElement(vid) as View;
                        if (v == null) { skipped.Add(vid.IntegerValue); continue; }

                        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, v.Id)) { skipped.Add(v.Id.IntegerValue); continue; }

                        // Posición base (puedes mejorar auto-layout luego)
                        var pt = new XYZ(0.2, 0.2, 0);
                        Viewport.Create(doc, sheet.Id, v.Id, pt);
                        added.Add(v.Id.IntegerValue);
                    }
                    t.Commit();
                }

                return new { sheetId = sheet.Id.IntegerValue, added, skipped };
            };
        }

        // sheets.assign_revisions
        public static Func<UIApplication, object> SheetsAssignRevisions(JObject args)
        {
            var req = args.ToObject<SheetsAssignRevisionsReq>() ?? throw new Exception("Invalid args for sheets.assign_revisions.");

            if (string.IsNullOrWhiteSpace(req.revisionName) && string.IsNullOrWhiteSpace(req.description))
                throw new Exception("revisionName or description is required.");

            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                // 1) Resolver/crear la revisión (usamos Description como "nombre")
                Revision rev = null;
                var allRevs = new FilteredElementCollector(doc).OfClass(typeof(Revision)).Cast<Revision>().ToList();
                var targetDesc = string.IsNullOrWhiteSpace(req.description) ? req.revisionName : req.description;

                rev = allRevs.FirstOrDefault(r => string.Equals(r.Description, targetDesc, StringComparison.OrdinalIgnoreCase));

                using (var t = new Transaction(doc, "MCP: Sheets.AssignRevisions - Create/Update"))
                {
                    t.Start();

                    if (rev == null)
                    {
                        rev = Revision.Create(doc);
                        rev.Description = targetDesc;
                    }
                    // Setear fecha si viene (tal cual string)
                    if (!string.IsNullOrWhiteSpace(req.date))
                        rev.RevisionDate = req.date;

                    t.Commit();
                }

                // 2) Resolver sheets
                var targets = new List<ViewSheet>();
                if (req.sheetIds != null && req.sheetIds.Length > 0)
                {
                    foreach (var sid in req.sheetIds)
                    {
                        var s = doc.GetElement(new ElementId(sid)) as ViewSheet;
                        if (s != null) targets.Add(s);
                    }
                }
                if (req.sheetNames != null && req.sheetNames.Length > 0)
                {
                    foreach (var token in req.sheetNames)
                    {
                        var s = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                            .FirstOrDefault(x => x.Name.Equals(token, StringComparison.OrdinalIgnoreCase)
                                              || x.SheetNumber.Equals(token, StringComparison.OrdinalIgnoreCase));
                        if (s != null && !targets.Any(x => x.Id.IntegerValue == s.Id.IntegerValue)) targets.Add(s);
                    }
                }
                if (targets.Count == 0) throw new Exception("No target sheets resolved.");

                // 3) Asignar la revisión a cada lámina
                var assigned = new List<int>();
                using (var t = new Transaction(doc, "MCP: Sheets.AssignRevisions"))
                {
                    t.Start();
                    foreach (var s in targets)
                    {
                        // Usamos AdditionalRevisionIds (lista editable por API)
                        var list = s.GetAdditionalRevisionIds()?.ToList() ?? new List<ElementId>();
                        if (!list.Any(x => x.IntegerValue == rev.Id.IntegerValue))
                        {
                            list.Add(rev.Id);
                            s.SetAdditionalRevisionIds(list);
                        }
                        assigned.Add(s.Id.IntegerValue);
                    }
                    t.Commit();
                }

                return new { revisionId = rev.Id.IntegerValue, description = rev.Description, date = rev.RevisionDate, assignedCount = assigned.Count, sheets = assigned };
            };
        }

        // views.set_scope_box
        public static Func<UIApplication, object> ViewsSetScopeBox(JObject args)
        {
            var req = args.ToObject<ViewsSetScopeBoxReq>() ?? throw new Exception("Invalid args for views.set_scope_box.");
            if (string.IsNullOrWhiteSpace(req.scopeBoxName)) throw new Exception("scopeBoxName is required.");

            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");

                // Buscar scope box por nombre (Categoría OST_VolumeOfInterest)
                var scope = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault(e => e.Name.Equals(req.scopeBoxName, StringComparison.OrdinalIgnoreCase));
                if (scope == null) throw new Exception($"Scope box '{req.scopeBoxName}' not found.");

                // Resolver vistas por Id/nombre
                var targets = new List<View>();
                if (req.viewIds != null && req.viewIds.Length > 0)
                {
                    foreach (var vid in req.viewIds)
                    {
                        var v = doc.GetElement(new ElementId(vid)) as View;
                        if (v != null && !v.IsTemplate) targets.Add(v);
                    }
                }
                if (req.viewNames != null && req.viewNames.Length > 0)
                {
                    foreach (var vn in req.viewNames)
                    {
                        var v = FindViewByName(doc, vn);
                        if (v != null && !targets.Any(x => x.Id.IntegerValue == v.Id.IntegerValue)) targets.Add(v);
                    }
                }
                if (targets.Count == 0) throw new Exception("No target views resolved.");

                var updated = new List<int>();

                using (var t = new Transaction(doc, "MCP: Views.SetScopeBox"))
                {
                    t.Start();
                    foreach (var v in targets)
                    {
                        // Parametro BuiltInParameter para scope box (varía por versión, probamos ambos)
                        Parameter p = null;
                        BuiltInParameter bip;

                        // 1) Intento con VIEWER_VOLUME_OF_INTEREST (si existe en esta versión del API)
                        if (Enum.TryParse("VIEWER_VOLUME_OF_INTEREST", true, out bip))
                        {
                            try { p = v.get_Parameter(bip); } catch { /* ignore */ }
                        }

                        // 2) Intento con VIEWER_VOLUME_OF_INTEREST_CROP (algunas builds lo exponen con este nombre)
                        if ((p == null || p.IsReadOnly) && Enum.TryParse("VIEWER_VOLUME_OF_INTEREST_CROP", true, out bip))
                        {
                            try { p = v.get_Parameter(bip); } catch { /* ignore */ }
                        }

                        // 3) Fallback por nombre visible del parámetro (según idioma)
                        if (p == null || p.IsReadOnly)
                        {
                            // Ajusta/añade alias si usas otro idioma
                            var aliases = new[]
                            {
                                "Scope Box",              // EN
                                "Cuadro de referencia",   // ES (Revit ES)
                                "Caixa de escopo",        // PT (ejemplo)
                                "Zone de cadrage"         // FR (ejemplo)
                            };

                            p = v.Parameters
                                 .Cast<Parameter>()
                                 .FirstOrDefault(par =>
                                     par.Definition != null &&
                                     aliases.Any(a => a.Equals(par.Definition.Name, StringComparison.OrdinalIgnoreCase)));
                        }

                        if (p == null || p.IsReadOnly)
                            throw new Exception($"View {v.Name} has no editable scope-box parameter.");

                        p.Set(scope.Id);

                        if (req.cropActive.HasValue)
                            v.CropBoxActive = req.cropActive.Value;

                        updated.Add(v.Id.IntegerValue);
                    }
                    t.Commit();
                }

                return new { updatedCount = updated.Count, views = updated, scopeBox = scope.Name };
            };
        }
    }
}
