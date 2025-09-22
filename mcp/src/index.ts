import { Server } from "@modelcontextprotocol/sdk";
import { z } from "zod";
import { request } from "undici";

const BRIDGE_URL = "http://127.0.0.1:55234/mcp";

const CreateWallSchema = z.object({
  level: z.string(),
  wallType: z.string(),
  start: z.object({ x: z.number(), y: z.number() }),
  end: z.object({ x: z.number(), y: z.number() }),
  height_m: z.number().positive(),
  structural: z.boolean().default(false)
});

const server = new Server({ name: "revit-mcp", version: "0.1.0" });

server.tool("create_wall", {
  description: "Create a straight wall in Revit.",
  inputSchema: CreateWallSchema,
  handler: async (input) => {
    const payload = { action: "wall.create", args: input };
    const { body, statusCode } = await request(BRIDGE_URL, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(payload)
    });
    const text = await body.text();
    let res: any; try { res = JSON.parse(text); } catch { res = { ok:false, message: text }; }

    return {
      content: [
        { type: "text", text: res.ok ? `✅ Wall created (id: ${res.elementId}).` : `❌ ${res.message}` },
        { type: "json", data: { httpStatus: statusCode, response: res } }
      ]
    };
  }
});

server.start();