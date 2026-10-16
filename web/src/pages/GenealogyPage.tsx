import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ApiError, api } from "../api";
import { Badge, ErrorBanner } from "../components";
import { useLoad } from "../hooks";
import { buildTree, countIssues, formatDate, type TreeNode } from "../model";
import type { GenealogyResponse, HistoryResponse, SerialRef } from "../types";

export default function GenealogyPage() {
  const serial = useParams().serial ?? "B-07";
  const navigate = useNavigate();
  const genealogy = useLoad(() => api.get<GenealogyResponse>(`/serials/${encodeURIComponent(serial)}/genealogy`), [serial]);
  const [selected, setSelected] = useState(serial);
  useEffect(() => setSelected(serial), [serial]);

  const tree = genealogy.data ? buildTree(genealogy.data.nodes) : null;

  return (
    <>
      <SerialSearch current={serial} onPick={(sn) => navigate(`/genealogy/${sn}`)} />
      <ErrorBanner message={genealogy.error} />
      {tree && (
        <div className="genealogy">
          <div className="tree-pane">
            {genealogy.data!.flightReadinessProblems && (
              <FlightPanel
                vehicle={serial}
                problems={genealogy.data!.flightReadinessProblems}
                onFlown={() => void genealogy.reload()}
              />
            )}
            <ul className="tree">
              <TreeItem node={tree} selected={selected} onSelect={setSelected} />
            </ul>
          </div>
          <HistoryPane serialNumber={selected} />
        </div>
      )}
    </>
  );
}

function SerialSearch({ current, onPick }: { current: string; onPick: (sn: string) => void }) {
  const [q, setQ] = useState("");
  const matches = useLoad(() => (q.length < 1 ? Promise.resolve([]) : api.get<SerialRef[]>(`/serials?q=${encodeURIComponent(q)}`)), [q]);
  return (
    <form
      className="search"
      onSubmit={(e) => {
        e.preventDefault();
        if (q.trim()) onPick(q.trim().toUpperCase());
      }}
    >
      <input list="serials" value={q} onChange={(e) => setQ(e.target.value)} placeholder={`Serial number (showing ${current})`} />
      <datalist id="serials">
        {matches.data?.map((s) => (
          <option key={s.serialNumber} value={s.serialNumber}>
            {s.partName}
          </option>
        ))}
      </datalist>
      <button type="submit">Show</button>
      <span className="dim quick">
        Vehicles:{" "}
        {["B-07", "B-09", "B-11"].map((sn) => (
          <Link key={sn} to={`/genealogy/${sn}`}>
            {sn}
          </Link>
        ))}
      </span>
    </form>
  );
}

function TreeItem({ node, selected, onSelect }: { node: TreeNode; selected: string; onSelect: (sn: string) => void }) {
  const [open, setOpen] = useState(node.depth < 2 || countIssues(node) > 0);
  const issues = countIssues(node);
  return (
    <li>
      <div className={`node ${node.serialNumber === selected ? "selected" : ""}`} onClick={() => onSelect(node.serialNumber)}>
        {node.children.length > 0 ? (
          <button
            className="toggle"
            aria-label={open ? "Collapse" : "Expand"}
            onClick={(e) => {
              e.stopPropagation();
              setOpen(!open);
            }}
          >
            {open ? "▾" : "▸"}
          </button>
        ) : (
          <span className="toggle" />
        )}
        <span className="node-sn">{node.serialNumber}</span>
        <span className="node-part">
          {node.partName} <span className="dim">{node.partNumber} rev {node.revision}</span>
        </span>
        {node.lotNumber && <span className="lot">lot {node.lotNumber}</span>}
        <span className="flights" title="flights since refurb / refurb interval">
          {node.totalFlights} flt
          {node.refurbIntervalFlights !== null && (
            <>
              {" "}
              · {node.flightsSinceRefurb}/{node.refurbIntervalFlights}
            </>
          )}
        </span>
        {node.status !== "Serviceable" && <Badge value={node.status} />}
        {node.refurbDue && <Badge value="Refurb due" tone="warn" />}
        {node.openNcrs > 0 && <Badge value={`${node.openNcrs} NCR`} tone="bad" />}
        {!open && issues > 0 && <span className="dim">({issues} below)</span>}
      </div>
      {open && node.children.length > 0 && (
        <ul>
          {node.children.map((c) => (
            <TreeItem key={c.id} node={c} selected={selected} onSelect={onSelect} />
          ))}
        </ul>
      )}
    </li>
  );
}

function FlightPanel({ vehicle, problems, onFlown }: { vehicle: string; problems: string[]; onFlown: () => void }) {
  const [mission, setMission] = useState("");
  const [error, setError] = useState<{ message: string; details: string[] } | null>(null);
  const ready = problems.length === 0;

  return (
    <div className={`panel flight-panel ${ready ? "ready" : "not-ready"}`}>
      <strong>{ready ? `${vehicle} is flight ready` : `${vehicle} is not flight ready`}</strong>
      {!ready && (
        <ul>
          {problems.map((p) => (
            <li key={p}>{p}</li>
          ))}
        </ul>
      )}
      <form
        className="form-actions"
        onSubmit={async (e) => {
          e.preventDefault();
          try {
            await api.post(`/vehicles/${vehicle}/flights`, { mission });
            setMission("");
            setError(null);
            onFlown();
          } catch (err) {
            if (err instanceof ApiError) setError({ message: err.message, details: err.details });
            else throw err;
          }
        }}
      >
        <input value={mission} onChange={(e) => setMission(e.target.value)} placeholder="Mission name" required />
        <button type="submit">Record flight</button>
      </form>
      <ErrorBanner message={error?.message ?? null} details={error?.details} />
    </div>
  );
}

function HistoryPane({ serialNumber }: { serialNumber: string }) {
  const history = useLoad(() => api.get<HistoryResponse>(`/serials/${encodeURIComponent(serialNumber)}/history`), [serialNumber]);
  const h = history.data;
  if (history.error) return <ErrorBanner message={history.error} />;
  if (!h) return <aside className="history-pane dim">Loading…</aside>;

  return (
    <aside className="history-pane">
      <h2>
        {h.serial.serialNumber} <Badge value={h.serial.status} />
      </h2>
      <p className="dim">
        {h.serial.partName} · {h.serial.partNumber} rev {h.serial.revision}
        {h.serial.lotNumber && <> · lot {h.serial.lotNumber}</>}
      </p>
      <div className="stats">
        <div>
          <span className="stat">{h.totalFlights}</span>flights
        </div>
        <div>
          <span className="stat">
            {h.flightsSinceRefurb}
            {h.refurbIntervalFlights !== null && <small>/{h.refurbIntervalFlights}</small>}
          </span>
          since refurb
        </div>
        <div>
          <span className="stat small">{h.installedIn ?? "—"}</span>installed in
        </div>
      </div>
      <ol className="timeline">
        {[...h.events].reverse().map((e, i) => (
          <li key={i} className={`ev-${e.kind}`}>
            <span className="ev-date">{formatDate(e.at)}</span>
            <span className="ev-title">
              {e.workOrderId ? <Link to={`/work-orders/${e.workOrderId}`}>{e.title}</Link> : e.title}
            </span>
            {e.detail && <span className="ev-detail">{e.detail}</span>}
          </li>
        ))}
      </ol>
    </aside>
  );
}
