import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";
import { snapshotContext } from "../../mcp_common/src/context.js";
import { runPlan } from "../../mcp_common/src/plan.js";
const server = new McpServer({ name: "mcp-orchestrator", version: "1.0.0" });
const asText = (obj) => ({ content: [{ type: "text", text: JSON.stringify(obj, null, 2) }] });
// Tool 1: macro.run (plan genérico)
const MacroRunShape = {
    vars: z.record(z.any()).optional(),
    steps: z.array(z.any()),
    dryRun: z.boolean().optional(),
    continueOnError: z.boolean().optional()
};
server.registerTool("macro_run", { title: "Run Macro Plan", description: "Ejecuta un plan de pasos con foreach/template/requires.", inputSchema: MacroRunShape }, async (args) => {
    // snapshot opcional por si lo usan dentro del plan
    const ctx = await snapshotContext(postRevit, { cacheSec: 30 });
    const result = await runPlan(postRevit, { ...args, vars: { ...args.vars, context: ctx } });
    return asText(result);
});
// Tool 2: macro.nave_build_basic (macro de alto nivel – stub)
const NaveShape = {
    width_m: z.number(),
    length_m: z.number(),
    baysX: z.number(),
    baysY: z.number(),
    eaveMin_m: z.number(),
    ridgeMax_m: z.number(),
    columnType: z.string(),
    beamType: z.string(),
    wallType: z.string(),
    roofType: z.string(),
    egressDoorType: z.string(),
    egressEveryXBays: z.number().default(3)
};
server.registerTool("macro_nave_build_basic", { title: "Build Basic Nave", description: "Crea ejes, columnas, vigas con pendiente, muros, roof y planos.", inputSchema: NaveShape }, async (args) => {
    const a = args;
    // 1) Snapshot (resuelve niveles/tipos)
    const ctx = await snapshotContext(postRevit, { cacheSec: 30 });
    // 2) Derivar ejes simple (ejemplo; puedes mejorar con cálculos desde ctx.grids)
    const plan = {
        vars: {
            params: a,
            baseLevel: ctx.levels?.items?.[0]?.name ?? "Level 1",
            topLevel: ctx.levels?.items?.[1]?.name ?? ctx.levels?.items?.[0]?.name ?? "Level 1",
            spans: [] // TODO: generar desde grids existentes o por ancho/largo
        },
        continueOnError: false,
        steps: [
            // ejes
            { action: "grid.create", args: { /* … */} },
            // columnas en intersección
            {
                action: "struct.columns.place_on_grid",
                args: {
                    baseLevel: "{$vars.baseLevel}",
                    topLevel: "{$vars.topLevel}",
                    familyType: "{$vars.params.columnType}",
                    gridX: ["A-{$vars.params.baysX}"], // si usas alfanumérico vs numérico, ajústalo
                    gridY: ["1-{$vars.params.baysY}"],
                    orientationRelativeTo: "Y",
                    skipIfColumnExistsNearby: true,
                    tolerance_m: 0.05
                }
            },
            // vigas (ejemplo con foreach de spans)
            {
                foreach: "spans",
                argsTemplate: {
                    level: "{$vars.baseLevel}",
                    familyType: "{$vars.params.beamType}",
                    elevation_m: 0,
                    start: "{$item.start}",
                    end: "{$item.end}"
                },
                action: "struct.beam.create"
            },
            // roof/muros/puertas/plans… (añadir cuando tengas las acciones listas)
            // { action: "roof.create_footprint", args: { … } },
            // { action: "wall.create_envelope", args: { type: "{$vars.params.wallType}", … } },
            // { action: "doc.sheets.create_bulk", args: { … } },
        ]
    };
    const result = await runPlan(postRevit, plan);
    return asText({ usedContext: { baseLevel: plan.vars.baseLevel, topLevel: plan.vars.topLevel }, result });
});
// stdio
const transport = new StdioServerTransport();
await server.connect(transport);
