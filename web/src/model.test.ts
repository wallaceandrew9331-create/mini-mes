import { describe, expect, it } from "vitest";
import { buildTree, countIssues, groupForBoard } from "./model";
import type { GenealogyNode, WorkOrderSummary } from "./types";

function node(id: number, parentId: number | null, depth: number, partial: Partial<GenealogyNode> = {}): GenealogyNode {
  return {
    id,
    parentId,
    depth,
    serialNumber: `SN-${id}`,
    partNumber: "P",
    partName: "Part",
    partKind: "Component",
    revision: "A",
    lotNumber: null,
    status: "Serviceable",
    refurbIntervalFlights: null,
    totalFlights: 0,
    flightsSinceRefurb: 0,
    refurbDue: false,
    openNcrs: 0,
    ...partial,
  };
}

describe("buildTree", () => {
  it("nests flat rows by parentId regardless of row order", () => {
    const tree = buildTree([node(3, 2, 2), node(1, null, 0, { partKind: "Vehicle" }), node(2, 1, 1)]);
    expect(tree?.id).toBe(1);
    expect(tree?.children.map((c) => c.id)).toEqual([2]);
    expect(tree?.children[0].children.map((c) => c.id)).toEqual([3]);
  });

  it("orders assemblies before components", () => {
    const tree = buildTree([
      node(1, null, 0, { partKind: "Vehicle" }),
      node(2, 1, 1, { partKind: "Component", partNumber: "AA" }),
      node(3, 1, 1, { partKind: "Assembly", partNumber: "ZZ" }),
    ]);
    expect(tree?.children.map((c) => c.id)).toEqual([3, 2]);
  });

  it("returns null for an empty result", () => {
    expect(buildTree([])).toBeNull();
  });
});

describe("countIssues", () => {
  it("counts refurb-due, open-NCR and non-serviceable nodes in the subtree", () => {
    const tree = buildTree([
      node(1, null, 0, { status: "InRefurb" }),
      node(2, 1, 1, { refurbDue: true }),
      node(3, 2, 2, { openNcrs: 2 }),
      node(4, 1, 1),
    ])!;
    expect(countIssues(tree)).toBe(3);
  });
});

describe("groupForBoard", () => {
  const wo = (id: number, status: WorkOrderSummary["status"], updatedAt: string): WorkOrderSummary => ({
    id,
    number: `WO-${id}`,
    type: "Build",
    status,
    holdReason: null,
    serialNumber: "X",
    partNumber: "P",
    partName: "Part",
    currentOperation: null,
    completedOperations: 0,
    totalOperations: 1,
    openNcrs: 0,
    updatedAt,
  });

  it("puts work orders in status columns, newest first, and caps completed", () => {
    const columns = groupForBoard(
      [
        wo(1, "Complete", "2026-01-01"),
        wo(2, "Complete", "2026-03-01"),
        wo(3, "Complete", "2026-02-01"),
        wo(4, "InProgress", "2026-01-05"),
      ],
      2,
    );
    expect(columns.get("Complete")!.map((w) => w.id)).toEqual([2, 3]);
    expect(columns.get("InProgress")!.map((w) => w.id)).toEqual([4]);
    expect(columns.get("Released")).toEqual([]);
  });

  it("leaves cancelled work orders off the board", () => {
    const columns = groupForBoard([wo(1, "Cancelled", "2026-01-01")]);
    expect([...columns.values()].flat()).toEqual([]);
  });
});
