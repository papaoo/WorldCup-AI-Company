import type {
  ProductAuditResult,
  ProductDataTrustItem,
  ProductMatchDetail,
  ProductMatchQueueItem,
  ProductModelHealthResult,
  ProductOverviewResult,
  ProductTeamResearchResult
} from "../types";

async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`${url} ${response.status}`);
  return response.json() as Promise<T>;
}

export function loadProductOverview(selectedMatchId?: string, limit = 18): Promise<ProductOverviewResult> {
  const params = new URLSearchParams({ limit: String(limit) });
  if (selectedMatchId) params.set("selected_match_id", selectedMatchId);
  return getJson<ProductOverviewResult>(`/api/worldcup/product/overview?${params.toString()}`);
}

export function loadProductMatches(limit = 72): Promise<ProductMatchQueueItem[]> {
  return getJson<ProductMatchQueueItem[]>(`/api/worldcup/product/matches?limit=${encodeURIComponent(String(limit))}`);
}

export function loadProductTeamResearch(matchLimit = 200): Promise<ProductTeamResearchResult> {
  return getJson<ProductTeamResearchResult>(`/api/worldcup/product/teams/research?match_limit=${encodeURIComponent(String(matchLimit))}`);
}

export function loadProductMatchDetail(matchId: string): Promise<ProductMatchDetail> {
  return getJson<ProductMatchDetail>(`/api/worldcup/product/matches/${encodeURIComponent(matchId)}`);
}

export function loadProductDataTrust(): Promise<ProductDataTrustItem[]> {
  return getJson<ProductDataTrustItem[]>("/api/worldcup/product/data-trust");
}

export function loadProductModelHealth(): Promise<ProductModelHealthResult> {
  return getJson<ProductModelHealthResult>("/api/worldcup/product/model-health");
}

export function loadProductAudit(matchId?: string, teamId?: string): Promise<ProductAuditResult> {
  const params = new URLSearchParams();
  if (matchId) params.set("match_id", matchId);
  if (teamId) params.set("team_id", teamId);
  return getJson<ProductAuditResult>(`/api/worldcup/product/audit?${params.toString()}`);
}
