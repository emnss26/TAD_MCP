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
const asText = (obj: unknown) => ({
  content: [{ type: "text" as const, text: JSON.stringify(obj, null, 2) }],
});

/* ============================
   struct.beam.create
   ============================ */
const BeamCreateShape = {
  level: z.string().optional(),          // el bridge resuelve activo si falta
  familyType: z.string().optional(),     // "Family: Type" o solo type
  elevation_m: z.number().optional(),    // en el bridge default = 3.0
  start: z.object({ x: z.number(), y: z.number() }),
  end: z.object({ x: z.number(), y: z.number() }),
};
const BeamCreateSchema = z.object(BeamCreateShape);

server.registerTool(
  "struct.beam.create",
  {
    title: "Create Structural Beam",
    description: "Crea una viga entre dos puntos XY en un nivel (opcional).",
    inputSchema: BeamCreateShape,
  },
  async (args: z.infer<typeof BeamCreateSchema>) => {
    const result = await postRevit("struct.beam.create", args);
    return asText(result);
  }
);

/* ============================
   struct.column.create
   ============================ */
const ColumnCreateShape = {
  level: z.string().optional(),
  familyType: z.string().optional(),     // "Family: Type" o solo type
  elevation_m: z.number().optional(),    // base offset; default 0.0 en bridge
  point: z.object({ x: z.number(), y: z.number() }),
};
const ColumnCreateSchema = z.object(ColumnCreateShape);

server.registerTool(
  "struct.column.create",
  {
    title: "Create Structural Column",
    description: "Crea una columna estructural en un punto XY (nivel opcional).",
    inputSchema: ColumnCreateShape,
  },
  async (args: z.infer<typeof ColumnCreateSchema>) => {
    const result = await postRevit("struct.column.create", args);
    return asText(result);
  }
);

/* ============================
   struct.floor.create
   ============================ */
const SFloorCreateShape = {
  level: z.string().optional(),
  floorType: z.string().optional(),
  profile: z.array(z.object({ x: z.number(), y: z.number() })).min(3), // polígono cerrado
};
const SFloorCreateSchema = z.object(SFloorCreateShape);

server.registerTool(
  "struct.floor.create",
  {
    title: "Create Structural Floor",
    description: "Crea un piso estructural por contorno en un nivel.",
    inputSchema: SFloorCreateShape,
  },
  async (args: z.infer<typeof SFloorCreateSchema>) => {
    const result = await postRevit("struct.floor.create", args);
    return asText(result);
  }
);

// stdio
const transport = new StdioServerTransport();
await server.connect(transport);
