import { matchesSearchTerm, normalizeSearchText } from './text-search.utils';

describe('text search utilities', () => {
  it('normalizes null values, outer whitespace, repeated whitespace, and English casing', () => {
    expect(normalizeSearchText(null)).toBe('');
    expect(normalizeSearchText('  CUT   Line  ')).toBe('cut line');
    expect(normalizeSearchText('  مرحلة   القص  ')).toBe('مرحلة القص');
  });

  it('matches a partial normalized query against any available value', () => {
    expect(matchesSearchTerm('  line  ', ['STG-01', 'Cut Line', null])).toBeTrue();
    expect(matchesSearchTerm('مرحلة', [undefined, 'مرحلة القص'])).toBeTrue();
    expect(matchesSearchTerm('packing', ['CUT-01', 'مرحلة القص'])).toBeFalse();
  });
});
