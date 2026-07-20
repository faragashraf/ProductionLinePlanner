export function normalizeSearchText(value: unknown): string {
  return String(value ?? '')
    .trim()
    .replace(/\s+/g, ' ')
    .toLocaleLowerCase();
}

export function matchesSearchTerm(searchTerm: string, values: readonly unknown[]): boolean {
  const normalizedSearch = normalizeSearchText(searchTerm);
  return normalizedSearch.length === 0 || values.some(value => normalizeSearchText(value).includes(normalizedSearch));
}
