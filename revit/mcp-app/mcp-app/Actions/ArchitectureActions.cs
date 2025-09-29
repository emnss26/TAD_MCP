using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Architecture;

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
        // === WallCreate: TU VERSIÓN ROBUSTA (tal cual la que pegaste) ===
        public static Func<UIApplication, object> WallCreate(JObject args)
        {
            var req = args.ToObject<CreateWallRequest>();
            if (req == null) throw new Exception("Invalid args for wall.create.");

            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;

                Level level = ViewHelpers.ResolveLevel(doc, req.level, uidoc.ActiveView);

                WallType wtype;
                if (!string.IsNullOrWhiteSpace(req.wallType))
                {
                    wtype = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                        .FirstOrDefault(t => t.Name.Equals(req.wallType, StringComparison.OrdinalIgnoreCase) ||
                                             $"{t.FamilyName}: {t.Name}".Equals(req.wallType, StringComparison.OrdinalIgnoreCase))
                        ?? throw new Exception($"WallType '{req.wallType}' not found.");
                }
                else
                {
                    var candidates = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                        .Where(t => t.Kind != WallKind.Curtain).ToList();
                    wtype = candidates
                        .OrderByDescending(t => t.Name.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) >= 0)
                        .ThenBy(t => t.Name).FirstOrDefault()
                        ?? throw new Exception("No suitable WallType found.");
                }

                var h = (req.height_m > 0) ? req.height_m : 3.0;

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
                    if (hParam == null || hParam.IsReadOnly) throw new Exception("Cannot set user wall height.");
                    hParam.Set(ToFt(h));
                    id = wall.Id.IntegerValue;
                    t.Commit();
                }

                return new CreateWallResponse
                {
                    elementId = id,
                    used = new { level = level.Name, wallType = $"{wtype.FamilyName}: {wtype.Name}", height_m = h }
                };
            };
        }

        public static Func<UIApplication, object> LevelCreate(JObject args)
        {
            var req = args.ToObject<LevelCreateRequest>() ?? throw new Exception("Invalid args for level.create.");
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                double elevFt = Core.Units.MetersToFt(req.elevation_m);
                int id;
                using (var t = new Transaction(doc, "MCP: Level.Create"))
                {
                    t.Start();
                    var lvl = Level.Create(doc, elevFt);
                    if (!string.IsNullOrWhiteSpace(req.name)) lvl.Name = req.name;
                    id = lvl.Id.IntegerValue;
                    t.Commit();
                }
                return new { elementId = id };
            };
        }

        public static Func<UIApplication, object> GridCreate(JObject args)
        {
            var req = args.ToObject<GridCreateRequest>() ?? throw new Exception("Invalid args for grid.create.");
            return (UIApplication app) =>
            {
                var doc = app.ActiveUIDocument?.Document ?? throw new Exception("No active document.");
                double ToFt(double m) => Core.Units.MetersToFt(m);
                var p1 = new XYZ(ToFt(req.start.x), ToFt(req.start.y), 0);
                var p2 = new XYZ(ToFt(req.end.x), ToFt(req.end.y), 0);
                int id;
                using (var t = new Transaction(doc, "MCP: Grid.Create"))
                {
                    t.Start();
                    var grid = Grid.Create(doc, Line.CreateBound(p1, p2));
                    if (!string.IsNullOrWhiteSpace(req.name)) grid.Name = req.name;
                    id = grid.Id.IntegerValue;
                    t.Commit();
                }
                return new { elementId = id };
            };
        }

        // Piso por contorno (cerrado, coplanar). Simplificado (sin huecos).
        public static Func<UIApplication, object> FloorCreate(JObject args)
        {
            var req = args.ToObject<FloorCreateRequest>() ?? throw new Exception("Invalid args for floor.create.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;

                var level = ViewHelpers.ResolveLevel(doc, req.level, uidoc.ActiveView);

                // Tipo de piso (opcional con fallback)
                FloorType ftype = null;
                if (!string.IsNullOrWhiteSpace(req.floorType))
                {
                    ftype = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                        .FirstOrDefault(t => t.Name.Equals(req.floorType, StringComparison.OrdinalIgnoreCase) ||
                                             $"{t.FamilyName}: {t.Name}".Equals(req.floorType, StringComparison.OrdinalIgnoreCase));
                    if (ftype == null) throw new Exception($"FloorType '{req.floorType}' not found.");
                }
                else
                {
                    ftype = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault()
                         ?? throw new Exception("No FloorType found.");
                }

                if (req.profile == null || req.profile.Length < 3)
                    throw new Exception("Floor profile requires at least 3 points.");

                double ToFt(double m) => Core.Units.MetersToFt(m);

                // Arma un Loop cerrado
                var loop = new CurveLoop();
                for (int i = 0; i < req.profile.Length; i++)
                {
                    var a = req.profile[i];
                    var b = req.profile[(i + 1) % req.profile.Length];
                    loop.Append(Line.CreateBound(
                        new XYZ(ToFt(a.x), ToFt(a.y), level.Elevation),  
                        new XYZ(ToFt(b.x), ToFt(b.y), level.Elevation)   
                    ));
                }

                int id;
                using (var t = new Transaction(doc, "MCP: Floor.Create"))
                {
                    t.Start();
                    var fl = Floor.Create(doc, new System.Collections.Generic.List<CurveLoop> { loop }, ftype.Id, level.Id);
                    id = fl.Id.IntegerValue;
                    t.Commit();
                }

                return new { elementId = id, used = new { level = level.Name, floorType = ftype.Name } };
            };
        }

        public static Func<UIApplication, object> CeilingCreate(JObject args)
        {
            // API de techo por perfil es más limitada; muchas veces requiere Sketched Ceiling (CeilingType + perfil en plan).
            // Devolvemos NotImplemented para evitar falsas expectativas.
            return (UIApplication app) =>
            {
                throw new Exception("ceiling.create not implemented yet (sketch ceilings require advanced handling).");
            };
        }

        // DOOR / WINDOW: requieren muro host explícito
        public static Func<UIApplication, object> DoorPlace(JObject args)
        {
            var req = args.ToObject<DoorPlaceRequest>() ?? throw new Exception("Invalid args for door.place.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;

                // Resolver nivel (opcional → vista activa o primer nivel)
                var level = ViewHelpers.ResolveLevel(doc, req.level, uidoc.ActiveView);

                // Resolver símbolo (opcional → primero disponible) + mensaje con ejemplos si no existe
                var sym = ResolveFamilySymbol(doc, BuiltInCategory.OST_Doors, req.familySymbol, "Door type not found.");

                // Resolver muro host (por id, por selección, por punto cercano, o primero del modelo)
                Pt2 pReq = req.point;
                double ToFt(double m) => Core.Units.MetersToFt(m);
                var pHint = (pReq != null) ? new XYZ(ToFt(pReq.x), ToFt(pReq.y), 0) : null;
                var host = ResolveHostWall(uidoc, req.hostWallId, pHint);

                // Resolver punto de inserción si no vino "point"
                var p = ResolveInsertionPoint(host, req.point, req.offsetAlong_m, req.alongNormalized);
                // Asegura Z=0 (Revit lo proyecta al host con el nivel)
                p = new XYZ(p.X, p.Y, 0);

                int id;
                using (var t = new Transaction(doc, "MCP: Door.Place"))
                {
                    t.Start();
                    if (!sym.IsActive) sym.Activate();

                    var inst = doc.Create.NewFamilyInstance(p, sym, host, level, StructuralType.NonStructural);

                    if (Math.Abs(req.offset_m) > 1e-9)
                        inst.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.Set(ToFt(req.offset_m));

                    if (req.flipHand) inst.flipHand();
                    if (req.flipFacing) inst.flipFacing();

                    id = inst.Id.IntegerValue;
                    t.Commit();
                }
                return new { elementId = id, used = new { level = level.Name, hostId = host.Id.IntegerValue, symbol = $"{sym.FamilyName}: {sym.Name}" } };
            };
        }

        public static Func<UIApplication, object> WindowPlace(JObject args)
        {
            var req = args.ToObject<WindowPlaceRequest>() ?? throw new Exception("Invalid args for window.place.");
            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;

                var level = ViewHelpers.ResolveLevel(doc, req.level, uidoc.ActiveView);
                var sym = ResolveFamilySymbol(doc, BuiltInCategory.OST_Windows, req.familySymbol, "Window type not found.");

                Pt2 pReq = req.point;
                double ToFt(double m) => Core.Units.MetersToFt(m);
                var pHint = (pReq != null) ? new XYZ(ToFt(pReq.x), ToFt(pReq.y), 0) : null;
                var host = ResolveHostWall(uidoc, req.hostWallId, pHint);

                var p = ResolveInsertionPoint(host, req.point, req.offsetAlong_m, req.alongNormalized);
                p = new XYZ(p.X, p.Y, 0);

                int id;
                using (var t = new Transaction(doc, "MCP: Window.Place"))
                {
                    t.Start();
                    if (!sym.IsActive) sym.Activate();
                    var inst = doc.Create.NewFamilyInstance(p, sym, host, level, StructuralType.NonStructural);

                    if (Math.Abs(req.offset_m) > 1e-9)
                        inst.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.Set(ToFt(req.offset_m));

                    if (req.flipHand) inst.flipHand();
                    if (req.flipFacing) inst.flipFacing();

                    id = inst.Id.IntegerValue;
                    t.Commit();
                }
                return new { elementId = id, used = new { level = level.Name, hostId = host.Id.IntegerValue, symbol = $"{sym.FamilyName}: {sym.Name}" } };
            };
        }

        private static FamilySymbol ResolveFamilySymbol(Document doc, BuiltInCategory bic, string tokenOrNull, string notFoundMsg)
        {
            var q = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Where(fs => fs.Category != null && fs.Category.Id.IntegerValue == (int)bic).ToList();

            if (!string.IsNullOrWhiteSpace(tokenOrNull))
            {
                var sym = q.FirstOrDefault(fs =>
                             fs.Name.Equals(tokenOrNull, StringComparison.OrdinalIgnoreCase) ||
                             ($"{fs.FamilyName}: {fs.Name}").Equals(tokenOrNull, StringComparison.OrdinalIgnoreCase));
                if (sym != null) return sym;

                // Mensaje con ejemplos para el usuario (primeros 10)
                var sample = string.Join(", ", q.Take(10).Select(fs => $"{fs.FamilyName}: {fs.Name}"));
                throw new Exception($"{notFoundMsg} Ejemplos: {sample}");
            }

            var first = q.FirstOrDefault();
            if (first == null) throw new Exception($"{notFoundMsg} (no hay tipos cargados).");
            return first;
        }

        private static Wall ResolveHostWall(UIDocument uidoc, int? hostWallId, XYZ pointOrNull, double tolFt = 2.0)
        {
            var doc = uidoc.Document;

            // 1) Por Id explícito
            if (hostWallId.HasValue && hostWallId.Value > 0)
            {
                var w = doc.GetElement(new ElementId(hostWallId.Value)) as Wall;
                if (w != null) return w;
                throw new Exception($"Host wall {hostWallId.Value} not found.");
            }

            // 2) Si hay UNA selección y es muro, usarla
            var sel = uidoc.Selection.GetElementIds();
            if (sel != null && sel.Count == 1)
            {
                var w = doc.GetElement(sel.First()) as Wall;
                if (w != null) return w;
            }

            // 3) Si hay punto, buscar muro más cercano a su LocationCurve
            if (pointOrNull != null)
            {
                Wall best = null;
                double bestDist = double.MaxValue;

                foreach (var w in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>())
                {
                    var lc = w.Location as LocationCurve;
                    if (lc == null) continue;
                    var c = lc.Curve;
                    var proj = c.Project(pointOrNull);
                    if (proj == null) continue;
                    // Asegurar que cae dentro del segmento
                    var onSeg = proj.Parameter >= c.GetEndParameter(0) - 1e-9 && proj.Parameter <= c.GetEndParameter(1) + 1e-9;
                    if (!onSeg) continue;

                    var d = proj.Distance;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = w;
                    }
                }

                if (best != null && bestDist <= tolFt) return best;
            }

            // 4) Último recurso: primer muro del modelo (peligroso, pero útil para “solo colócala”)
            var firstWall = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().FirstOrDefault();
            if (firstWall == null) throw new Exception("No walls found in the model.");
            return firstWall;
        }

        private static XYZ ResolveInsertionPoint(Wall host, Pt2 pt2, double? offsetAlong_m, double? alongNormalized)
        {
            // Si el usuario dio punto XY, úsalo
            if (pt2 != null)
            {
                double ToFt(double m) => Core.Units.MetersToFt(m);
                return new XYZ(ToFt(pt2.x), ToFt(pt2.y), 0);
            }

            // Si no hay punto, podemos calcular a lo largo del LocationCurve
            var lc = host.Location as LocationCurve;
            if (lc == null) throw new Exception("Host wall has no LocationCurve.");

            var c = lc.Curve;
            double t;

            if (alongNormalized.HasValue)
            {
                // clamp
                t = Math.Max(0.0, Math.Min(1.0, alongNormalized.Value));
                return c.Evaluate(t, true);
            }

            if (offsetAlong_m.HasValue)
            {
                var L = c.Length;
                var off = Core.Units.MetersToFt(offsetAlong_m.Value);
                t = (L > 1e-9) ? Math.Max(0.0, Math.Min(1.0, off / L)) : 0.5;
                return c.Evaluate(t, true);
            }

            // Si no se dio nada, usar el centro del muro
            return c.Evaluate(0.5, true);
        }

        public static Func<UIApplication, object> RoomsCreateOnLevels(JObject args)
        {
            var req = args.ToObject<RoomsCreateOnLevelsRequest>() ?? new RoomsCreateOnLevelsRequest();

            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;

                var allLvls = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
                var levels = (req.levelNames != null && req.levelNames.Length > 0)
                    ? allLvls.Where(l => req.levelNames.Any(n => n.Equals(l.Name, StringComparison.OrdinalIgnoreCase))).ToList()
                    : allLvls.OrderBy(l => l.Elevation).ToList();

                if (levels.Count == 0) throw new Exception("No target levels resolved.");

                var created = new List<int>();
                var failed = new List<object>();

                using (var t = new Transaction(doc, "MCP: Rooms.CreateOnLevels"))
                {
                    t.Start();
                    foreach (var lvl in levels)
                    {
                        try
                        {
                            bool placedAny = false;

                            // Intento 1: usar NewRooms2(Level) por reflexión (auto-colocar en TODOS los recintos cerrados)
                            if (req.placeOnlyEnclosed.GetValueOrDefault(true))
                            {
                                var creation = doc.Create;
                                var mi = creation.GetType().GetMethod("NewRooms2", new Type[] { typeof(Level) });
                                if (mi != null)
                                {
                                    var result = mi.Invoke(creation, new object[] { lvl }) as System.Collections.IEnumerable;
                                    if (result != null)
                                    {
                                        foreach (var o in result)
                                        {
                                            // soporta IList<ElementId> sin referenciar tipo explícito
                                            var prop = o?.GetType().GetProperty("IntegerValue");
                                            if (prop != null)
                                            {
                                                int id = (int)prop.GetValue(o);
                                                created.Add(id);
                                                placedAny = true;
                                            }
                                            else if (o is ElementId eid)
                                            {
                                                created.Add(eid.IntegerValue);
                                                placedAny = true;
                                            }
                                        }
                                    }
                                }
                            }

                            // Fallback: si no hay NewRooms2 o no creó nada, colocar una habitación por punto (si cae en un recinto)
                            if (!placedAny)
                            {
                                // UV(0,0) es tentativa; el usuario puede mover/duplicar después.
                                var room = doc.Create.NewRoom(lvl, new UV(0, 0));
                                if (room != null) { created.Add(room.Id.IntegerValue); placedAny = true; }
                            }
                        }
                        catch (Exception ex)
                        {
                            failed.Add(new { level = lvl.Name, error = ex.Message });
                        }
                    }
                    t.Commit();
                }

                return new { created = created.Count, rooms = created, failed };
            };
        }

        public static Func<UIApplication, object> FloorsFromRooms(JObject args)
        {
            var req = args.ToObject<FloorsFromRoomsRequest>() ?? new FloorsFromRoomsRequest();

            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;

                if (req.roomIds == null || req.roomIds.Length == 0) throw new Exception("floors.from_rooms requires roomIds.");

                // Resolver FloorType
                FloorType ftype = null;
                var ftypes = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().ToList();
                if (!string.IsNullOrWhiteSpace(req.floorType))
                {
                    ftype = ftypes.FirstOrDefault(t =>
                        t.Name.Equals(req.floorType, StringComparison.OrdinalIgnoreCase) ||
                        $"{t.FamilyName}: {t.Name}".Equals(req.floorType, StringComparison.OrdinalIgnoreCase));
                    if (ftype == null) throw new Exception($"FloorType '{req.floorType}' not found.");
                }
                else
                {
                    ftype = ftypes.FirstOrDefault() ?? throw new Exception("No FloorType found.");
                }

                double offsetFt = Core.Units.MetersToFt(req.baseOffset_m.GetValueOrDefault(0.0));

                var created = new List<object>();
                var skipped = new List<object>();

                var opts = new SpatialElementBoundaryOptions
                {
                    SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
                };

                using (var t = new Transaction(doc, "MCP: Floors.FromRooms"))
                {
                    t.Start();
                    foreach (var rid in req.roomIds)
                    {
                        try
                        {
                            var room = doc.GetElement(new ElementId(rid)) as Room;
                            if (room == null || room.Location == null || room.Area <= 1e-6)
                            {
                                skipped.Add(new { roomId = rid, reason = "Room not found or not enclosed." });
                                continue;
                            }

                            var level = doc.GetElement(room.LevelId) as Level;
                            if (level == null) { skipped.Add(new { roomId = rid, reason = "Room has no valid level." }); continue; }

                            var loopsRaw = room.GetBoundarySegments(opts);
                            if (loopsRaw == null || loopsRaw.Count == 0)
                            {
                                skipped.Add(new { roomId = rid, reason = "No boundary segments." });
                                continue;
                            }

                            // Construye CurveLoops trasladando a Z del nivel + offset (conserva líneas/arcos originales)
                            var loops = new List<CurveLoop>();
                            foreach (var segLoop in loopsRaw)
                            {
                                var cl = new CurveLoop();
                                foreach (var seg in segLoop)
                                {
                                    var c = seg.GetCurve();
                                    var zTarget = level.Elevation + offsetFt;
                                    var dz = zTarget - c.GetEndPoint(0).Z;
                                    var c2 = c.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, dz)));
                                    cl.Append(c2);
                                }
                                loops.Add(cl);
                            }

                            var fl = Floor.Create(doc, loops, ftype.Id, level.Id);
                            created.Add(new { roomId = rid, floorId = fl.Id.IntegerValue });
                        }
                        catch (Exception ex)
                        {
                            skipped.Add(new { roomId = rid, error = ex.Message });
                        }
                    }
                    t.Commit();
                }

                return new { created = created.Count, items = created, skipped = skipped.Count, skippedItems = skipped };
            };
        }

        public static Func<UIApplication, object> CeilingsFromRooms(JObject args)
        {
            return (UIApplication app) =>
            {
                throw new Exception("ceilings.from_rooms not implemented yet (sketch ceilings require advanced handling).");
            };
        }

        public static Func<UIApplication, object> RoofFootprintCreate(JObject args)
        {
            var req = args.ToObject<RoofFootprintCreateRequest>() ?? throw new Exception("Invalid args for roof.create_footprint.");
            if (req.profile == null || req.profile.Length < 3) throw new Exception("Roof footprint requires at least 3 points.");
            if (string.IsNullOrWhiteSpace(req.level)) throw new Exception("Level is required.");

            return (UIApplication app) =>
            {
                var uidoc = app.ActiveUIDocument ?? throw new Exception("No active document.");
                var doc = uidoc.Document;

                // Nivel
                var level = ViewHelpers.ResolveLevel(doc, req.level, uidoc.ActiveView);

                // RoofType
                RoofType rtype = null;
                var rtypes = new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>().ToList();
                if (!string.IsNullOrWhiteSpace(req.roofType))
                {
                    rtype = rtypes.FirstOrDefault(rt =>
                        rt.Name.Equals(req.roofType, StringComparison.OrdinalIgnoreCase) ||
                        $"{rt.FamilyName}: {rt.Name}".Equals(req.roofType, StringComparison.OrdinalIgnoreCase));
                    if (rtype == null) throw new Exception($"RoofType '{req.roofType}' not found.");
                }
                else
                {
                    rtype = rtypes.FirstOrDefault() ?? throw new Exception("No RoofType found.");
                }

                // Perfil cerrado
                double ToFt(double m) => Core.Units.MetersToFt(m);
                var ca = new CurveArray();
                for (int i = 0; i < req.profile.Length; i++)
                {
                    var a = req.profile[i];
                    var b = req.profile[(i + 1) % req.profile.Length];
                    var p1 = new XYZ(ToFt(a.x), ToFt(a.y), level.Elevation);
                    var p2 = new XYZ(ToFt(b.x), ToFt(b.y), level.Elevation);
                    ca.Append(Line.CreateBound(p1, p2));
                }

                int id;
                using (var t = new Transaction(doc, "MCP: Roof.CreateFootprint"))
                {
                    t.Start();
                    ModelCurveArray footprintCurves;
                    var roof = doc.Create.NewFootPrintRoof(ca, level, rtype, out footprintCurves);

                    // Inclinación (grados → radianes), si viene
                    if (req.slope.HasValue && req.slope.Value > 0)
                    {
                        double rad = req.slope.Value * Math.PI / 180.0;
                        foreach (ModelCurve mc in footprintCurves)
                        {
                            roof.set_DefinesSlope(mc, true);
                            roof.set_SlopeAngle(mc, rad);
                        }
                    }

                    id = roof.Id.IntegerValue;
                    t.Commit();
                }

                return new { elementId = id, used = new { level = level.Name, roofType = $"{rtype.FamilyName}: {rtype.Name}", slope_deg = req.slope } };
            };
        }

    }
}

