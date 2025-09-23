import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
const BRIDGE_URL = "http://127.0.0.1:55234/mcp";
// Shape para registerTool
const CreateWallShape = {
    level: z.string(),
    wallType: z.string(),
    start: z.object({ x: z.number(), y: z.number() }),
    end: z.object({ x: z.number(), y: z.number() }),
    height_m: z.number().positive(),
    structural: z.boolean().default(false),
};
const CreateWallSchema = z.object(CreateWallShape);
const server = new McpServer({ name: "revit-mcp", version: "0.1.0" });
server.registerTool("create_wall", {
    title: "Create a straight wall in Revit",
    description: "Posts to the local Revit bridge to create a wall.",
    inputSchema: CreateWallShape,
}, async (args) => {
    // opcional: CreateWallSchema.parse(args);
    const payload = { action: "wall.create", args };
    const res = await fetch(BRIDGE_URL, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(payload),
    });
    const statusCode = res.status;
    const text = await res.text();
    let data;
    try {
        data = JSON.parse(text);
    }
    catch {
        data = { ok: false, message: text };
    }
    const summary = data.ok
        ? `✅ Wall created (id: ${data.elementId}).`
        : `❌ ${data.message}`;
    return {
        content: [
            { type: "text", text: summary },
            { type: "text", text: `HTTP ${statusCode}\n` + JSON.stringify(data, null, 2) },
        ],
    };
});
const transport = new StdioServerTransport();
await server.connect(transport);
