import type {
  AttributeBlueprintDataType,
  AttributeBlueprintDataTypeFilter,
  AttributeBlueprintStatusFilter,
} from '@mkbj/api-graphql';

export interface AttributeListState {
  q: string;
  status?: AttributeBlueprintStatusFilter;
  dataType?: AttributeBlueprintDataTypeFilter;
  includeDeleted: boolean;
  page: number;
  pageSize: number;
}

export interface AttributeOptionDraft {
  key: string;
  optionId?: string;
  value: string;
  measurementUnitId: string;
}

const pageSizes = new Set([10, 25, 50, 100]);
const statuses = new Set<AttributeBlueprintStatusFilter>(['ACTIVE', 'INACTIVE', 'ARCHIVED']);
const dataTypes = new Set<AttributeBlueprintDataTypeFilter>(['TEXT', 'NUMBER', 'BOOLEAN', 'DATE', 'OPTION']);

export function readAttributeListState(params: URLSearchParams): AttributeListState {
  const page = Number(params.get('page'));
  const pageSize = Number(params.get('pageSize'));
  const status = params.get('status') as AttributeBlueprintStatusFilter | null;
  const dataType = params.get('type') as AttributeBlueprintDataTypeFilter | null;

  return {
    q: params.get('q')?.trim() ?? '',
    ...(status && statuses.has(status) ? { status } : {}),
    ...(dataType && dataTypes.has(dataType) ? { dataType } : {}),
    includeDeleted: params.get('deleted') === 'true',
    page: Number.isInteger(page) && page > 0 ? page : 1,
    pageSize: pageSizes.has(pageSize) ? pageSize : 25,
  };
}

export function writeAttributeListState(state: AttributeListState): URLSearchParams {
  const params = new URLSearchParams();
  if (state.q) params.set('q', state.q);
  if (state.status) params.set('status', state.status);
  if (state.dataType) params.set('type', state.dataType);
  if (state.includeDeleted) params.set('deleted', 'true');
  if (state.page > 1) params.set('page', String(state.page));
  if (state.pageSize !== 25) params.set('pageSize', String(state.pageSize));
  return params;
}

export function parseAliases(value: string): string[] {
  return [...new Set(value.split(/[\n,]/).map((alias) => alias.trim()).filter(Boolean))];
}

export function createOptionDraft(index: number): AttributeOptionDraft {
  return { key: `new-${index}`, value: '', measurementUnitId: '' };
}

export function validateAttributeDraft(
  name: string,
  dataType: AttributeBlueprintDataType,
  options: ReadonlyArray<AttributeOptionDraft>,
): string[] {
  const errors: string[] = [];
  if (!name.trim()) errors.push('Enter an attribute name.');
  if (name.trim().length > 200) errors.push('Attribute names cannot exceed 200 characters.');
  if (dataType === 'BOOLEAN' && options.length) errors.push('Boolean attributes use Yes/No and cannot have predefined options.');
  if (dataType === 'OPTION' && options.length === 0) errors.push('Add at least one option for an option-list attribute.');
  if (options.some((option) => !option.value.trim())) errors.push('Every predefined option needs a value.');
  if (dataType === 'NUMBER' && options.some((option) => !option.measurementUnitId)) errors.push('Every numeric option needs a measurement unit.');
  if (dataType !== 'NUMBER' && options.some((option) => option.measurementUnitId)) errors.push('Only numeric options can use a measurement unit.');

  const normalized = options.map((option) => `${option.value.trim().toLocaleLowerCase()}::${option.measurementUnitId}`);
  if (new Set(normalized).size !== normalized.length) errors.push('Predefined option values must be unique within the same unit.');
  return errors;
}

export function displayAttributeValue(value: string): string {
  return value.replace(/_/g, ' ').toLocaleLowerCase().replace(/(^|\s)\S/g, (character) => character.toLocaleUpperCase());
}
