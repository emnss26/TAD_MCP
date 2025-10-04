import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";
// ⬇️ Usa relativas por ahora (funciona en dev con tsx y al compilar si apuntas a dist)
// Si prefieres imports por paquete más adelante, cambia a:
//   import { snapshotContext } from "mcp_common/context";
//   import { orchestratePlan } from "mcp_plans/plan";
import { snapshotContext } from "../../mcp_common/src/context.js";
import { orchestratePlan } from "../../mcp_common/src/plan.js";
const server = new McpServer({ name: "mcp-orchestrator", version: "1.0.0" });
const asText = (obj) => ({
    content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
});
// Traducción de nombres de tool → acción del bridge (Revit)
const toolToAction = {
    // Arquitectura
    arch_wall_create: "wall.create",
    arch_floor_create: "floor.create",
    arch_roof_create: "roof.create_footprint",
    arch_door_place: "door.place",
    arch_window_place: "window.place",
    // Estructura (acepto ambos prefijos para no fallar)
    str_beam_create: "struct.beam.create",
    struct_beam_create: "struct.beam.create",
    str_column_create: "struct.column.create",
    struct_column_create: "struct.column.create",
    struct_floor_create: "struct.floor.create",
    // MEP
    mep_pipe_create: "mep.pipe.create",
    mep_duct_create: "mep.duct.create",
    mep_conduit_create: "mep.conduit.create",
    mep_cabletray_create: "mep.cabletray.create",
    // Snapshot especial (lo resolvemos localmente)
    query_context_snapshot: "__local_context__",
};
// Runner mínimo: ejecuta secuencialmente los pasos
async function runSteps(steps, opts) {
    const out = [];
    for (let i = 0; i < steps.length; i++) {
        const s = steps[i] || {};
        const key = s.tool ?? s.action ?? "";
        const action = toolToAction[key] ?? (key.includes(".") ? key : undefined); // permite "domain.action" directo
        if (!action) {
            out.push({
                index: i,
                ok: false,
                action: key || "(empty)",
                error: "Unknown step/tool/action",
            });
            if (!opts?.continueOnError)
                break;
            continue;
        }
        if (opts?.dryRun) {
            out.push({ index: i, ok: true, action, args: s.args, data: { dryRun: true } });
            continue;
        }
        try {
            if (action === "__local_context__") {
                const cacheSec = typeof s.args?.cacheSec === "number" ? s.args.cacheSec : 30;
                const ctx = await snapshotContext(postRevit, { cacheSec });
                out.push({ index: i, ok: true, action: key, args: s.args, data: ctx });
            }
            else {
                const res = await postRevit(action, s.args ?? {});
                out.push({ index: i, ok: true, action, args: s.args, data: res });
            }
        }
        catch (e) {
            out.push({
                index: i,
                ok: false,
                action,
                args: s.args,
                error: String(e?.message ?? e),
            });
            if (!opts?.continueOnError)
                break;
        }
    }
    return {
        ok: out.every((r) => r.ok !== false),
        steps: out,
    };
}
/* =========================
 * Tool: orchestrate_plan
 * (genera los pasos a partir de intent+args)
 * ========================= */
const OrchestrateShape = {
    intent: z.enum([
        "create_wall",
        "place_door",
        "place_window",
        "create_floor",
        "create_roof",
        "create_beam",
        "create_column",
        "create_pipe",
        "create_duct",
    ]),
    args: z.record(z.any()).optional(),
};
const OrchestrateSchema = z.object(OrchestrateShape);
server.registerTool("orchestrate_plan", {
    title: "Orquestar plan",
    description: "Devuelve una secuencia de pasos (incluye un snapshot de contexto) para un intent dado.",
    inputSchema: OrchestrateShape,
}, async (raw) => {
    const input = OrchestrateSchema.parse(raw);
    const steps = orchestratePlan(input); // tu función de mcp_plans
    return asText({ plan: steps });
});
/* =========================
 * Tool: macro_run
 * (ejecuta pasos o bien compila intent→pasos y ejecuta)
 * ========================= */
const MacroRunBySteps = z.object({
    steps: z.array(z.object({
        tool: z.string().optional(),
        action: z.string().optional(),
        args: z.any().optional(),
        why: z.string().optional(),
    })),
    dryRun: z.boolean().optional(),
    continueOnError: z.boolean().optional(),
});
const MacroRunByIntent = z.object({
    intent: OrchestrateSchema.shape.intent,
    args: OrchestrateSchema.shape.args,
    dryRun: z.boolean().optional(),
    continueOnError: z.boolean().optional(),
});
const MacroRunSchema = z.union([MacroRunBySteps, MacroRunByIntent]);
server.registerTool("macro_run", {
    title: "Run Macro Plan",
    description: "Ejecuta un plan: pásame steps[] o bien intent+args para que yo derive y ejecute.",
    // Nota: el SDK actual acepta un 'shape' estilo ZodRawShape también; uso union por claridad.
}, async (raw) => {
    const parsed = MacroRunSchema.parse(raw);
    let steps;
    if ("steps" in parsed) {
        steps = parsed.steps;
    }
    else {
        steps = orchestratePlan({ intent: parsed.intent, args: parsed.args });
    }
    // Consejo: agrega el snapshot como primer paso si no está
    const hasSnap = steps.some((s) => (s.tool ?? s.action) === "query_context_snapshot");
    if (!hasSnap) {
        steps = [{ tool: "query_context_snapshot", args: { cacheSec: 30 }, why: "Contexto" }, ...steps];
    }
    const result = await runSteps(steps, {
        dryRun: parsed.dryRun,
        continueOnError: parsed.continueOnError,
    });
    return asText(result);
});
// stdio
const transport = new StdioServerTransport();
await server.connect(transport);
