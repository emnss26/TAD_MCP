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
    internal class ParamsActions
    {
        // ===== Models =====
        private class GetReq
        {
            public int[] elementIds { get; set; } = Array.Empty<int>();
            public string[] paramNames { get; set; } = Array.Empty<string>();
            public bool includeValueString { get; set; } = true;
        }

        private class UpdateItem
        {
            public int elementId { get; set; }
            public string param { get; set; }      // nombre visible (e.g. "Mark") o BuiltInParameter (e.g. "ALL_MODEL_MARK")
            public JToken value { get; set; }      // string | number | boolean
        }

        private class SetReq
        {
            public UpdateItem[] updates { get; set; } = Array.Empty<UpdateItem>();
        }

        private class BulkReq
        {
            // NOTA: el server TS procesará CSV/XLSX y terminará mandando updates[].
            public UpdateItem[] updates { get; set; } = Array.Empty<UpdateItem>();

            // Estos campos pueden venir en la petición original, pero aquí no se usan.
            public string tableCsv { get; set; }
            public string tableXlsxPath { get; set; }
            public string idColumn { get; set; }
            public string paramColumn { get; set; }
            public string valueColumn { get; set; }
        }

        // ===== Public Actions =====

        // params.get
        public static Func<UIApplication, object> ParamsGet(JObject args)
        {
            var req = args.ToObject<GetReq>() ?? new GetReq();

            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var rows = new List<object>();

                foreach (var eid in req.elementIds ?? Array.Empty<int>())
                {
                    var el = doc.GetElement(new ElementId(eid));
                    if (el == null)
                    {
                        foreach (var pname in req.paramNames ?? Array.Empty<string>())
                            rows.Add(new { elementId = eid, param = pname, error = "Element not found" });
                        continue;
                    }

                    foreach (var pname in req.paramNames ?? Array.Empty<string>())
                    {
                        var p = FindParam(el, pname, out Element targetEl, out string source);
                        if (p == null)
                        {
                            rows.Add(new { elementId = eid, param = pname, error = "Parameter not found" });
                            continue;
                        }

                        rows.Add(ParamRow(p, eid, pname, source, req.includeValueString));
                    }
                }

                return new { count = rows.Count, items = rows };
            };
        }

        // params.set
        public static Func<UIApplication, object> ParamsSet(JObject args)
        {
            var req = args.ToObject<SetReq>() ?? new SetReq();

            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                var results = new List<object>();
                int ok = 0, failed = 0;

                using (var t = new Transaction(doc, "MCP: Params.Set"))
                {
                    t.Start();

                    foreach (var u in req.updates ?? Array.Empty<UpdateItem>())
                    {
                        try
                        {
                            var el = doc.GetElement(new ElementId(u.elementId));
                            if (el == null) { failed++; results.Add(new { u.elementId, u.param, ok = false, error = "Element not found" }); continue; }

                            var p = FindParam(el, u.param, out Element targetEl, out string source);
                            if (p == null) { failed++; results.Add(new { u.elementId, u.param, ok = false, error = "Parameter not found" }); continue; }
                            if (p.IsReadOnly) { failed++; results.Add(new { u.elementId, u.param, ok = false, error = "Parameter is read-only" }); continue; }

                            // Set value
                            ApplyParamValue(p, u.value);

                            ok++;
                            results.Add(new { u.elementId, u.param, ok = true, target = source });
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            results.Add(new { u.elementId, u.param, ok = false, error = ex.Message });
                        }
                    }

                    t.Commit();
                }

                return new { updated = ok, failed, results };
            };
        }

        // params.bulk_from_table
        // En este bridge C# NO parseamos CSV/XLSX. Esperamos que el server TS nos mande updates[] ya construido.
        public static Func<UIApplication, object> ParamsBulkFromTable(JObject args)
        {
            var req = args.ToObject<BulkReq>() ?? new BulkReq();

            // Si trae updates[], delegamos a la misma lógica de ParamsSet.
            if (req.updates != null && req.updates.Length > 0)
            {
                var pass = new JObject
                {
                    ["updates"] = JArray.FromObject(req.updates)
                };
                return ParamsSet(pass);
            }

            // Si no trae updates[], avisamos que el server TS debe convertir CSV/XLSX a updates[].
            return (UIApplication _) =>
                new
                {
                    ok = false,
                    message = "params.bulk_from_table expects 'updates[]'. The TS server should parse CSV/XLSX and send updates[]."
                };
        }

        // ===== Helpers =====

        private static object ParamRow(Parameter p, int elementId, string paramToken, string source, bool includeValueString)
        {
            object raw = null;
            string storage = p.StorageType.ToString();
            string valueString = null;

            try
            {
                switch (p.StorageType)
                {
                    case StorageType.Integer:
                        raw = p.AsInteger();
                        break;
                    case StorageType.Double:
                        raw = p.AsDouble();
                        break;
                    case StorageType.String:
                        raw = p.AsString();
                        break;
                    case StorageType.ElementId:
                        raw = p.AsElementId()?.IntegerValue;
                        break;
                }
            }
            catch { /* ignore */ }

            if (includeValueString)
            {
                try { valueString = p.AsValueString(); } catch { }
            }

            return new
            {
                elementId,
                param = paramToken,
                storageType = storage,
                value = raw,
                valueString,
                source
            };
        }

        private static void ApplyParamValue(Parameter p, JToken tok)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));

            switch (p.StorageType)
            {
                case StorageType.String:
                    p.Set(tok?.Type == JTokenType.Null ? null : tok.ToString());
                    break;

                case StorageType.Integer:
                    if (tok?.Type == JTokenType.Boolean)
                    {
                        p.Set(tok.Value<bool>() ? 1 : 0); // Yes/No params
                    }
                    else
                    {
                        p.Set(tok?.Type == JTokenType.Null ? 0 : tok.Value<int>());
                    }
                    break;

                case StorageType.Double:
                    // Preferimos SetValueString si viene como string (respeta unidades del proyecto)
                    if (tok?.Type == JTokenType.String)
                    {
                        var s = tok.Value<string>();
                        bool done = false;
                        try { p.SetValueString(s); done = true; } catch { /* fall back */ }
                        if (!done)
                        {
                            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                                p.Set(d);
                            else
                                throw new Exception($"Cannot parse numeric value '{s}'.");
                        }
                    }
                    else
                    {
                        // numérico directo (se asume en unidades internas si aplica)
                        p.Set(tok?.Type == JTokenType.Null ? 0.0 : tok.Value<double>());
                    }
                    break;

                case StorageType.ElementId:
                    if (tok?.Type == JTokenType.Null) { p.Set(ElementId.InvalidElementId); }
                    else
                    {
                        var id = new ElementId(tok.Value<int>());
                        p.Set(id);
                    }
                    break;

                default:
                    throw new Exception($"Unsupported storage type: {p.StorageType}");
            }
        }

        private static Parameter FindParam(Element el, string token, out Element targetElement, out string source)
        {
            targetElement = el; source = "instance";

            if (string.IsNullOrWhiteSpace(token)) return null;

            // 1) BuiltInParameter por nombre (e.g., "ALL_MODEL_MARK")
            if (Enum.TryParse<BuiltInParameter>(token, true, out var bip))
            {
                var pBuiltIn = el.get_Parameter(bip);
                if (pBuiltIn != null) return pBuiltIn;

                // Buscar también en el tipo si no está en la instancia
                var typId = el.GetTypeId();
                if (typId != ElementId.InvalidElementId)
                {
                    var typ = el.Document.GetElement(typId);
                    var pT = (typ as Element)?.get_Parameter(bip);
                    if (pT != null) { targetElement = typ; source = "type"; return pT; }
                }
            }

            // 2) Nombre visible (instance)
            var pByName = el.Parameters
                .Cast<Parameter>()
                .FirstOrDefault(p => string.Equals(p.Definition?.Name, token, StringComparison.OrdinalIgnoreCase));
            if (pByName != null) return pByName;

            // 3) Nombre visible (type)
            var typeId = el.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                var typEl = el.Document.GetElement(typeId);
                var pTypeByName = typEl?.Parameters
                    .Cast<Parameter>()
                    .FirstOrDefault(p => string.Equals(p.Definition?.Name, token, StringComparison.OrdinalIgnoreCase));
                if (pTypeByName != null) { targetElement = typEl; source = "type"; return pTypeByName; }
            }

            return null;
        }
    }
}
