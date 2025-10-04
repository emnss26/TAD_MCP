// mcp_query/src/index.ts
import "dotenv/config";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
// z no es estrictamente necesario aquí, pero lo dejamos por consistencia con otros paquetes
import { z } from "zod";
import { postRevit } from "./bridge.js";
import { snapshotContext } from "../../mcp_common/src/context.js";

// Servidor MCP
const server = new McpServer({
  name: "mcp-query",
  version: "1.0.0",
});

// Helper: responder como texto (JSON pretty)
const asText = (obj: unknown) => ({
  content: [{ type: "text" as const, text: JSON.stringify(obj, null, 2) }],
});

// Reutilizamos un “schema sin args”
const NoArgs: Record<string, never> = {};

/* ============================
   levels.list
   ============================ */
server.registerTool(
  "levels_list",
  {
    title: "List levels",
    description: "Devuelve todos los niveles del documento.",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("levels.list", {}))
);

/* ============================
   walltypes.list
   ============================ */
server.registerTool(
  "walltypes_list",
  {
    title: "List wall types",
    description: "Tipos de muro disponibles.",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("walltypes.list", {}))
);

/* ============================
   view.active
   ============================ */
server.registerTool(
  "activeview_info",
  {
    title: "Active view info",
    description: "Información de la vista activa (id, nombre, tipo, nivel si aplica).",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("view.active", {}))
);

/* ============================
   views.list
   ============================ */
server.registerTool(
  "views_list",
  {
    title: "List views",
    description: "Lista de vistas no-template.",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("views.list", {}))
);

/* ============================
   schedules.list
   ============================ */
server.registerTool(
  "schedules_list",
  {
    title: "List schedules",
    description: "Lista de schedules.",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("schedules.list", {}))
);

/* ============================
   materials.list
   ============================ */
server.registerTool(
  "materials_list",
  {
    title: "List materials",
    description: "Lista de materiales del documento.",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("materials.list", {}))
);

/* ============================
   categories.list
   ============================ */
server.registerTool(
  "categories_list",
  {
    title: "List categories",
    description: "Categorías del documento (BuiltInCategory incluido).",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("categories.list", {}))
);

/* ============================
   families.types.list
   ============================ */
server.registerTool(
  "families_types_list",
  {
    title: "List family types",
    description: "Lista de FamilySymbol (familia, tipo, categoría).",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("families.types.list", {}))
);

/* ============================
   links.list
   ============================ */
server.registerTool(
  "links_list",
  {
    title: "List Revit links",
    description: "Instancias de Revit Link (id, nombre, pinned).",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("links.list", {}))
);

/* ============================
   imports.list
   ============================ */
server.registerTool(
  "imports_list",
  {
    title: "List CAD imports",
    description: "ImportInstance en el documento.",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("imports.list", {}))
);

/* ============================
   worksets.list
   ============================ */
server.registerTool(
  "worksets_list",
  {
    title: "List worksets",
    description: "Lista de worksets (id, nombre, tipo).",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("worksets.list", {}))
);

/* ============================
   textnotes.find
   ============================ */
server.registerTool(
  "textnotes_find",
  {
    title: "Find text notes in active view",
    description: "Devuelve TextNotes (id, texto) de la vista activa.",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("textnotes.find", {}))
);

/* ============================
   ducttypes.list
   ============================ */
server.registerTool(
  "ducttypes_list",
  {
    title: "List duct types",
    description: "Tipos de ducto (MEP) disponibles.",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("ducttypes.list", {}))
);

/* ============================
   pipetypes.list
   ============================ */
server.registerTool(
  "pipetypes_list",
  {
    title: "List pipe types",
    description: "Tipos de tubería disponibles.",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("pipetypes.list", {}))
);

/* ============================
   cabletraytypes.list
   ============================ */
server.registerTool(
  "cabletraytypes_list",
  {
    title: "List cable tray types",
    description: "Tipos de charola disponibles.",
    inputSchema: NoArgs,
  },
  async () => asText(await postRevit("cabletraytypes.list", {}))
);

/* ============================
   selection.info
   ============================ */
const SelectionInfoShape = {
  includeParameters: z.boolean().optional(),
  topNParams: z.number().int().min(1).max(1000).optional(),
};
const SelectionInfoSchema = z.object(SelectionInfoShape);

server.registerTool(
  "selection_info",
  {
    title: "Selection info",
    description: "Devuelve información de los elementos actualmente seleccionados.",
    inputSchema: SelectionInfoShape,
  },
  async (args: z.infer<typeof SelectionInfoSchema>) =>
    asText(await postRevit("selection.info", args))
);

/* ============================
   element.info
   ============================ */
const ElementInfoShape = {
  elementId: z.number().int(),
  includeParameters: z.boolean().optional(),
  topNParams: z.number().int().min(1).max(1000).optional(),
};
const ElementInfoSchema = z.object(ElementInfoShape);

server.registerTool(
  "element_info",
  {
    title: "Element info by id",
    description: "Devuelve información detallada de un elemento por Id.",
    inputSchema: ElementInfoShape,
  },
  async (args: z.infer<typeof ElementInfoSchema>) =>
    asText(await postRevit("element.info", args))
);

server.registerTool(
  "query_context_snapshot",
  {
    title: "Revit Context Snapshot",
    description: "Devuelve vista activa, niveles, grids, tipos clave, worksets, selección, etc.",
    inputSchema: { type: "object", properties: { cacheSec: { type: "number" } } }
  },
  async (args: any) => {
    const snap = await snapshotContext(postRevit, { cacheSec: args?.cacheSec ?? 30 });
    return { content: [{ type: "text", text: JSON.stringify(snap, null, 2) }] };
  }
);

// stdio
const transport = new StdioServerTransport();
await server.connect(transport);
