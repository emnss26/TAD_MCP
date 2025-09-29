// mcp_arch/src/index.ts
import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";
const server = new McpServer({ name: "mcp-arch", version: "1.0.0" });
const asText = (obj) => ({
    content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
});
// 👇 helper para calzar el tipo que pide el SDK
const asInputShape = (shape) => shape;
const Pt2Shape = { x: z.number(), y: z.number() };
const Pt2 = z.object(Pt2Shape);
/* wall.create */
const WallCreateShape = {
    level: z.string().optional(),
    wallType: z.string().optional(),
    start: z.object(Pt2Shape),
    end: z.object(Pt2Shape),
    height_m: z.number().optional(),
    structural: z.boolean().optional(),
};
const WallCreate = z.object(WallCreateShape);
server.registerTool("arch_wall_create", {
    title: "Create Wall",
    description: "Crea un muro recto con tipo/nivel opcional",
    inputSchema: asInputShape(WallCreateShape),
}, async (rawArgs) => {
    const args = WallCreate.parse(rawArgs);
    const result = await postRevit("wall.create", args);
    return asText(result);
});
/* level.create */
const LevelCreateShape = { elevation_m: z.number(), name: z.string().optional() };
const LevelCreate = z.object(LevelCreateShape);
server.registerTool("arch_level_create", {
    title: "Create Level",
    description: "Crea un nivel a cierta elevación (m)",
    inputSchema: asInputShape(LevelCreateShape),
}, async (rawArgs) => {
    const args = LevelCreate.parse(rawArgs);
    const result = await postRevit("level.create", args);
    return asText(result);
});
/* grid.create */
const GridCreateShape = { start: Pt2, end: Pt2, name: z.string().optional() };
const GridCreate = z.object(GridCreateShape);
server.registerTool("arch_grid_create", {
    title: "Create Grid",
    description: "Crea una retícula",
    inputSchema: asInputShape(GridCreateShape),
}, async (rawArgs) => {
    const args = GridCreate.parse(rawArgs);
    const result = await postRevit("grid.create", args);
    return asText(result);
});
/* floor.create */
const FloorCreateShape = {
    level: z.string().optional(),
    floorType: z.string().optional(),
    profile: z.array(Pt2).min(3),
};
const FloorCreate = z.object(FloorCreateShape);
server.registerTool("arch_floor_create", {
    title: "Create Floor",
    description: "Crea un piso por contorno en un nivel",
    inputSchema: asInputShape(FloorCreateShape),
}, async (rawArgs) => {
    const args = FloorCreate.parse(rawArgs);
    const result = await postRevit("floor.create", args);
    return asText(result);
});
/* ceiling.create (no args) */
server.registerTool("arch_ceiling_create", {
    title: "Create Ceiling (sketch)",
    description: "Intento de crear techo por sketch (no implementado en el bridge)",
    inputSchema: asInputShape({}),
}, async () => {
    const result = await postRevit("ceiling.create", {});
    return asText(result);
});
/* door.place */
const DoorPlaceShape = {
    hostWallId: z.number().int().optional(),
    level: z.string().optional(),
    familySymbol: z.string().optional(),
    point: Pt2.optional(),
    offset_m: z.number().optional(),
    offsetAlong_m: z.number().optional(),
    alongNormalized: z.number().min(0).max(1).optional(),
    flipHand: z.boolean().optional(),
    flipFacing: z.boolean().optional(),
};
const DoorPlace = z.object(DoorPlaceShape);
server.registerTool("arch_door_place", {
    title: "Place Door",
    description: "Coloca una puerta. hostWallId/point pueden omitirse; el bridge resuelve por selección o muro cercano. Soporta offsetAlong_m / alongNormalized.",
    inputSchema: asInputShape(DoorPlaceShape),
}, async (rawArgs) => {
    const args = DoorPlace.parse(rawArgs);
    const result = await postRevit("door.place", args);
    return asText(result);
});
/* window.place (mismo shape) */
const WindowPlace = DoorPlace;
server.registerTool("arch_window_place", {
    title: "Place Window",
    description: "Coloca una ventana. hostWallId/point pueden omitirse; el bridge resuelve por selección o muro cercano. Soporta offsetAlong_m / alongNormalized.",
    inputSchema: asInputShape(DoorPlaceShape),
}, async (rawArgs) => {
    const args = WindowPlace.parse(rawArgs);
    const result = await postRevit("window.place", args);
    return asText(result);
});
/* rooms.create_on_levels */
const RoomsOnLevelsShape = {
    levelNames: z.array(z.string()).optional(),
    placeOnlyEnclosed: z.boolean().optional(),
};
const RoomsOnLevels = z.object(RoomsOnLevelsShape);
server.registerTool("arch_rooms_create_on_levels", {
    title: "Create Rooms on Levels",
    description: "Crea habitaciones automáticamente en los niveles indicados (o en todos).",
    inputSchema: asInputShape(RoomsOnLevelsShape),
}, async (rawArgs) => {
    const args = RoomsOnLevels.parse(rawArgs);
    const result = await postRevit("rooms.create_on_levels", args);
    return asText(result);
});
/* floors.from_rooms */
const FloorsFromRoomsShape = {
    roomIds: z.array(z.number().int()).min(1),
    floorType: z.string().optional(),
    baseOffset_m: z.number().optional(),
};
const FloorsFromRooms = z.object(FloorsFromRoomsShape);
server.registerTool("arch_floors_from_rooms", {
    title: "Create Floors from Rooms",
    description: "Crea pisos siguiendo el borde de las habitaciones.",
    inputSchema: asInputShape(FloorsFromRoomsShape),
}, async (rawArgs) => {
    const args = FloorsFromRooms.parse(rawArgs);
    const result = await postRevit("floors.from_rooms", args);
    return asText(result);
});
/* roof.create_footprint */
const RoofFootprintShape = {
    level: z.string(),
    roofType: z.string().optional(),
    profile: z.array(Pt2).min(3),
    slope: z.number().optional(),
};
const RoofFootprint = z.object(RoofFootprintShape);
server.registerTool("arch_roof_create_footprint", {
    title: "Create Roof (Footprint)",
    description: "Crea una cubierta por huella con pendiente opcional (grados).",
    inputSchema: asInputShape(RoofFootprintShape),
}, async (rawArgs) => {
    const args = RoofFootprint.parse(rawArgs);
    const result = await postRevit("roof.create_footprint", args);
    return asText(result);
});
/* ceilings.from_rooms (not implemented) */
const CeilFromRoomsShape = {
    roomIds: z.array(z.number().int()).min(1),
    ceilingType: z.string().optional(),
    height_m: z.number().optional(),
};
const CeilFromRooms = z.object(CeilFromRoomsShape);
server.registerTool("arch_ceilings_from_rooms", {
    title: "Create Ceilings from Rooms",
    description: "No implementado en el bridge; devolverá error.",
    inputSchema: asInputShape(CeilFromRoomsShape),
}, async (rawArgs) => {
    const args = CeilFromRooms.parse(rawArgs);
    const result = await postRevit("ceilings.from_rooms", args);
    return asText(result);
});
const transport = new StdioServerTransport();
await server.connect(transport);
