// mcp_qa/src/index.ts
import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";
// Server MCP
const server = new McpServer({
    name: "mcp-qa",
    version: "1.0.0",
});
// Helper: devolver JSON pretty como texto
const asText = (obj) => ({
    content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
});
/* =========================================
   qa.fix.pin_all_links
   ========================================= */
server.registerTool("qa_pin_all_links", {
    title: "Pin all Revit links",
    description: "Pone pin a todos los Revit Link Instances del modelo.",
    inputSchema: {},
}, async () => {
    const result = await postRevit("qa.fix.pin_all_links", {});
    return asText(result);
});
/* =========================================
   qa.fix.delete_imports
   ========================================= */
server.registerTool("qa_delete_imports", {
    title: "Delete all CAD imports",
    description: "Elimina todos los ImportInstance (CAD) del documento.",
    inputSchema: {},
}, async () => {
    const result = await postRevit("qa.fix.delete_imports", {});
    return asText(result);
});
/* =========================================
   qa.fix.apply_view_templates
   - soporta autoPickFirst y devuelve lista si hay varias plantillas
   ========================================= */
const ApplyViewTemplatesShape = {
    templateName: z.string().optional(),
    templateId: z.number().int().optional(),
    onlyWithoutTemplate: z.boolean().optional(), // default true en bridge
    viewIds: z.array(z.number().int()).optional(),
    autoPickFirst: z.boolean().optional(),
};
const ApplyViewTemplatesSchema = z.object(ApplyViewTemplatesShape);
server.registerTool("qa_apply_view_templates", {
    title: "Apply view template to views",
    description: "Aplica una plantilla a vistas. Si no se especifica plantilla y hay varias, el bridge devuelve la lista de opciones.",
    inputSchema: ApplyViewTemplatesShape,
}, async (args) => {
    const result = await postRevit("qa.fix.apply_view_templates", args);
    return asText(result);
});
/* =========================================
   qa.fix.remove_textnotes
   ========================================= */
const RemoveTextNotesShape = {
    viewId: z.number().int().optional(),
};
const RemoveTextNotesSchema = z.object(RemoveTextNotesShape);
server.registerTool("qa_remove_text_notes", {
    title: "Remove text notes",
    description: "Elimina todas las TextNotes de la vista activa o de la vista indicada por viewId.",
    inputSchema: RemoveTextNotesShape,
}, async (args) => {
    const result = await postRevit("qa.fix.remove_textnotes", args);
    return asText(result);
});
/* =========================================
   qa.fix.delete_unused_types
   ========================================= */
server.registerTool("qa_delete_unused_types", {
    title: "Delete unused element types",
    description: "Intenta borrar ElementTypes no usados (excluye ViewFamilyType y TitleBlocks).",
    inputSchema: {},
}, async () => {
    const result = await postRevit("qa.fix.delete_unused_types", {});
    return asText(result);
});
/* =========================================
   qa.fix.rename_views
   ========================================= */
const RenameViewsShape = {
    prefix: z.string().optional(),
    find: z.string().optional(),
    replace: z.string().optional(),
    viewIds: z.array(z.number().int()).optional(),
};
const RenameViewsSchema = z.object(RenameViewsShape);
server.registerTool("qa_rename_views", {
    title: "Rename views",
    description: "Renombra vistas (find/replace y/o prefix). Si no se pasan viewIds, aplica a todas las vistas no plantilla.",
    inputSchema: RenameViewsShape,
}, async (args) => {
    const result = await postRevit("qa.fix.rename_views", args);
    return asText(result);
});
// stdio transport
const transport = new StdioServerTransport();
await server.connect(transport);
