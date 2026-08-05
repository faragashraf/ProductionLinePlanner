export interface PermissionRequirementDescriptor {
  permission?: string;
  requireAny?: string | string[];
  requireAll?: string | string[];
  visibleForRoles?: readonly string[];
  hiddenForRoles?: readonly string[];
  showWithoutChildren?: boolean;
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

  const hasPermission = data['permission'] !== undefined;
  const hasAny = data['requireAny'] !== undefined;
  const hasAll = data['requireAll'] !== undefined;

  const modeCount = Number(hasPermission) + Number(hasAny) + Number(hasAll);
  if (modeCount === 0) {
    return { hasMetadata: false, isMalformed: true };
  }

  const permission = hasPermission ? normalizeSingle(data['permission']) : undefined;
  const requireAny = hasAny ? normalizeList(data['requireAny']) : undefined;
  const requireAll = hasAll ? normalizeList(data['requireAll']) : undefined;

  const malformed = modeCount !== 1 || (permission === undefined && requireAny === undefined && requireAll === undefined);
  const requirement = malformed
    ? undefined
    : permission !== undefined
      ? { permission }
      : requireAny !== undefined
        ? { requireAny }
        : { requireAll };

  return malformed
    ? { hasMetadata: true, isMalformed: true }
    : { hasMetadata: true, isMalformed: false, requirement };
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
