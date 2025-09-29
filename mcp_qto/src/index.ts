import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";

// Servidor MCP
const server = new McpServer({
  name: "mcp-qto",
  version: "1.0.0",
});

// Helper: serializa cualquier resultado como texto
const asText = (obj: unknown) => ({
  content: [{ type: "text" as const, text: JSON.stringify(obj, null, 2) }],
});

// Helper: castea shapes locales a la firma que espera el SDK
const asInputShape = <T extends Record<string, unknown>>(shape: T) =>
  shape as unknown as Record<string, any>;

/* ===========================
   qto.walls
   Args: { groupBy?: ("type"|"level"|"phase")[], includeIds?: boolean }
=========================== */
const QtoWallsShape = {
  groupBy: z.array(z.enum(["type", "level", "phase"])).optional(),
  includeIds: z.boolean().optional(),
};
const QtoWallsSchema = z.object(QtoWallsShape);

server.registerTool(
  "qto_walls",
  {
    title: "QTO Walls",
    description:
      "Cuantifica muros: longitud, área y volumen. Agrupa por type/level/phase y opcionalmente incluye filas con IDs.",
    inputSchema: asInputShape(QtoWallsShape),
  },
  async (rawArgs) => {
    const args = QtoWallsSchema.parse(rawArgs);
    const result = await postRevit("qto.walls", args);
    return asText(result);
  }
);

/* ===========================
   qto.floors
   Args: { groupBy?: ("type"|"level")[], includeIds?: boolean }
=========================== */
const QtoFloorsShape = {
  groupBy: z.array(z.enum(["type", "level"])).optional(),
  includeIds: z.boolean().optional(),
};
const QtoFloorsSchema = z.object(QtoFloorsShape);

server.registerTool(
  "qto_floors",
  {
    title: "QTO Floors",
    description:
      "Cuantifica pisos: área y volumen. Agrupa por type/level y puede incluir filas con IDs.",
    inputSchema: asInputShape(QtoFloorsShape),
  },
  async (rawArgs) => {
    const args = QtoFloorsSchema.parse(rawArgs);
    const result = await postRevit("qto.floors", args);
    return asText(result);
  }
);

/* ===========================
   qto.struct.concrete
   Args: {
     includeBeams?:bool, includeColumns?:bool, includeFoundation?:bool,
     groupBy?: ("type"|"level")[], includeIds?: boolean
   }
=========================== */
const QtoStructConcreteShape = {
  includeBeams: z.boolean().optional(),
  includeColumns: z.boolean().optional(),
  includeFoundation: z.boolean().optional(),
  groupBy: z.array(z.enum(["type", "level"])).optional(),
  includeIds: z.boolean().optional(),
};
const QtoStructConcreteSchema = z.object(QtoStructConcreteShape);

server.registerTool(
  "qto_struct_concrete",
  {
    title: "QTO Structural Concrete",
    description:
      "Cuantifica concreto estructural (vigas, columnas, cimentación): volumen total y m.l. donde aplique. Agrupa por type/level.",
    inputSchema: asInputShape(QtoStructConcreteShape),
  },
  async (rawArgs) => {
    const args = QtoStructConcreteSchema.parse(rawArgs);
    const result = await postRevit("qto.struct.concrete", args);
    return asText(result);
  }
);

/* ===========================
   qto.mep.pipes
   Args: { groupBy?: ("system"|"type"|"level")[], diameterBucketsMm?: number[], includeIds?: boolean }
=========================== */
const QtoMepPipesShape = {
  groupBy: z.array(z.enum(["system", "type", "level"])).optional(),
  diameterBucketsMm: z.array(z.number()).optional(),
  includeIds: z.boolean().optional(),
};
const QtoMepPipesSchema = z.object(QtoMepPipesShape);

server.registerTool(
  "qto_mep_pipes",
  {
    title: "QTO MEP Pipes",
    description:
      "Cuantifica tuberías: metros lineales totales y por buckets de diámetro (mm). Agrupa por system/type/level.",
    inputSchema: asInputShape(QtoMepPipesShape),
  },
  async (rawArgs) => {
    const args = QtoMepPipesSchema.parse(rawArgs);
    const result = await postRevit("qto.mep.pipes", args);
    return asText(result);
  }
);

/* ===========================
   qto.mep.ducts
   Args: { groupBy?: ("system"|"type"|"level")[], roundVsRect?: boolean, includeIds?: boolean }
=========================== */
const QtoMepDuctsShape = {
  groupBy: z.array(z.enum(["system", "type", "level"])).optional(),
  roundVsRect: z.boolean().optional(),
  includeIds: z.boolean().optional(),
};
const QtoMepDuctsSchema = z.object(QtoMepDuctsShape);

server.registerTool(
  "qto_mep_ducts",
  {
    title: "QTO MEP Ducts",
    description:
      "Cuantifica ductos: metros lineales y área superficial estimada. Puede distinguir redondos vs rectangulares y agrupar por system/type/level.",
    inputSchema: asInputShape(QtoMepDuctsShape),
  },
  async (rawArgs) => {
    const args = QtoMepDuctsSchema.parse(rawArgs);
    const result = await postRevit("qto.mep.ducts", args);
    return asText(result);
  }
);

// Arrancar stdio
const transport = new StdioServerTransport();
await server.connect(transport);
