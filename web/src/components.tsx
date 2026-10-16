import { humanize } from "./model";

const TONE: Record<string, string> = {
  Serviceable: "ok",
  Complete: "ok",
  Closed: "muted",
  InProgress: "info",
  Released: "muted",
  InBuild: "info",
  InRefurb: "warn",
  OnHold: "warn",
  Open: "bad",
  Scrapped: "bad",
  Cancelled: "bad",
  Pending: "muted",
  Build: "muted",
  Refurb: "accent",
};

export function Badge({ value, tone }: { value: string; tone?: string }) {
  return <span className={`badge ${tone ?? TONE[value] ?? "muted"}`}>{humanize(value)}</span>;
}

export function ErrorBanner({ message, details }: { message: string | null; details?: string[] }) {
  if (!message) return null;
  return (
    <div className="error-banner" role="alert">
      <strong>{message}</strong>
      {details && details.length > 0 && (
        <ul>
          {details.map((d) => (
            <li key={d}>{d}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function Progress({ done, total }: { done: number; total: number }) {
  return (
    <div className="progress" title={`${done} of ${total} operations signed off`}>
      <div style={{ width: `${total ? (100 * done) / total : 0}%` }} />
    </div>
  );
}
