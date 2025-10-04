let _cache = null;
export async function snapshotContext(post, opts = {}) {
    const ttl = (opts.cacheSec ?? 30) * 1000;
    const now = Date.now();
    if (_cache && now - _cache.at < _cache.ttl)
        return _cache.value;
    // Llamadas directas al Bridge (ya las tienes implementadas):
    const [activeView, levels, worksets, wallTypes, ductTypes, pipeTypes, families, grids, selection] = await Promise.all([
        post("view.active", {}),
        post("levels.list", {}),
        post("worksets.list", {}).catch(() => ({ items: [] })), // si no hay worksharing
        post("walltypes.list", {}).catch(() => ({ items: [] })),
        post("ducttypes.list", {}).catch(() => ({ items: [] })),
        post("pipetypes.list", {}).catch(() => ({ items: [] })),
        post("families.types.list", {}).catch(() => ({ items: [] })),
        post("grids.list", {}).catch(() => ({ items: [] })), // si aún no existe, añade en Bridge
        post("selection.info", {}).catch(() => ({ items: [] }))
    ]);
    const snapshot = {
        activeView,
        levels,
        worksets,
        wallTypes,
        ductTypes,
        pipeTypes,
        families,
        grids,
        selection,
        units: { internal: "ft", display: "metric" }
    };
    _cache = { at: now, ttl, value: snapshot };
    return snapshot;
}
