export interface PermissionRequirementDescriptor {
  permission?: string;
  requireAny?: string | string[];
  requireAll?: string | string[];
}

export interface ParsedPermissionRequirement {
  hasMetadata: boolean;
  isMalformed: boolean;
  requirement?: PermissionRequirementDescriptor;
}

export function parsePermissionRequirement(data: Record<string, unknown> | undefined): ParsedPermissionRequirement {
  if (!data) {
    return { hasMetadata: false, isMalformed: false };
  }

  const rawValues = [data['permission'], data['requireAny'], data['requireAll']];
  const hasMetadata = rawValues.some((value) => value !== undefined);
  if (!hasMetadata) {
    return { hasMetadata: false, isMalformed: false };
  }

  const single = normalizeSingle(data['permission']);
  const any = normalizeList(data['requireAny']);
  const all = normalizeList(data['requireAll']);
  const malformed =
    (data['permission'] !== undefined && !single) ||
    (data['requireAny'] !== undefined && !any) ||
    (data['requireAll'] !== undefined && !all) ||
    [single, any, all].filter(Boolean).length !== 1;

  return malformed
    ? { hasMetadata: true, isMalformed: true }
    : { hasMetadata: true, isMalformed: false, requirement: { permission: single, requireAny: any, requireAll: all } };
}

function normalizeSingle(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : undefined;
}

function normalizeList(value: unknown): string | string[] | undefined {
  if (typeof value === 'string') {
    return normalizeSingle(value);
  }

  if (!Array.isArray(value)) {
    return undefined;
  }

  const items = value
    .filter((item): item is string => typeof item === 'string')
    .map((item) => item.trim())
    .filter(Boolean);
  return items.length > 0 && items.length === value.length ? items : undefined;
}
