/** Error carrying the API's RFC 7807 problem details. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly details: string[] = [],
  ) {
    super(message);
  }
}

export interface ApiResult<T> {
  data: T;
  /** The work order version, for If-Match on the next write. */
  etag: string | null;
}

async function request<T>(method: string, path: string, body?: unknown, etag?: string | null): Promise<ApiResult<T>> {
  const headers: Record<string, string> = {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (etag) headers["If-Match"] = etag;

  const res = await fetch(`/api${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    const problem = await res.json().catch(() => ({}));
    throw new ApiError(res.status, problem.detail ?? problem.title ?? res.statusText, problem.details ?? []);
  }
  const data = res.status === 204 ? (undefined as T) : ((await res.json()) as T);
  return { data, etag: res.headers.get("ETag") };
}

export const api = {
  get: async <T>(path: string) => (await request<T>("GET", path)).data,
  getWithETag: <T>(path: string) => request<T>("GET", path),
  post: <T>(path: string, body?: unknown, etag?: string | null) => request<T>("POST", path, body ?? {}, etag),
};
