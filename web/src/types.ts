// Mirrors the API's DTOs (backend/src/MiniMes.Api/Endpoints). Enums arrive as strings.

export type WorkOrderType = "Build" | "Refurb";
export type WorkOrderStatus = "Released" | "InProgress" | "OnHold" | "Complete" | "Cancelled";
export type SerialStatus = "InBuild" | "Serviceable" | "InRefurb" | "Scrapped";
export type Disposition = "UseAsIs" | "Rework" | "Scrap";

export interface OperationRef {
  sequence: number;
  name: string;
  stationCode: string;
}

export interface WorkOrderSummary {
  id: number;
  number: string;
  type: WorkOrderType;
  status: WorkOrderStatus;
  holdReason: string | null;
  serialNumber: string;
  partNumber: string;
  partName: string;
  currentOperation: OperationRef | null;
  completedOperations: number;
  totalOperations: number;
  openNcrs: number;
  updatedAt: string;
}

export interface SerialRef {
  serialNumber: string;
  partNumber: string;
  partName: string;
  revision: string;
  lotNumber: string | null;
  status: SerialStatus;
}

export interface TravelerOperation {
  id: number;
  sequence: number;
  name: string;
  stationCode: string;
  stationName: string;
  isRework: boolean;
  status: "Pending" | "Complete";
  isCurrent: boolean;
  signOff: { signedBy: string; signedAt: string; notes: string | null } | null;
  blockedByNcr: boolean;
}

export interface Ncr {
  id: number;
  number: string;
  serialNumber: string;
  partNumber: string;
  workOrderId: number | null;
  operationId: number | null;
  description: string;
  raisedBy: string;
  raisedAt: string;
  status: "Open" | "Closed";
  disposition: Disposition | null;
  dispositionedBy: string | null;
  dispositionedAt: string | null;
  dispositionNotes: string | null;
}

export interface Traveler {
  id: number;
  number: string;
  type: WorkOrderType;
  status: WorkOrderStatus;
  holdReason: string | null;
  serial: SerialRef;
  createdAt: string;
  completedAt: string | null;
  version: number;
  operations: TravelerOperation[];
  ncrs: Ncr[];
}

export interface GenealogyNode {
  id: number;
  parentId: number | null;
  depth: number;
  serialNumber: string;
  partNumber: string;
  partName: string;
  partKind: "Component" | "Assembly" | "Vehicle";
  revision: string;
  lotNumber: string | null;
  status: SerialStatus;
  refurbIntervalFlights: number | null;
  totalFlights: number;
  flightsSinceRefurb: number;
  refurbDue: boolean;
  openNcrs: number;
}

export interface GenealogyResponse {
  serialNumber: string;
  nodes: GenealogyNode[];
  flightReadinessProblems: string[] | null;
}

export interface HistoryEvent {
  at: string;
  kind: "created" | "install" | "remove" | "build" | "refurb" | "flight" | "ncr";
  title: string;
  detail: string | null;
  workOrderId: number | null;
}

export interface HistoryResponse {
  serial: SerialRef;
  totalFlights: number;
  flightsSinceRefurb: number;
  refurbIntervalFlights: number | null;
  installedIn: string | null;
  events: HistoryEvent[];
}

export interface VehicleSummary {
  serialNumber: string;
  partNumber: string;
  status: SerialStatus;
  flights: number;
  lastFlownAt: string | null;
  flightReadinessProblems: string[];
}

export interface LotSummary {
  lotNumber: string;
  partNumber: string;
  serials: number;
}

export interface RecallResponse {
  lotNumber: string;
  parts: {
    serialNumber: string;
    partNumber: string;
    revision: string;
    status: SerialStatus;
    currentPath: string[];
    flights: { vehicle: string; flightNumber: number; mission: string; flownAt: string }[];
  }[];
  affectedVehicles: { serialNumber: string; flightsWithLotParts: number; suspectSerialsInstalledNow: string[] }[];
}
