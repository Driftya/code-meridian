import { describe, expect, it } from 'vitest';
import { walkTypeScript } from '../src/walker.js';
import { useTempProject } from './walker-test-helpers.js';

const project = useTempProject();

describe('TypeScript database tracing', () => {
  it('indexes Prisma model reads from the shared database tracing config', () => {
    writeDatabaseTracingConfig(`
{
  "DatabaseTracing": {
    "Enabled": true,
    "Presets": [
      {
        "Id": "prisma",
        "Strategy": "Prisma",
        "Provider": "Prisma",
        "Enabled": true,
        "Languages": [ "TypeScript" ],
        "ReadMethods": [ "findMany" ],
        "WriteMethods": [ "create" ],
        "ReceiverTextHints": [ "prisma", "tx" ],
        "ImportModuleHints": [ "@prisma/client" ],
        "TableSources": [ "ReceiverMemberName" ]
      }
    ]
  }
}
`);
    project.writeFile(
      'orders.ts',
      `import { PrismaClient } from '@prisma/client';

const prisma = new PrismaClient();

export async function listOrders() {
  return prisma.order.findMany();
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.nodes).toContainEqual(expect.objectContaining({
      type: 'ExternalConcept',
      name: 'Prisma Reads order',
      properties: expect.objectContaining({
        externalKind: 'DatabaseOperation',
        provider: 'Prisma',
        operationType: 'Reads',
      }),
    }));
    expect(result.nodes).toContainEqual(expect.objectContaining({
      id: 'Proj::DatabaseTable::order',
      type: 'DatabaseTable',
      name: 'order',
    }));
    expect(result.edges).toContainEqual(expect.objectContaining({
      sourceId: 'Proj:Method:orders.ts:listOrders',
      targetId: expect.stringContaining('Proj::ExternalConcept::DatabaseOperation::'),
      type: 'Reads',
    }));
  });

  it('indexes Knex table writes from the shared database tracing config', () => {
    writeDatabaseTracingConfig(`
{
  "DatabaseTracing": {
    "Enabled": true,
    "Presets": [
      {
        "Id": "knex",
        "Strategy": "Knex",
        "Provider": "Knex",
        "Enabled": true,
        "Languages": [ "TypeScript" ],
        "ReadMethods": [ "select" ],
        "WriteMethods": [ "insert", "update" ],
        "ReceiverTextHints": [ "knex", "db" ],
        "ImportModuleHints": [ "knex" ],
        "TableSources": [ "FirstArgumentString", "FromMethodArgument", "IntoMethodArgument" ]
      }
    ]
  }
}
`);
    project.writeFile(
      'orders.ts',
      `import knex from 'knex';

const db = knex({});

export async function createOrder(order: { id: string }) {
  return db('orders').insert(order);
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.nodes).toContainEqual(expect.objectContaining({
      type: 'DatabaseTable',
      name: 'orders',
    }));
    expect(result.edges).toContainEqual(expect.objectContaining({
      sourceId: 'Proj:Method:orders.ts:createOrder',
      type: 'Writes',
    }));
  });

  it('indexes Neo4j Cypher reads and resolves query text from identifiers', () => {
    writeDatabaseTracingConfig(`
{
  "DatabaseTracing": {
    "Enabled": true,
    "Presets": [
      {
        "Id": "neo4j-cypher",
        "Strategy": "Cypher",
        "Provider": "Neo4j",
        "Enabled": true,
        "Languages": [ "TypeScript" ],
        "ReadMethods": [ "run", "executeQuery" ],
        "WriteMethods": [ "run", "executeQuery" ],
        "StatementArgumentIndexes": [ 0 ],
        "ReceiverTextHints": [ "session", "tx", "driver" ],
        "ImportModuleHints": [ "neo4j-driver" ],
        "TableSources": [ "CypherText" ]
      }
    ]
  }
}
`);
    project.writeFile(
      'graph.ts',
      `import neo4j from 'neo4j-driver';

const query = 'MATCH (o:Order)-[:PLACED]->(c:Customer) RETURN o';

export async function loadOrders(session: { run: (query: string) => unknown }) {
  return session.run(query);
}
`,
    );

    const result = walkTypeScript(project.getRootPath(), 'Proj', project.listTypeScriptFiles());

    expect(result.nodes).toEqual(expect.arrayContaining([
      expect.objectContaining({ type: 'DatabaseTable', name: 'Order' }),
      expect.objectContaining({ type: 'DatabaseTable', name: 'PLACED' }),
      expect.objectContaining({ type: 'DatabaseTable', name: 'Customer' }),
    ]));
    expect(result.edges).toContainEqual(expect.objectContaining({
      sourceId: 'Proj:Method:graph.ts:loadOrders',
      type: 'Reads',
    }));
  });
});

function writeDatabaseTracingConfig(json: string): void {
  project.writeFile('.meridian/database-tracing.json', `${json.trim()}\n`);
}
