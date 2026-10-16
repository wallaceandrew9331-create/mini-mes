import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { ApiError, api } from "../api";
import { Badge, ErrorBanner, Progress } from "../components";
import { useLoad, useStoredString } from "../hooks";
import { formatDate, formatDateTime } from "../model";
import type { Disposition, GenealogyResponse, Ncr, Traveler, TravelerOperation } from "../types";

interface Failure {
  message: string;
  details?: string[];
}

export default function TravelerPage() {
  const id = Number(useParams().id);
  const loaded = useLoad(() => api.getWithETag<Traveler>(`/work-orders/${id}`), [id]);
  const [signer, setSigner] = useStoredString("mes.signer", "");
  const [failure, setFailure] = useState<Failure | null>(null);
  const [ncrFor, setNcrFor] = useState<TravelerOperation | null>(null);

  const traveler = loaded.data?.data;
  const etag = loaded.data?.etag ?? null;

  /** Runs a write, adopting the returned traveler + ETag. A 412 means someone else got there first. */
  async function write(action: () => Promise<{ data: Traveler; etag: string | null } | void>) {
    setFailure(null);
    try {
      const result = await action();
      if (result) loaded.setData(result);
      else await loaded.reload();
    } catch (e) {
      if (e instanceof ApiError && e.status === 412) {
        await loaded.reload();
        setFailure({ message: "Someone else updated this traveler. It has been reloaded; check it and try again." });
      } else if (e instanceof ApiError) {
        setFailure({ message: e.message, details: e.details });
      } else throw e;
    }
  }

  if (loaded.error) return <ErrorBanner message={loaded.error} />;
  if (!traveler) return <p className="dim">Loading…</p>;

  const done = traveler.operations.filter((o) => o.status === "Complete").length;
  const canWork = traveler.status === "Released" || traveler.status === "InProgress";

  return (
    <div className="traveler">
      <div className="traveler-head">
        <div>
          <h1>
            {traveler.number} <Badge value={traveler.type} /> <Badge value={traveler.status} />
          </h1>
          <p>
            <Link to={`/genealogy/${traveler.serial.serialNumber}`}>{traveler.serial.serialNumber}</Link>{" "}
            {traveler.serial.partName} · {traveler.serial.partNumber} rev {traveler.serial.revision}
            {traveler.serial.lotNumber && <> · lot {traveler.serial.lotNumber}</>} · released {formatDate(traveler.createdAt)}
          </p>
          {traveler.holdReason && <p className="card-hold">On hold: {traveler.holdReason}</p>}
        </div>
        <div className="traveler-actions">
          <label>
            Signing as
            <input value={signer} onChange={(e) => setSigner(e.target.value)} placeholder="Your name" />
          </label>
          {canWork && (
            <button
              className="secondary"
              onClick={() => {
                const reason = prompt("Reason for hold?");
                if (reason) void write(() => api.post<Traveler>(`/work-orders/${id}/hold`, { reason }, etag));
              }}
            >
              Hold
            </button>
          )}
          {traveler.status === "OnHold" && (
            <button className="secondary" onClick={() => void write(() => api.post<Traveler>(`/work-orders/${id}/resume`, {}, etag))}>
              Resume
            </button>
          )}
        </div>
      </div>

      <Progress done={done} total={traveler.operations.length} />
      <ErrorBanner message={failure?.message ?? null} details={failure?.details} />

      <table className="ops">
        <thead>
          <tr>
            <th>Op</th>
            <th>Operation</th>
            <th>Station</th>
            <th>Status</th>
            <th>Sign-off</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {traveler.operations.map((op) => (
            <tr key={op.id} className={op.isCurrent ? "current" : op.status === "Complete" ? "done" : ""}>
              <td className="seq">{op.sequence}</td>
              <td>
                {op.isRework && <Badge value="Rework" tone="warn" />} {op.name}
              </td>
              <td title={op.stationName}>{op.stationCode}</td>
              <td>
                <Badge value={op.status} />
                {op.blockedByNcr && <Badge value="NCR hold" tone="bad" />}
              </td>
              <td className="dim">
                {op.signOff && (
                  <>
                    {op.signOff.signedBy} · {formatDateTime(op.signOff.signedAt)}
                  </>
                )}
              </td>
              <td className="row-actions">
                {op.isCurrent && canWork && (
                  <>
                    <button
                      disabled={!signer.trim() || op.blockedByNcr}
                      title={!signer.trim() ? "Enter your name first" : op.blockedByNcr ? "Disposition the open NCR first" : ""}
                      onClick={() =>
                        void write(() =>
                          api.post<Traveler>(`/work-orders/${id}/operations/${op.id}/sign-off`, { signedBy: signer }, etag),
                        )
                      }
                    >
                      Sign off
                    </button>
                    <button className="secondary" onClick={() => setNcrFor(op)}>
                      Raise NCR
                    </button>
                  </>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {ncrFor && (
        <NcrForm
          traveler={traveler}
          operation={ncrFor}
          raisedBy={signer}
          onCancel={() => setNcrFor(null)}
          onSubmit={async (serialNumber, description) => {
            await write(() =>
              api
                .post("/ncrs", { serialNumber, workOrderId: traveler.id, operationId: ncrFor.id, description, raisedBy: signer })
                .then(() => setNcrFor(null)),
            );
          }}
        />
      )}

      <h2>Nonconformances</h2>
      {traveler.ncrs.length === 0 && <p className="dim">None raised on this work order.</p>}
      {traveler.ncrs.map((ncr) => (
        <NcrRow
          key={ncr.id}
          ncr={ncr}
          signer={signer}
          onDisposition={(disposition, notes) =>
            void write(() => api.post(`/ncrs/${ncr.id}/disposition`, { disposition, by: signer, notes }).then(() => undefined))
          }
        />
      ))}
    </div>
  );
}

function NcrForm(props: {
  traveler: Traveler;
  operation: TravelerOperation;
  raisedBy: string;
  onCancel: () => void;
  onSubmit: (serialNumber: string, description: string) => Promise<void>;
}) {
  const root = props.traveler.serial.serialNumber;
  // The nonconforming part may be the work order's serial or anything installed in it.
  const tree = useLoad(() => api.get<GenealogyResponse>(`/serials/${root}/genealogy`), [root]);
  const [serial, setSerial] = useState(root);
  const [description, setDescription] = useState("");

  return (
    <form
      className="panel ncr-form"
      onSubmit={(e) => {
        e.preventDefault();
        void props.onSubmit(serial, description);
      }}
    >
      <h3>
        Raise NCR at op {props.operation.sequence}: {props.operation.name}
      </h3>
      <label>
        Nonconforming serial
        <select value={serial} onChange={(e) => setSerial(e.target.value)}>
          {(tree.data?.nodes ?? []).map((n) => (
            <option key={n.id} value={n.serialNumber}>
              {"  ".repeat(n.depth)}
              {n.serialNumber} · {n.partName}
            </option>
          ))}
        </select>
      </label>
      <label>
        Description
        <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={3} required />
      </label>
      <div className="form-actions">
        <button type="submit" disabled={!props.raisedBy.trim() || !description.trim()}>
          Raise NCR
        </button>
        <button type="button" className="secondary" onClick={props.onCancel}>
          Cancel
        </button>
        {!props.raisedBy.trim() && <span className="dim">Enter your name above first.</span>}
      </div>
    </form>
  );
}

function NcrRow({ ncr, signer, onDisposition }: { ncr: Ncr; signer: string; onDisposition: (d: Disposition, notes: string) => void }) {
  const [notes, setNotes] = useState("");
  return (
    <div className={`panel ncr ${ncr.status === "Open" ? "open" : ""}`}>
      <div className="ncr-head">
        <strong>{ncr.number}</strong> <Badge value={ncr.status} />
        {ncr.disposition && <Badge value={ncr.disposition} tone="accent" />}
        <span className="dim">
          {ncr.serialNumber} ({ncr.partNumber}) · raised by {ncr.raisedBy} {formatDateTime(ncr.raisedAt)}
        </span>
      </div>
      <p>{ncr.description}</p>
      {ncr.dispositionedBy && (
        <p className="dim">
          Dispositioned by {ncr.dispositionedBy} {formatDateTime(ncr.dispositionedAt!)}
          {ncr.dispositionNotes && <>: {ncr.dispositionNotes}</>}
        </p>
      )}
      {ncr.status === "Open" && (
        <div className="form-actions">
          <input value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="MRB notes" />
          {(["UseAsIs", "Rework", "Scrap"] as const).map((d) => (
            <button
              key={d}
              className={d === "Scrap" ? "danger" : "secondary"}
              disabled={!signer.trim() || (d === "Rework" && ncr.operationId === null)}
              onClick={() => onDisposition(d, notes)}
            >
              {d === "UseAsIs" ? "Use as is" : d}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
