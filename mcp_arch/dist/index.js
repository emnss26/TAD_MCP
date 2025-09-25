import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";
// Crea el servidor MCP
const server = new McpServer({
    name: "mcp-arch",
    version: "1.0.0",
});
// Helper: empaqueta cualquier objeto como texto (JSON pretty)
const asText = (obj) => ({
    content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
});
// --- wall.create ---
const WallCreateShape = {
    level: z.string().optional(),
    wallType: z.string().optional(),
    start: z.object({ x: z.number(), y: z.number() }),
    end: z.object({ x: z.number(), y: z.number() }),
    height_m: z.number().optional(),
    structural: z.boolean().optional(),
};
const WallCreateSchema = z.object(WallCreateShape);
server.registerTool("arch_wall_create", {
    title: "Create Wall",
    description: "Crea un muro recto con tipo/nivel opcional",
    inputSchema: WallCreateShape,
}, async (args) => {
    const result = await postRevit("wall.create", args);
    return asText(result);
});
// --- level.create ---
const LevelCreateShape = {
    elevation_m: z.number(),
    name: z.string().optional(),
};
const LevelCreateSchema = z.object(LevelCreateShape);
server.registerTool("arch_level_create", {
    title: "Create Level",
    description: "Crea un nivel a cierta elevación (m)",
    inputSchema: LevelCreateShape,
}, async (args) => {
    const result = await postRevit("level.create", args);
    return asText(result);
});
// --- grid.create ---
const GridCreateShape = {
    start: z.object({ x: z.number(), y: z.number() }),
    end: z.object({ x: z.number(), y: z.number() }),
    name: z.string().optional(),
};
const GridCreateSchema = z.object(GridCreateShape);
server.registerTool("arch_grid_create", {
    title: "Create Grid",
    description: "Crea una retícula",
    inputSchema: GridCreateShape,
}, async (args) => {
    const result = await postRevit("grid.create", args);
    return asText(result);
});
// --- floor.create ---
const FloorCreateShape = {
    level: z.string().optional(),
    floorType: z.string().optional(),
    profile: z.array(z.object({ x: z.number(), y: z.number() })).min(3),
};
const FloorCreateSchema = z.object(FloorCreateShape);
server.registerTool("arch_floor_create", {
    title: "Create Floor",
    description: "Crea un piso por contorno en un nivel",
    inputSchema: FloorCreateShape,
}, async (args) => {
    const result = await postRevit("floor.create", args);
    return asText(result);
});
// --- ceiling.create --- (expone la acción; el bridge responderá NotImplemented)
server.registerTool("arch_ceiling_create", {
    title: "Create Ceiling (sketch)",
    description: "Intento de crear techo por sketch (no implementado en el bridge)",
    inputSchema: {}, // no usa args en el bridge actual
}, async () => {
    const result = await postRevit("ceiling.create", {}); // propagará el error del bridge
    return asText(result);
});
// --- door.place ---
const DoorPlaceShape = {
    // ahora opcionales, el bridge puede resolver host y punto
    hostWallId: z.number().int().optional(),
    level: z.string().optional(),
    familySymbol: z.string().optional(),
    point: z.object({ x: z.number(), y: z.number() }).optional(),
    offset_m: z.number().optional(),
    // nuevos parámetros soportados por tu bridge:
    offsetAlong_m: z.number().optional(), // distancia (m) a lo largo del muro
    alongNormalized: z.number().min(0).max(1).optional(), // 0..1 a lo largo del muro
    flipHand: z.boolean().optional(),
    flipFacing: z.boolean().optional(),
};
const DoorPlaceSchema = z.object(DoorPlaceShape);
server.registerTool("arch_door_place", {
    title: "Place Door",
    description: "Coloca una puerta. hostWallId/point pueden omitirse; el bridge resuelve por selección o muro cercano. Soporta offsetAlong_m / alongNormalized.",
    inputSchema: DoorPlaceShape,
}, async (args) => {
    const result = await postRevit("door.place", args);
    return asText(result);
});
// --- window.place --- (mismo shape que puerta)
const WindowPlaceShape = DoorPlaceShape;
const WindowPlaceSchema = DoorPlaceSchema;
server.registerTool("arch_window_place", {
    title: "Place Window",
    description: "Coloca una ventana. hostWallId/point pueden omitirse; el bridge resuelve por selección o muro cercano. Soporta offsetAlong_m / alongNormalized.",
    inputSchema: WindowPlaceShape,
}, async (args) => {
    const result = await postRevit("window.place", args);
    return asText(result);
});
// Arrancar stdio
const transport = new StdioServerTransport();
await server.connect(transport);
