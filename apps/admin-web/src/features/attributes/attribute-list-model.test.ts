import { describe, expect, it } from 'vitest';
import {
  parseAliases,
  readAttributeListState,
  validateAttributeDraft,
  writeAttributeListState,
} from './attribute-list-model';

describe('attribute list model', () => {
  it('round-trips supported filters and paging', () => {
    const params = writeAttributeListState({
      q: 'screen',
      status: 'ARCHIVED',
      dataType: 'NUMBER',
      includeDeleted: true,
      page: 3,
      pageSize: 50,
    });

    expect(readAttributeListState(params)).toEqual({
      q: 'screen',
      status: 'ARCHIVED',
      dataType: 'NUMBER',
      includeDeleted: true,
      page: 3,
      pageSize: 50,
    });
  });

  it('normalizes aliases without changing their spelling', () => {
    expect(parseAliases('Screen diagonal, Display size\nScreen diagonal')).toEqual(['Screen diagonal', 'Display size']);
  });

  it('enforces type-specific option rules before submitting', () => {
    expect(validateAttributeDraft('Available', 'BOOLEAN', [{ key: '1', value: 'Yes', measurementUnitId: '' }]))
      .toContain('Boolean attributes use Yes/No and cannot have predefined options.');
    expect(validateAttributeDraft('Size', 'NUMBER', [{ key: '1', value: '42', measurementUnitId: '' }]))
      .toContain('Every numeric option needs a measurement unit.');
    expect(validateAttributeDraft('Colour', 'OPTION', []))
      .toContain('Add at least one option for an option-list attribute.');
  });
});
