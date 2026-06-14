import { loadMeridianConfigForInvocation } from '../config/meridian-config.js';
const defaultPatterns = {
    test: [
        'tests/**/*.cs',
        'test/**/*.cs',
        '**/*.Tests/**/*.cs',
        '**/*.Test/**/*.cs',
        '**/*Tests.cs',
        '**/*.test.ts',
        '**/*.spec.ts',
        '**/*.test.tsx',
        '**/*.spec.tsx',
    ],
    migration: ['**/Migrations/*.cs', '**/Migrations/**/*.cs'],
    snapshot: ['**/*ModelSnapshot.cs'],
    generated: [
        '**/*.g.cs',
        '**/*.generated.cs',
        '**/*.Designer.cs',
        '**/*.designer.cs',
        '**/openapi.generated.ts',
        '**/graphql.generated.ts',
    ],
    buildArtifact: [
        '**/bin/**',
        '**/obj/**',
        '**/node_modules/**',
        '**/dist/**',
        '**/build/**',
        '**/coverage/**',
    ],
    documentation: ['**/*.md', '**/*.mdx', '**/*.txt'],
    configuration: [
        '**/appsettings.json',
        '**/appsettings.*.json',
        '**/meridian.json',
        '**/meridian.sample.json',
        '**/.env',
        '**/docker-compose*.yml',
        '**/docker-compose*.yaml',
    ],
};
const evaluationOrder = [
    'BuildArtifact',
    'Snapshot',
    'Migration',
    'Generated',
    'Test',
    'Documentation',
    'Configuration',
];
export function loadIndexedFileRoleClassifier(rootPath) {
    const config = loadMeridianConfigForInvocation(rootPath);
    const configured = config.local?.indexing?.fileRoles ?? config.global?.indexing?.fileRoles;
    const patterns = {
        test: configured?.test ?? defaultPatterns.test,
        migration: configured?.migration ?? defaultPatterns.migration,
        snapshot: configured?.snapshot ?? defaultPatterns.snapshot,
        generated: configured?.generated ?? defaultPatterns.generated,
        buildArtifact: configured?.buildArtifact ?? defaultPatterns.buildArtifact,
        documentation: configured?.documentation ?? defaultPatterns.documentation,
        configuration: configured?.configuration ?? defaultPatterns.configuration,
    };
    return (relativePath) => classify(relativePath, patterns);
}
function classify(relativePath, patterns) {
    const normalizedPath = normalize(relativePath);
    if (!normalizedPath) {
        return 'Unknown';
    }
    for (const role of evaluationOrder) {
        if (getPatterns(patterns, role).some(pattern => isMatch(normalizedPath, pattern))) {
            return role;
        }
    }
    return /\.(cs|ts|tsx|js|jsx)$/i.test(normalizedPath) ? 'Source' : 'Unknown';
}
function getPatterns(patterns, role) {
    switch (role) {
        case 'Test':
            return patterns.test;
        case 'Migration':
            return patterns.migration;
        case 'Snapshot':
            return patterns.snapshot;
        case 'Generated':
            return patterns.generated;
        case 'BuildArtifact':
            return patterns.buildArtifact;
        case 'Documentation':
            return patterns.documentation;
        case 'Configuration':
            return patterns.configuration;
        default:
            return [];
    }
}
function normalize(value) {
    return value.replace(/\\/g, '/').replace(/^\/+/, '');
}
function isMatch(path, pattern) {
    const normalizedPattern = normalize(pattern);
    if (normalizedPattern.startsWith('**/') && isMatch(path, normalizedPattern.slice(3))) {
        return true;
    }
    const regex = `^${escapeRegex(normalizedPattern
        .replace(/\*\*/g, '<<<DOUBLESTAR>>>')
        .replace(/\*/g, '<<<SINGLESTAR>>>'))
        .replace(/<<<SINGLESTAR>>>/g, '[^/]*')
        .replace(/<<<DOUBLESTAR>>>/g, '.*')}$`;
    return new RegExp(regex, 'i').test(path);
}
function escapeRegex(value) {
    return value.replace(/[|\\{}()[\]^$+?.]/g, '\\$&');
}
