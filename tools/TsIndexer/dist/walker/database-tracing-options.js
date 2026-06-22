import fs from 'node:fs';
import path from 'node:path';
const defaultOptions = {
    enabled: false,
    configurationVersion: 1,
    maxTablesPerOperation: 6,
    presets: [],
};
export function loadDatabaseTracingOptions(rootPath) {
    const dedicatedConfig = tryReadJson(path.join(rootPath, '.meridian', 'database-tracing.json'));
    const meridianConfig = tryReadJson(path.join(rootPath, 'meridian.json'));
    const rawSection = readDatabaseTracingSection(dedicatedConfig) ?? readDatabaseTracingSection(meridianConfig);
    if (!rawSection || typeof rawSection !== 'object') {
        return defaultOptions;
    }
    const section = rawSection;
    return {
        enabled: readBoolean(section.Enabled, section.enabled, defaultOptions.enabled),
        configurationVersion: readNumber(section.ConfigurationVersion, section.configurationVersion, defaultOptions.configurationVersion),
        maxTablesPerOperation: readNumber(section.MaxTablesPerOperation, section.maxTablesPerOperation, defaultOptions.maxTablesPerOperation),
        presets: readArray(section.Presets, section.presets).map(normalizePreset),
    };
}
function normalizePreset(raw) {
    const preset = typeof raw === 'object' && raw ? raw : {};
    return {
        id: readString(preset.Id, preset.id),
        strategy: readString(preset.Strategy, preset.strategy),
        provider: readString(preset.Provider, preset.provider),
        enabled: readBoolean(preset.Enabled, preset.enabled, true),
        languages: readStringArray(preset.Languages, preset.languages),
        readMethods: readStringArray(preset.ReadMethods, preset.readMethods),
        writeMethods: readStringArray(preset.WriteMethods, preset.writeMethods),
        statementArgumentIndexes: readNumberArray(preset.StatementArgumentIndexes, preset.statementArgumentIndexes, preset.SqlArgumentIndexes, preset.sqlArgumentIndexes, [0]),
        receiverTextHints: readStringArray(preset.ReceiverTextHints, preset.receiverTextHints),
        importModuleHints: readStringArray(preset.ImportModuleHints, preset.importModuleHints),
        statementTextProperties: readStringArray(preset.StatementTextProperties, preset.statementTextProperties, preset.CommandTextProperties, preset.commandTextProperties, ['CommandText']),
        tableSources: readStringArray(preset.TableSources, preset.tableSources),
    };
}
function readDatabaseTracingSection(root) {
    if (!root || typeof root !== 'object') {
        return undefined;
    }
    const obj = root;
    if (obj.DatabaseTracing && typeof obj.DatabaseTracing === 'object') {
        return obj.DatabaseTracing;
    }
    const codeMeridian = obj.CodeMeridian;
    if (codeMeridian && typeof codeMeridian === 'object') {
        const nested = codeMeridian.DatabaseTracing;
        if (nested && typeof nested === 'object') {
            return nested;
        }
    }
    return undefined;
}
function tryReadJson(filePath) {
    if (!fs.existsSync(filePath)) {
        return undefined;
    }
    try {
        return JSON.parse(fs.readFileSync(filePath, 'utf8'));
    }
    catch {
        return undefined;
    }
}
function readArray(...values) {
    for (const value of values) {
        if (Array.isArray(value)) {
            return value;
        }
    }
    return [];
}
function readBoolean(...values) {
    for (const value of values) {
        if (typeof value === 'boolean') {
            return value;
        }
    }
    return false;
}
function readNumber(...values) {
    for (const value of values) {
        if (typeof value === 'number' && Number.isFinite(value)) {
            return value;
        }
    }
    return 0;
}
function readString(...values) {
    for (const value of values) {
        if (typeof value === 'string' && value.trim().length > 0) {
            return value.trim();
        }
    }
    return '';
}
function readStringArray(...values) {
    return readArray(...values)
        .filter((value) => typeof value === 'string')
        .map(value => value.trim())
        .filter(value => value.length > 0);
}
function readNumberArray(...values) {
    return readArray(...values)
        .filter((value) => typeof value === 'number' && Number.isFinite(value));
}
