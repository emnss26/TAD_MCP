// mcp_str/src/index.ts
import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";
// Servidor MCP
const server = new McpServer({
    name: "mcp-str",
    version: "1.0.0",
});
// Respuesta como texto (JSON pretty)
const asText = (obj) => ({
    content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
});
/* ============================
   struct.beam.create
   ============================ */
const BeamCreateShape = {
    level: z.string().optional(), // el bridge resuelve activo si falta
    familyType: z.string().optional(), // "Family: Type" o solo type
    elevation_m: z.number().optional(), // en el bridge default = 3.0
    start: z.object({ x: z.number(), y: z.number() }),
    end: z.object({ x: z.number(), y: z.number() }),
};
const BeamCreateSchema = z.object(BeamCreateShape);
server.registerTool("struct_beam_create", {
    title: "Create Structural Beam",
    description: "Crea una viga entre dos puntos XY en un nivel (opcional).",
    inputSchema: BeamCreateShape,
}, async (args) => {
    const result = await postRevit("struct.beam.create", args);
    return asText(result);
});
/* ============================
   struct.column.create
   ============================ */
const ColumnCreateShape = {
    level: z.string().optional(),
    familyType: z.string().optional(), // "Family: Type" o solo type
    elevation_m: z.number().optional(), // base offset; default 0.0 en bridge
    point: z.object({ x: z.number(), y: z.number() }),
};
const ColumnCreateSchema = z.object(ColumnCreateShape);
server.registerTool("struct_column_create", {
    title: "Create Structural Column",
    description: "Crea una columna estructural en un punto XY (nivel opcional).",
    inputSchema: ColumnCreateShape,
}, async (args) => {
    const result = await postRevit("struct.column.create", args);
    return asText(result);
});
/* ============================
   struct.floor.create
   ============================ */
const SFloorCreateShape = {
    level: z.string().optional(),
    floorType: z.string().optional(),
    profile: z.array(z.object({ x: z.number(), y: z.number() })).min(3), // polígono cerrado
};
const SFloorCreateSchema = z.object(SFloorCreateShape);
server.registerTool("struct_floor_create", {
    title: "Create Structural Floor",
    description: "Crea un piso estructural por contorno en un nivel.",
    inputSchema: SFloorCreateShape,
}, async (args) => {
    const result = await postRevit("struct.floor.create", args);
    return asText(result);
});
/* ============================
   struct.columns.place_on_grid
   ============================ */
const ColumnsPlaceOnGridShape = {
    baseLevel: z.string(), // requerido
    topLevel: z.string().optional(), // opcional (usa baseLevel si falta)
    familyType: z.string(), // requerido ("Family: Type" o solo Type)
    gridX: z.array(z.string()).optional(), // p.ej. ["A-E"] o ["A","B","C"]
    gridY: z.array(z.string()).optional(), // p.ej. ["1-8"] o ["1","2","3"]
    gridNames: z.array(z.string()).optional(), // alternativa: lista plana (el server separa X/Y por dirección)
    baseOffset_m: z.number().optional(), // default 0
    topOffset_m: z.number().optional(), // default 0
    onlyIntersectionsInsideActiveCrop: z.boolean().optional(), // filtra por crop de vista activa
    tolerance_m: z.number().optional(), // default 0.05
    skipIfColumnExistsNearby: z.boolean().optional(),
    worksetName: z.string().optional(),
    pinned: z.boolean().optional(),
    orientationRelativeTo: z.enum(["X", "Y", "None"]).optional(), // rotación respecto a Z
};
const ColumnsPlaceOnGridSchema = z.object(ColumnsPlaceOnGridShape);
server.registerTool("struct_columns_place_on_grid", {
    title: "Place Columns on Grid",
    description: "Coloca columnas estructurales en intersecciones de ejes (rangos A-E / 1-8 o gridNames).",
    inputSchema: ColumnsPlaceOnGridShape,
}, async (args) => {
    const result = await postRevit("struct.columns.place_on_grid", args);
    return asText(result);
});
// stdio
const transport = new StdioServerTransport();
await server.connect(transport);
