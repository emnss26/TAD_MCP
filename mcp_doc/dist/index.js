import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { postRevit } from "./bridge.js";
// Servidor MCP
const server = new McpServer({
    name: "mcp-doc",
    version: "1.0.0",
});
// Helper: serializa cualquier resultado como texto
const asText = (obj) => ({
    content: [{ type: "text", text: JSON.stringify(obj, null, 2) }],
});
// Helper: castea shapes locales a la firma que espera el SDK
const asInputShape = (shape) => shape;
/* ===========================
   sheets.create
=========================== */
const SheetsCreateShape = {
    titleBlockType: z.string().optional(), // "Family: Type" o solo "Type"
    number: z.string().optional(),
    name: z.string().optional(),
};
const SheetsCreateSchema = z.object(SheetsCreateShape);
server.registerTool("doc_sheets_create", {
    title: "Create Sheet",
    description: "Crea una lámina con número/nombre opcional y un type de carátula (titleblock) opcional.",
    inputSchema: asInputShape(SheetsCreateShape),
}, async (rawArgs) => {
    const args = SheetsCreateSchema.parse(rawArgs);
    const result = await postRevit("sheets.create", args);
    return asText(result);
});
/* ===========================
   sheets.create_bulk
=========================== */
const SheetItemShape = {
    number: z.string(), // requerido a nivel de cliente (el bridge igual valida)
    name: z.string().optional(),
};
const SheetsCreateBulkShape = {
    titleBlockType: z.string().optional(),
    items: z.array(z.object(SheetItemShape)).min(1),
};
const SheetsCreateBulkSchema = z.object(SheetsCreateBulkShape);
server.registerTool("doc_sheets_create_bulk", {
    title: "Create Sheets (Bulk)",
    description: "Crea múltiples láminas en lote. Evita duplicados por número de lámina.",
    inputSchema: asInputShape(SheetsCreateBulkShape),
}, async (rawArgs) => {
    const args = SheetsCreateBulkSchema.parse(rawArgs);
    const result = await postRevit("sheets.create_bulk", args);
    return asText(result);
});
/* ===========================
   sheets.add_views
=========================== */
const SheetsAddViewsShape = {
    sheetId: z.number().int().optional(),
    sheetName: z.string().optional(), // nombre o número
    viewIds: z.array(z.number().int()).optional(),
    viewNames: z.array(z.string()).optional(),
};
const SheetsAddViewsSchema = z.object(SheetsAddViewsShape);
server.registerTool("doc_sheets_add_views", {
    title: "Add Views to Sheet",
    description: "Agrega vistas a una lámina. Puedes referenciar la lámina por Id o nombre/número; y las vistas por Id o nombre.",
    inputSchema: asInputShape(SheetsAddViewsShape),
}, async (rawArgs) => {
    const args = SheetsAddViewsSchema.parse(rawArgs);
    const result = await postRevit("sheets.add_views", args);
    return asText(result);
});
/* ===========================
   sheets.assign_revisions
=========================== */
const SheetsAssignRevisionsShape = {
    sheetIds: z.array(z.number().int()).optional(),
    sheetNames: z.array(z.string()).optional(), // nombres o números
    revisionName: z.string().optional(), // el bridge usa Description = revisionName si no hay description
    description: z.string().optional(), // si viene, override del revisionName
    date: z.string().optional(), // string tal cual (ej. "2025-09-28")
};
const SheetsAssignRevisionsSchema = z.object(SheetsAssignRevisionsShape);
server.registerTool("doc_sheets_assign_revisions", {
    title: "Assign Revision to Sheets",
    description: "Crea/actualiza una revisión (por descripción/fecha) y la asigna a láminas objetivo.",
    inputSchema: asInputShape(SheetsAssignRevisionsShape),
}, async (rawArgs) => {
    const args = SheetsAssignRevisionsSchema.parse(rawArgs);
    const result = await postRevit("sheets.assign_revisions", args);
    return asText(result);
});
/* ===========================
   views.set_scope_box
=========================== */
const ViewsSetScopeBoxShape = {
    viewIds: z.array(z.number().int()).optional(),
    viewNames: z.array(z.string()).optional(),
    scopeBoxName: z.string(), // requerido
    cropActive: z.boolean().optional(),
};
const ViewsSetScopeBoxSchema = z.object(ViewsSetScopeBoxShape);
server.registerTool("doc_views_set_scope_box", {
    title: "Set Scope Box on Views",
    description: "Asigna una scope box (por nombre) a un conjunto de vistas, y opcionalmente activa el crop.",
    inputSchema: asInputShape(ViewsSetScopeBoxShape),
}, async (rawArgs) => {
    const args = ViewsSetScopeBoxSchema.parse(rawArgs);
    const result = await postRevit("views.set_scope_box", args);
    return asText(result);
});
// Arrancar stdio
const transport = new StdioServerTransport();
await server.connect(transport);
