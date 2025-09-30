import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";

const server = new McpServer({ name: "mcp-qto", version: "1.0.0" });
const asText = (obj: unknown) => ({ content: [{ type: "text" as const, text: JSON.stringify(obj, null, 2) }] });
const asInputShape = <T extends Record<string, unknown>>(shape: T) => shape as unknown as Record<string, any>;

/* qto.walls */
const WallsShape = {
  groupBy: z.array(z.enum(["type", "level", "phase"])).optional(),
  includeIds: z.boolean().optional(),
};
const Walls = z.object(WallsShape);

server.registerTool(
  "qto_walls",
  { title: "QTO Walls", description: "Metrajes de muros por agrupación.", inputSchema: asInputShape(WallsShape) },
  async (rawArgs) => {
    const args = Walls.parse(rawArgs);
    const result = await postRevit("qto.walls", args);
    return asText(result);
  }
);

/* qto.floors */
const FloorsShape = {
  groupBy: z.array(z.enum(["type", "level"])).optional(),
  includeIds: z.boolean().optional(),
};
const Floors = z.object(FloorsShape);

server.registerTool(
  "qto_floors",
  { title: "QTO Floors", description: "Metrajes de pisos por agrupación.", inputSchema: asInputShape(FloorsShape) },
  async (rawArgs) => {
    const args = Floors.parse(rawArgs);
    const result = await postRevit("qto.floors", args);
    return asText(result);
  }
);

/* qto.struct.concrete */
const StructConcreteShape = {
  includeBeams: z.boolean().optional(),
  includeColumns: z.boolean().optional(),
  includeFoundation: z.boolean().optional(),
  groupBy: z.array(z.enum(["type", "level"])).optional(),
  includeIds: z.boolean().optional(),
};
const StructConcrete = z.object(StructConcreteShape);

server.registerTool(
  "qto_struct_concrete",
  {
    title: "QTO Structural Concrete",
    description: "Volúmenes y m.l. (vigas) por agrupación.",
    inputSchema: asInputShape(StructConcreteShape),
  },
  async (rawArgs) => {
    const args = StructConcrete.parse(rawArgs);
    const result = await postRevit("qto.struct.concrete", args);
    return asText(result);
  }
);

/* qto.mep.pipes */
const PipesShape = {
  groupBy: z.array(z.enum(["system", "type", "level"])).optional(),
  diameterBucketsMm: z.array(z.number()).optional(),
  includeIds: z.boolean().optional(),
};
const Pipes = z.object(PipesShape);

server.registerTool(
  "qto_mep_pipes",
  {
    title: "QTO MEP Pipes",
    description: "m.l. totales y por buckets de diámetro (mm).",
    inputSchema: asInputShape(PipesShape),
  },
  async (rawArgs) => {
    const args = Pipes.parse(rawArgs);
    const result = await postRevit("qto.mep.pipes", args);
    return asText(result);
  }
);

/* qto.mep.ducts */
const DuctsShape = {
  groupBy: z.array(z.enum(["system", "type", "level"])).optional(),
  roundVsRect: z.boolean().optional(),
  includeIds: z.boolean().optional(),
};
const Ducts = z.object(DuctsShape);

server.registerTool(
  "qto_mep_ducts",
  {
    title: "QTO MEP Ducts",
    description: "m.l. y área superficial estimada. Puede separar redondos/rectangulares.",
    inputSchema: asInputShape(DuctsShape),
  },
  async (rawArgs) => {
    const args = Ducts.parse(rawArgs);
    const result = await postRevit("qto.mep.ducts", args);
    return asText(result);
  }
);

const transport = new StdioServerTransport();
await server.connect(transport);