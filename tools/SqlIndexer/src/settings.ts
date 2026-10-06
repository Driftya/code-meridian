import type { SqlSettings } from './contracts.js';

export function selectSqlSource(path: string, settings: SqlSettings): { dialect: string; scope: string } {
  const matching = (settings.sources ?? []).filter(rule => matchesSqlPattern(path, rule.pattern));
  if (matching.length > 1) throw new Error(`Conflicting SQL source rules for ${path}.`);
  const dialect = matching[0]?.dialect ?? settings.defaultDialect;
  const scope = matching[0]?.databaseScope ?? settings.databaseScope;
  if (!dialect || !scope || scope.length > 256 || /[\x00-\x1f]/.test(scope)) throw new Error(`Invalid SQL dialect or database scope for ${path}.`);
  return { dialect, scope };
}

export function matchesSqlPattern(path: string, pattern: string): boolean {
  let regex = '^';
  for (let i = 0; i < pattern.length; i++) {
    if (pattern[i] === '*' && pattern[i + 1] === '*') {
      i++;
      if (pattern[i + 1] === '/') { regex += '(?:.*/)?'; i++; } else regex += '.*';
    } else if (pattern[i] === '*') regex += '[^/]*';
    else if (pattern[i] === '?') regex += '[^/]';
    else regex += pattern[i].replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }
  return new RegExp(regex + '$').test(path);
}
