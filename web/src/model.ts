import type { GenealogyNode, WorkOrderStatus, WorkOrderSummary } from "./types";

export interface TreeNode extends GenealogyNode {
  children: TreeNode[];
}

/**
 * Nests the API's flat genealogy rows (id + parentId) into a tree. Children are ordered
 * assemblies first, then by part number and serial, so engines sit above loose components.
 */
export function buildTree(nodes: GenealogyNode[]): TreeNode | null {
  const byId = new Map<number, TreeNode>(nodes.map((n) => [n.id, { ...n, children: [] }]));
  let root: TreeNode | null = null;
  for (const node of byId.values()) {
    const parent = node.parentId === null ? undefined : byId.get(node.parentId);
    if (parent) parent.children.push(node);
    else if (node.depth === 0) root = node;
  }
  const kindRank = { Vehicle: 0, Assembly: 1, Component: 2 } as const;
  const sort = (n: TreeNode) => {
    n.children.sort(
      (a, b) =>
        kindRank[a.partKind] - kindRank[b.partKind] ||
        a.partNumber.localeCompare(b.partNumber) ||
        a.serialNumber.localeCompare(b.serialNumber),
    );
    n.children.forEach(sort);
  };
  if (root) sort(root);
  return root;
}

/** Counts the nodes beneath (and including) a node that need attention. */
export function countIssues(node: TreeNode): number {
  const own = node.refurbDue || node.openNcrs > 0 || node.status !== "Serviceable" ? 1 : 0;
  return node.children.reduce((sum, c) => sum + countIssues(c), own);
}

export const BOARD_COLUMNS: { status: WorkOrderStatus; title: string }[] = [
  { status: "Released", title: "Released" },
  { status: "InProgress", title: "In progress" },
  { status: "OnHold", title: "On hold" },
  { status: "Complete", title: "Recently completed" },
];

/** Groups work orders into board columns; completed ones are capped to the most recent few. */
export function groupForBoard(workOrders: WorkOrderSummary[], completedLimit = 6): Map<WorkOrderStatus, WorkOrderSummary[]> {
  const columns = new Map<WorkOrderStatus, WorkOrderSummary[]>(BOARD_COLUMNS.map((c) => [c.status, []]));
  const newestFirst = [...workOrders].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
  for (const wo of newestFirst) columns.get(wo.status)?.push(wo);
  columns.set("Complete", columns.get("Complete")!.slice(0, completedLimit));
  return columns;
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" });
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-US", { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
}

export function humanize(status: string): string {
  return status.replace(/([a-z])([A-Z])/g, "$1 $2");
}
