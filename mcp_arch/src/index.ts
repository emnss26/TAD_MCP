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
const asText = (obj: unknown) => ({
  content: [{ type: "text" as const, text: JSON.stringify(obj, null, 2) }],
});

// ===== Shapes / Schemas reutilizables =====
const Pt2Shape = { x: z.number(), y: z.number() };
const Pt2Schema = z.object(Pt2Shape);

// ===== wall.create =====
const WallCreateShape = {
  level: z.string().optional(),
  wallType: z.string().optional(),
  start: z.object(Pt2Shape),
  end: z.object(Pt2Shape),
  height_m: z.number().optional(),
  structural: z.boolean().optional(),
};
const WallCreateSchema = z.object(WallCreateShape);

server.registerTool(
  "arch_wall_create",
  {
    title: "Create Wall",
    description: "Crea un muro recto con tipo/nivel opcional",
    inputSchema: WallCreateShape, // <- SHAPE
  },
  async (args: z.infer<typeof WallCreateSchema>) => {
    const result = await postRevit("wall.create", args);
    return asText(result);
  }
);

// ===== level.create =====
const LevelCreateShape = {
  elevation_m: z.number(),
  name: z.string().optional(),
};
const LevelCreateSchema = z.object(LevelCreateShape);

server.registerTool(
  "arch_level_create",
  {
    title: "Create Level",
    description: "Crea un nivel a cierta elevación (m)",
    inputSchema: LevelCreateShape, // <- SHAPE
  },
  async (args: z.infer<typeof LevelCreateSchema>) => {
    const result = await postRevit("level.create", args);
    return asText(result);
  }
);

// ===== grid.create =====
const GridCreateShape = {
  start: z.object(Pt2Shape),
  end: z.object(Pt2Shape),
  name: z.string().optional(),
};
const GridCreateSchema = z.object(GridCreateShape);

server.registerTool(
  "arch_grid_create",
  {
    title: "Create Grid",
    description: "Crea una retícula",
    inputSchema: GridCreateShape, // <- SHAPE
  },
  async (args: z.infer<typeof GridCreateSchema>) => {
    const result = await postRevit("grid.create", args);
    return asText(result);
  }
);

// ===== floor.create =====
const FloorCreateShape = {
  level: z.string().optional(),
  floorType: z.string().optional(),
  profile: z.array(z.object(Pt2Shape)).min(3),
};
const FloorCreateSchema = z.object(FloorCreateShape);

server.registerTool(
  "arch_floor_create",
  {
    title: "Create Floor",
    description: "Crea un piso por contorno en un nivel",
    inputSchema: FloorCreateShape, // <- SHAPE
  },
  async (args: z.infer<typeof FloorCreateSchema>) => {
    const result = await postRevit("floor.create", args);
    return asText(result);
  }
);

// ===== ceiling.create (NotImplemented en bridge) =====
server.registerTool(
  "arch_ceiling_create",
  {
    title: "Create Ceiling (sketch)",
    description:
      "Intento de crear techo por sketch (no implementado en el bridge)",
    inputSchema: {}, // <- SHAPE vacío
  },
  async () => {
    const result = await postRevit("ceiling.create", {}); // propagará el error del bridge
    return asText(result);
  }
);

// ===== door.place =====
const DoorPlaceShape = {
  hostWallId: z.number().int().optional(),
  level: z.string().optional(),
  familySymbol: z.string().optional(),
  point: z.object(Pt2Shape).optional(),
  offset_m: z.number().optional(),
  offsetAlong_m: z.number().optional(),
  alongNormalized: z.number().min(0).max(1).optional(),
  flipHand: z.boolean().optional(),
  flipFacing: z.boolean().optional(),
};
const DoorPlaceSchema = z.object(DoorPlaceShape);

server.registerTool(
  "arch_door_place",
  {
    title: "Place Door",
    description:
      "Coloca una puerta. hostWallId/point pueden omitirse; el bridge resuelve por selección o muro cercano. Soporta offsetAlong_m / alongNormalized.",
    inputSchema: DoorPlaceShape, // <- SHAPE
  },
  async (args: z.infer<typeof DoorPlaceSchema>) => {
    const result = await postRevit("door.place", args);
    return asText(result);
  }
);

