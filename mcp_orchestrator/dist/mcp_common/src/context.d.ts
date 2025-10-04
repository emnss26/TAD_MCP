import { z } from "zod";
export declare const ContextOpts: z.ZodObject<{
    cacheSec: z.ZodDefault<z.ZodNumber>;
}, "strip", z.ZodTypeAny, {
    cacheSec: number;
}, {
    cacheSec?: number | undefined;
}>;
type Ctx = z.infer<typeof ContextOpts>;
export declare function snapshotContext(postRevit: (action: string, args: any) => Promise<any>, opts?: Partial<Ctx>): Promise<any>;
export {};
