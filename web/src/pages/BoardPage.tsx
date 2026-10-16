import { Link } from "react-router-dom";
import { api } from "../api";
import { Badge, ErrorBanner, Progress } from "../components";
import { useLoad } from "../hooks";
import { BOARD_COLUMNS, formatDate, groupForBoard } from "../model";
import type { VehicleSummary, WorkOrderSummary } from "../types";

export default function BoardPage() {
  const workOrders = useLoad(() => api.get<WorkOrderSummary[]>("/work-orders"), []);
  const vehicles = useLoad(() => api.get<VehicleSummary[]>("/vehicles"), []);
  const columns = groupForBoard(workOrders.data ?? []);

  return (
    <>
      <section className="vehicles">
        {vehicles.data?.map((v) => <VehicleCard key={v.serialNumber} vehicle={v} />)}
      </section>
      <ErrorBanner message={workOrders.error ?? vehicles.error} />
      <section className="board">
        {BOARD_COLUMNS.map((col) => (
          <div className="column" key={col.status}>
            <h2>
              {col.title} <span className="count">{columns.get(col.status)!.length}</span>
            </h2>
            {columns.get(col.status)!.map((wo) => (
              <WorkOrderCard key={wo.id} wo={wo} />
            ))}
          </div>
        ))}
      </section>
    </>
  );
}

function VehicleCard({ vehicle: v }: { vehicle: VehicleSummary }) {
  const ready = v.flightReadinessProblems.length === 0;
  return (
    <Link to={`/genealogy/${v.serialNumber}`} className={`vehicle ${ready ? "ready" : "not-ready"}`}>
      <div className="vehicle-head">
        <span className="vehicle-sn">{v.serialNumber}</span>
        <Badge value={v.status} />
      </div>
      <div className="vehicle-meta">
        {v.flights} flight{v.flights === 1 ? "" : "s"}
        {v.lastFlownAt && <> · last {formatDate(v.lastFlownAt)}</>}
      </div>
      <div className="readiness">
        {ready ? "Flight ready" : `${v.flightReadinessProblems.length} blocker${v.flightReadinessProblems.length > 1 ? "s" : ""}`}
      </div>
    </Link>
  );
}

function WorkOrderCard({ wo }: { wo: WorkOrderSummary }) {
  return (
    <Link to={`/work-orders/${wo.id}`} className="card">
      <div className="card-head">
        <span className="wo-number">{wo.number}</span>
        <Badge value={wo.type} />
        {wo.openNcrs > 0 && <Badge value={`${wo.openNcrs} NCR`} tone="bad" />}
      </div>
      <div className="card-serial">
        {wo.serialNumber} <span className="dim">{wo.partName}</span>
      </div>
      {wo.currentOperation && (
        <div className="card-op">
          <span className="station">{wo.currentOperation.stationCode}</span>
          {wo.currentOperation.sequence} · {wo.currentOperation.name}
        </div>
      )}
      {wo.holdReason && <div className="card-hold">{wo.holdReason}</div>}
      <Progress done={wo.completedOperations} total={wo.totalOperations} />
    </Link>
  );
}
