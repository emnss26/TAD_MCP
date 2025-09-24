import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
const BRIDGE_URL = "http://127.0.0.1:55234/mcp";
// -------- helpers --------
async function callBridge(action, args) {
    const res = await fetch(BRIDGE_URL, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ action, args }),
    });
    const status = res.status;
    const text = await res.text();
    let data;
    try {
        data = JSON.parse(text);
    }
    catch {
        data = { ok: false, message: text };
    }
    return { status, data };
}
function okOrThrow(data) {
    if (!data?.ok)
        throw new Error(data?.message ?? "Bridge error");
}
// -------- schemas --------
// create_wall
const CreateWallShape = {
    level: z.string(),
    wallType: z.string(),
    start: z.object({ x: z.number(), y: z.number() }),
    end: z.object({ x: z.number(), y: z.number() }),
    height_m: z.number().positive(),
    structural: z.boolean().default(false),
};
const CreateWallSchema = z.object(CreateWallShape);
// view.category.set_visibility
const SetVisibilityShape = {
    categories: z.array(z.string()).min(1),
    visible: z.boolean().default(true),
    forceDetachTemplate: z.boolean().default(false),
    viewId: z.number().int().optional(),
};
const SetVisibilitySchema = z.object(SetVisibilityShape);
// view.category.override_color
const Rgb = z.object({
    r: z.number().int().min(0).max(255),
    g: z.number().int().min(0).max(255),
    b: z.number().int().min(0).max(255),
});
const Hex = z.string().regex(/^#?[0-9A-Fa-f]{6}$/, "Use #RRGGBB");
const OverrideColorShape = {
    categories: z.array(z.string()).min(1),
    color: z.union([Hex, Rgb]),
    transparency: z.number().int().min(0).max(100).default(0),
    halftone: z.boolean().default(false),
    surfaceSolid: z.boolean().default(true),
    projectionLines: z.boolean().default(false),
    forceDetachTemplate: z.boolean().default(false),
    viewId: z.number().int().optional(),
};
const OverrideColorSchema = z.object(OverrideColorShape);
// view.category.clear_overrides
const ClearOverridesShape = {
    categories: z.array(z.string()).min(1),
    forceDetachTemplate: z.boolean().default(false),
    viewId: z.number().int().optional(),
};
const ClearOverridesSchema = z.object(ClearOverridesShape);
// -------- server --------
const server = new McpServer({ name: "revit-mcp", version: "0.2.0" });
// Tool: create_wall  -> wall.create
server.registerTool("create_wall", {
    title: "Create a straight wall in Revit",
    description: "Creates a wall between two XY points (meters).",
    inputSchema: CreateWallShape,
}, async (args) => {
    const { status, data } = await callBridge("wall.create", args);
    const summary = data.ok
        ? `✅ Wall created (id: ${data.data?.elementId ?? data.elementId ?? "?"}).`
        : `❌ ${data.message}`;
    return { content: [{ type: "text", text: `${summary}\nHTTP ${status}` }] };
});
// Tool: view_category_set_visibility  -> view.category.set_visibility
server.registerTool("view_category_set_visibility", {
    title: "Set category visibility in current (or given) view",
    description: "categories: names o BuiltInCategory. visible=true|false. Optional viewId; forceDetachTemplate para vistas con plantilla.",
    inputSchema: SetVisibilityShape,
}, async (args) => {
    const { status, data } = await callBridge("view.category.set_visibility", args);
    okOrThrow(data);
    return { content: [{ type: "text", text: `✅ Visibility updated. HTTP ${status}` }] };
});
// Tool: view_category_override_color  -> view.category.override_color
server.registerTool("view_category_override_color", {
    title: "Override category color in view",
    description: "color: '#RRGGBB' o {r,g,b}. transparency 0-100. surfaceSolid/projectionLines/halftone opcionales.",
    inputSchema: OverrideColorShape,
}, async (args) => {
    const { status, data } = await callBridge("view.category.override_color", args);
    okOrThrow(data);
    return { content: [{ type: "text", text: `✅ Color overrides applied. HTTP ${status}` }] };
});
// Tool: view_category_clear_overrides  -> view.category.clear_overrides
server.registerTool("view_category_clear_overrides", {
    title: "Clear category overrides in view",
    description: "Elimina overrides de categorías en la vista.",
    inputSchema: ClearOverridesShape,
}, async (args) => {
    const { status, data } = await callBridge("view.category.clear_overrides", args);
    okOrThrow(data);
    return { content: [{ type: "text", text: `✅ Overrides cleared. HTTP ${status}` }] };
});
// stdio
const transport = new StdioServerTransport();
await server.connect(transport);
