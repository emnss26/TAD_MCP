// mcp_mep/src/index.ts
import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";
// Server MCP
const server = new McpServer({
    name: "mcp-mep",
    version: "1.0.0",
});
// Helper: respuesta como texto (JSON pretty)
const asText = (obj) => ({
    content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
});
// PT2 común
const PtShape = z.object({ x: z.number(), y: z.number() });
/* =========================================
   mep.duct.create
   ========================================= */
const DuctCreateShape = {
    level: z.string().optional(),
    systemType: z.string().optional(), // nombre o clasificación (SupplyAir, ReturnAir, etc)
    ductType: z.string().optional(),
    elevation_m: z.number().optional(), // por defecto en bridge: 2.7
    start: PtShape,
    end: PtShape,
    width_mm: z.number().optional(), // rectangular
    height_mm: z.number().optional(), // rectangular
    diameter_mm: z.number().optional(), // redondo
};
const DuctCreateSchema = z.object(DuctCreateShape);
server.registerTool("mep_duct_create", {
    title: "Create Duct",
    description: "Crea un ducto entre dos puntos XY (m). Soporta systemType/ductType y dimensiones opcionales.",
    inputSchema: DuctCreateShape,
}, async (args) => {
    const result = await postRevit("mep.duct.create", args);
    return asText(result);
});
/* =========================================
   mep.pipe.create
   ========================================= */
const PipeCreateShape = {
    level: z.string().optional(),
    systemType: z.string().optional(), // PipingSystemType (nombre)
    pipeType: z.string().optional(),
    elevation_m: z.number().optional(), // por defecto en bridge: 2.5
    start: PtShape,
    end: PtShape,
    diameter_mm: z.number().optional(),
};
const PipeCreateSchema = z.object(PipeCreateShape);
server.registerTool("mep_pipe_create", {
    title: "Create Pipe",
    description: "Crea una tubería entre dos puntos XY (m). systemType/pipeType opcionales; diámetro opcional.",
    inputSchema: PipeCreateShape,
}, async (args) => {
    const result = await postRevit("mep.pipe.create", args);
    return asText(result);
});
/* =========================================
   mep.conduit.create
   ========================================= */
const ConduitCreateShape = {
    level: z.string().optional(),
    conduitType: z.string().optional(),
    elevation_m: z.number().optional(), // por defecto en bridge: 2.4
    start: PtShape,
    end: PtShape,
    diameter_mm: z.number().optional(),
};
const ConduitCreateSchema = z.object(ConduitCreateShape);
server.registerTool("mep_conduit_create", {
    title: "Create Conduit",
    description: "Crea un conduit entre dos puntos XY (m). Tipo y diámetro opcionales.",
    inputSchema: ConduitCreateShape,
}, async (args) => {
    const result = await postRevit("mep.conduit.create", args);
    return asText(result);
});
/* =========================================
   mep.cabletray.create
   ========================================= */
const CableTrayCreateShape = {
    level: z.string().optional(),
    cableTrayType: z.string().optional(),
    elevation_m: z.number().optional(), // por defecto en bridge: 2.7
    start: PtShape,
    end: PtShape,
};
const CableTrayCreateSchema = z.object(CableTrayCreateShape);
server.registerTool("mep_cabletray_create", {
    title: "Create Cable Tray",
    description: "Crea una charola entre dos puntos XY (m). Tipo opcional.",
    inputSchema: CableTrayCreateShape,
}, async (args) => {
    const result = await postRevit("mep.cabletray.create", args);
    return asText(result);
});
// stdio
const transport = new StdioServerTransport();
await server.connect(transport);