// ===== window.place (mismo shape que puerta) =====
const WindowPlaceShape = DoorPlaceShape;
const WindowPlaceSchema = DoorPlaceSchema;

server.registerTool(
  "arch_window_place",
  {
    title: "Place Window",
    description:
      "Coloca una ventana. hostWallId/point pueden omitirse; el bridge resuelve por selección o muro cercano. Soporta offsetAlong_m / alongNormalized.",
    inputSchema: WindowPlaceShape, // <- SHAPE
  },
  async (args: z.infer<typeof WindowPlaceSchema>) => {
    const result = await postRevit("window.place", args);
    return asText(result);
  }
);

// ===== rooms.create_on_levels =====
const RoomsCreateOnLevelsShape = {
  levelNames: z.array(z.string()).optional(),
  placeOnlyEnclosed: z.boolean().optional(),
};
const RoomsCreateOnLevelsSchema = z.object(RoomsCreateOnLevelsShape);

server.registerTool(
  "arch_rooms_create_on_levels",
  {
    title: "Create Rooms on Levels",
    description:
      "Crea habitaciones automáticamente en los niveles indicados (o en todos si no se especifican).",
    inputSchema: RoomsCreateOnLevelsShape, // <- SHAPE
  },
  async (args: z.infer<typeof RoomsCreateOnLevelsSchema>) => {
    const result = await postRevit("rooms.create_on_levels", args);
    return asText(result);
  }
);

// ===== floors.from_rooms =====
const FloorsFromRoomsShape = {
  roomIds: z.array(z.number().int()).min(1),
  floorType: z.string().optional(),
  baseOffset_m: z.number().optional(),
};
const FloorsFromRoomsSchema = z.object(FloorsFromRoomsShape);

server.registerTool(
  "arch_floors_from_rooms",
  {
    title: "Create Floors from Rooms",
    description:
      "Crea pisos siguiendo el borde de las habitaciones. Acepta floorType y baseOffset_m.",
    inputSchema: FloorsFromRoomsShape, // <- SHAPE
  },
  async (args: z.infer<typeof FloorsFromRoomsSchema>) => {
    const result = await postRevit("floors.from_rooms", args);
    return asText(result);
  }
);

// ===== roof.create_footprint =====
const RoofCreateFootprintShape = {
  level: z.string(), // requerido por el bridge
  roofType: z.string().optional(),
  profile: z.array(z.object(Pt2Shape)).min(3),
  slope: z.number().optional(), // grados
};
const RoofCreateFootprintSchema = z.object(RoofCreateFootprintShape);

server.registerTool(
  "arch_roof_create_footprint",
  {
    title: "Create Roof (Footprint)",
    description:
      "Crea una cubierta por huella en un nivel, con perfil cerrado y pendiente opcional (grados).",
    inputSchema: RoofCreateFootprintShape, // <- SHAPE
  },
  async (args: z.infer<typeof RoofCreateFootprintSchema>) => {
    const result = await postRevit("roof.create_footprint", args);
    return asText(result);
  }
);

// ===== ceilings.from_rooms (opcional; NotImplemented en bridge) =====
const CeilingsFromRoomsShape = {
  roomIds: z.array(z.number().int()).min(1),
  ceilingType: z.string().optional(),
  height_m: z.number().optional(),
};
const CeilingsFromRoomsSchema = z.object(CeilingsFromRoomsShape);

server.registerTool(
  "arch_ceilings_from_rooms",
  {
    title: "Create Ceilings from Rooms",
    description:
      "Crea techos a partir de habitaciones (no implementado aún en el bridge; devolverá error).",
    inputSchema: CeilingsFromRoomsShape, // <- SHAPE
  },
  async (args: z.infer<typeof CeilingsFromRoomsSchema>) => {
    const result = await postRevit("ceilings.from_rooms", args);
    return asText(result);
  }
);

// Arrancar stdio
const transport = new StdioServerTransport();
await server.connect(transport);
