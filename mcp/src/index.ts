// mcp/src/index.ts
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { request } from "undici";

const BRIDGE_URL = "http://127.0.0.1:55234/mcp";

// 1) SHAPE (ZodRawShape) para registerTool
const CreateWallShape = {
  level: z.string(),
  wallType: z.string(),
  start: z.object({ x: z.number(), y: z.number() }),
  end: z.object({ x: z.number(), y: z.number() }),
  height_m: z.number().positive(),
  structural: z.boolean().default(false),
};

// 2) Schema completo para validar en runtime
const CreateWallSchema = z.object(CreateWallShape);
type CreateWallInput = z.infer<typeof CreateWallSchema>;

const server = new McpServer({ name: "revit-mcp", version: "0.1.0" });

server.registerTool(
  "create_wall",
  {
    title: "Create a straight wall in Revit",
    description: "Posts to the local Revit bridge to create a wall.",
    // El SDK espera ZodRawShape aquí (no un z.object(...))
    inputSchema: CreateWallShape,
  },
  // Firma correcta: (args, extra) => ...
  async (args: CreateWallInput) => {
    // Si quieres doble validación, puedes hacer: CreateWallSchema.parse(args);

    const payload = { action: "wall.create", args };
    const { body, statusCode } = await request(BRIDGE_URL, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(payload),
    });

    const text = await body.text();
    let res: any;
    try { res = JSON.parse(text); } catch { res = { ok: false, message: text }; }

    const summary = res.ok
      ? `✅ Wall created (id: ${res.elementId}).`
      : `❌ ${res.message}`;

    // Usa SOLO tipos de contenido válidos. Aquí, dos "text":
    return {
      content: [
        { type: "text", text: summary },
        { type: "text", text: `HTTP ${statusCode}\n` + JSON.stringify(res, null, 2) },
      ],
    };
  }
);

// Conexión por stdio (patrón recomendado)
const transport = new StdioServerTransport();
await server.connect(transport);
