import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api";
import { Badge, ErrorBanner } from "../components";
import { useLoad } from "../hooks";
import { formatDate } from "../model";
import type { LotSummary, RecallResponse } from "../types";

export default function RecallPage() {
  const [params, setParams] = useSearchParams();
  const lot = params.get("lot") ?? "";
  const lots = useLoad(() => api.get<LotSummary[]>("/lots"), []);
  const recall = useLoad(
    () => (lot ? api.get<RecallResponse>(`/recall?lot=${encodeURIComponent(lot)}`) : Promise.resolve(null)),
    [lot],
  );
  const r = recall.data;

  return (
    <>
      <form className="search" onSubmit={(e) => e.preventDefault()}>
        <label>
          Suspect lot{" "}
          <select value={lot} onChange={(e) => setParams(e.target.value ? { lot: e.target.value } : {})}>
            <option value="">Choose a lot…</option>
            {lots.data?.map((l) => (
              <option key={l.lotNumber} value={l.lotNumber}>
                {l.lotNumber} · {l.partNumber} ({l.serials} serials)
              </option>
            ))}
          </select>
        </label>
        <span className="dim">Which flown vehicles contain a part from this lot?</span>
      </form>
      <ErrorBanner message={recall.error ?? lots.error} />

      {r && (
        <>
          <h2>Affected flown vehicles</h2>
          {r.affectedVehicles.length === 0 && <p className="dim">No part from this lot has flown.</p>}
          <section className="vehicles">
            {r.affectedVehicles.map((v) => (
              <Link key={v.serialNumber} to={`/genealogy/${v.serialNumber}`} className="vehicle not-ready">
                <div className="vehicle-head">
                  <span className="vehicle-sn">{v.serialNumber}</span>
                </div>
                <div className="vehicle-meta">
                  {v.flightsWithLotParts} flight{v.flightsWithLotParts === 1 ? "" : "s"} with lot parts
                </div>
                <div className="readiness">
                  {v.suspectSerialsInstalledNow.length > 0
                    ? `Still aboard: ${v.suspectSerialsInstalledNow.join(", ")}`
                    : "No suspect parts aboard now"}
                </div>
              </Link>
            ))}
          </section>

          <h2>Every serial in lot {r.lotNumber}</h2>
          <table className="ops">
            <thead>
              <tr>
                <th>Serial</th>
                <th>Status</th>
                <th>Where it is now</th>
                <th>Flights</th>
              </tr>
            </thead>
            <tbody>
              {r.parts.map((p) => (
                <tr key={p.serialNumber}>
                  <td>
                    <Link to={`/genealogy/${p.serialNumber}`}>{p.serialNumber}</Link>{" "}
                    <span className="dim">
                      {p.partNumber} rev {p.revision}
                    </span>
                  </td>
                  <td>
                    <Badge value={p.status} />
                  </td>
                  <td>{p.currentPath.length > 1 ? p.currentPath.join(" › ") : <span className="dim">Not installed</span>}</td>
                  <td>
                    {p.flights.length === 0 ? (
                      <span className="dim">Never flown</span>
                    ) : (
                      p.flights.map((f) => (
                        <div key={`${f.vehicle}-${f.flightNumber}`}>
                          {f.vehicle} F{f.flightNumber} · {f.mission} <span className="dim">{formatDate(f.flownAt)}</span>
                        </div>
                      ))
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </>
  );
}
