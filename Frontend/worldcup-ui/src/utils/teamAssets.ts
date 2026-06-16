const FIFA_TO_FLAG_FILE: Record<string, string> = {
  ALG: "ALG",
  ANG: "ANG",
  ARG: "ARG",
  ARG2: "ARG",
  AUS: "AUS",
  AUT: "AUT",
  BEL: "BEL",
  BIH: "BIH",
  BRA: "BRA",
  CAN: "CAN",
  CHN: "CHN",
  CIV: "CIV",
  CMR: "CMR",
  COD: "COD",
  COL: "COL",
  CPV: "CPV",
  CRC: "CRC",
  CRO: "CRO",
  CUW: "CUW",
  CZE: "CZE",
  DEN: "DEN",
  ECU: "ECU",
  EGY: "EGY",
  ENG: "ENG",
  ESP: "ESP",
  FRA: "FRA",
  GER: "GER",
  GHA: "GHA",
  HAI: "HAI",
  HON: "HON",
  IRN: "IRN",
  IRQ: "IRQ",
  ISL: "ISL",
  ITA: "ITA",
  JOR: "JOR",
  JPN: "JPN",
  KOR: "KOR",
  KSA: "KSA",
  MAR: "MAR",
  MEX: "MEX",
  NED: "NED",
  NGA: "NGR",
  NGR: "NGR",
  NOR: "NOR",
  NZL: "NZL",
  PAN: "PAN",
  PAR: "PAR",
  POR: "POR",
  QAT: "QAT",
  RSA: "RSA",
  SCO: "SCO",
  SEN: "SEN",
  SLO: "SLO",
  SRB: "SRB",
  SUI: "SUI",
  SWE: "SWE",
  TGA: "TGA",
  TUN: "TUN",
  TUR: "TUR",
  UKR: "UKR",
  URU: "URU",
  USA: "USA",
  UZB: "UZB",
  WAL: "WAL"
};

export function flagSrcForCode(code: string): string | null {
  const fileCode = FIFA_TO_FLAG_FILE[cleanCode(code)];
  return fileCode ? `/generated/team-flags/${fileCode}.png` : null;
}

export function cleanCode(code: string): string {
  return (code || "TBA").trim().toUpperCase();
}

export function teamInitials(name: string, code: string): string {
  const normalized = name.trim();
  if (!normalized) return cleanCode(code).slice(0, 3);
  return normalized.length <= 3 ? normalized : normalized.slice(0, 2);
}

export function teamAccent(code: string): string {
  const value = cleanCode(code).split("").reduce((sum, char) => sum + char.charCodeAt(0), 0);
  const accents = ["gold", "green", "red", "blue", "clay", "ink"];
  return accents[value % accents.length];
}
