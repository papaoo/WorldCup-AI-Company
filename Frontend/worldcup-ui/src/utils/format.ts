export function pct(value: number): string {
  return `${Math.round(value * 1000) / 10}%`;
}
